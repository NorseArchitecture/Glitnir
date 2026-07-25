# Svartalfheim — Foundational Primitives Design

**Date:** 2026-05-20
**Status:** Draft for review
**Owner:** Buvy
**Supersedes:** none
**Companion specs:** `2026-05-19-architecture-analyzers-design.md` (the `[MustConsume]` diagnostic will be implemented as `YGG201` in `Norse.Primitives.Architecture`; the attribute itself ships from this package)

> **Amended 2026-06-07 (error-vocabulary reconciliation, punch-list §1.6):** the "crossing the streams" boundary ruling. Primitives is smart about **scalar→domain conversion only**, so the six-case `Error` union (`Parse`/`Validation`/`NotFound`/`Unauthorized`/`Conflict`/`Aggregate`) collapsed to a `Failure` carrying a closed **`ParseFailure`** reason enum. Application error categories (validation/not-found/conflict) moved to the mediator's `Outcome<T>` / `ErrorCategory`; authorization and transport conditions to the host pipeline; batch accumulation (former `Collect` → `AggregateError`) to the mediator's validate step. Touched: §1, §2, §3, §4, §5, §6, §7, §10, §12, §13. See mediator spec §3.3/§7 for the receiving side.

---

## 1. Motivation

Every Norse layer that crosses a trust boundary — file ingestion, HTTP request handling, third-party API responses, message deserialization — must return a value that the caller cannot ignore and cannot mishandle silently. The default C# return shapes do not satisfy this:

- A bare return type (`int Parse(string)`) either succeeds or throws. Exceptions on a parse path are expensive, hide intent, and conflate "the input was invalid" with "something broke."
- The BCL `TryParse(out T)` pattern returns a `bool` and an `out` value. The boolean can be ignored; the `out` parameter can be read without checking the boolean; the *reason* a parse failed is lost.
- Returning `T?` collapses "absent," "invalid," and "default value" into one state. It violates §2.6 (hard-fail on ambiguity).
- Throwing for parse failures violates §2.7 (push errors upstream): exceptions surface at runtime, not at the layer that introduced the parse.

Primitives provides the foundational primitives that make the §2.6 + §2.7 contract enforceable rather than aspirational: `Result<T>` as a closed-set return shape, a closed `ParseFailure` reason for conversion failures, a parser gateway that wraps every `ISpanParsable<T>` type in `Result`-returning form, and the `[MustConsume]` attribute that lets the architecture analyzer reject discarded results at compile time. Application-level error categories (validation, not-found, conflict) and transport conditions (auth, unavailability) deliberately live elsewhere — Primitives is smart about conversion and dumb about everything else (§4.2).

Primitives is **the** forge. Every other Norse layer that crosses a boundary depends on it. Getting the shape right once means every consumer inherits the §2.6 + §2.7 guarantees for free; getting it wrong means retrofitting the foundation of the platform after dozens of consumers have settled on its API.

## 2. Scope and Non-Goals

### In scope (this spec)

- `Result<T>` and `Failure` (a success value, or a closed `ParseFailure` reason; native C# 15 union for `Result<T>`).
- The parser gateway (`Parser.ParseRequired<T>`, `Parser.ParseOptional<T>`) over `ISpanParsable<T>`.
- Result composition (`Map`, `Bind`, `Match`, `GetValueOrThrow`, `GetValueOrDefault`, aggregation, async siblings, and the `Result<T>?` "Present" variants).
- The `[MustConsume]` attribute itself.
- AOT + runtime-async tier policy for Primitives and its consumers.
- F# consumer support requirements.
- Testing and benchmarking strategy.

### Out of scope (separate specs)

- **`Money`** — currency, rounding rules, comparison semantics, ISO 4217 enumeration, FX-out-of-scope decisions. Earns its own spec; non-trivial.
- **UUID v5 namespace registry** — the static catalog and the deterministic ID generation pipeline.
- **`[MustConsume]` analyzer implementation** — the diagnostic itself ships from `norse-primitives-architecture` as `YGG201` (in the `Norse.Primitives.Architecture` analyzer package). This spec defines the *contract* (the attribute and the consumption rules); the architecture-analyzers spec is updated separately to implement it.
- **Domain value types** (`EmailAddress`, `UsZipCode`, `PolicyNumber`, etc.) — these compose against Primitives's parser gateway by implementing `ISpanParsable<T>` themselves; their definitions live in their owning contexts.

## 3. Architecture Overview

```
┌──────────────────────────────────────────────────────────────────┐
│                       Norse.Primitives (NuGet)                       │
│                                                                  │
│  ┌────────────────────────┐  ┌────────────────────────────────┐  │
│  │ Result<T>              │  │ Parser                         │  │
│  │ Success<T> / Failure   │  │ ParseRequired<T>   → Result<T> │  │
│  │ [MustConsume]          │  │ ParseOptional<T>   → Result<T>?│  │
│  │                        │  │                                │  │
│  │ Failure                │  └────────────────────────────────┘  │
│  │   ParseFailure (enum)  │                                      │
│  │   + bounded diagnostics│  ┌────────────────────────────────┐  │
│  │                        │  │ Composition (extension methods)│  │
│  │  → app errors: mediator│  │   Map / MapError / Bind        │  │
│  │                        │  │   Match / GetValueOr*          │  │
│  │                        │  │   MapAsync / BindAsync         │  │
│  └────────────────────────┘  │   Combine                      │  │
│                              │   MapPresent / BindPresent     │  │
│                              └────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────┘
                                  │
                                  │ referenced from
                                  ▼
┌──────────────────────────────────────────────────────────────────┐
│  norse-primitives-architecture (sibling submodule, separate spec)    │
│                                                                  │
│  Norse.Primitives.Architecture (Roslyn analyzers + BuildCheck)       │
│  YGG201 — [MustConsume] diagnostic                              │
│  (implements the contract this spec defines)                     │
│                                                                  │
│  Norse.Abstractions.Architecture (attribute model — the law      │
│  read at compile time)                                           │
└──────────────────────────────────────────────────────────────────┘
```

One assembly, one NuGet package for the primitives. The architecture analyzer that enforces `[MustConsume]` lives in a sibling submodule because all `YGG0xx` rules live in `Norse.Primitives.Architecture` (CLAUDE.md §5; architecture-analyzers spec): Primitives ships the runtime contract (the attribute + `Result<T>`); `Norse.Primitives.Architecture` ships the build-time enforcement (the analyzer); `Norse.Abstractions.Architecture` ships the declared law (the attribute model the analyzer reads). All three travel together from the `norse-primitives-architecture` submodule + this `Norse.Primitives` submodule.

## 4. `Result<T>` and `Failure`

### 4.1 Native C# 15 union representation

`Result<T>` is a closed two-case union backed by native C# 15 union syntax. Case types are `readonly record struct` for zero-allocation composition in hot parsing paths.

> **Preview-syntax caveat:** the exact form of a struct union (vs. class union) in C# 15 is still moving between previews. Microsoft Learn confirms both struct unions and class unions exist as of Preview 4, and the non-boxing access pattern (§4.3) requires struct cases inside a struct union. The implementation will pin the precise declaration syntax against the .NET 11 preview in use at the time. If preview 5 / RC shifts the keyword or constraint placement, the fix is contained inside Primitives — every consumer continues to write `Result<T>` regardless.

```csharp
namespace Norse.Primitives;

public readonly record struct Success<T>(T Value) where T : notnull;

public readonly record struct Failure(            // a parse/conversion failure — full shape in §4.2
	ParseFailure Reason,
	string Input,
	string ExpectedType,
	string? Format,
	string? Detail);

[MustConsume]
public union Result<T>(Success<T>, Failure) where T : notnull;
```

The `where T : notnull` constraint forbids `Result<T?>` and `Result<int?>` at the type-system level. The orthogonal concerns of *presence* (the outer nullability of `Result<T>?`) and *validity* (the inner `Success` / `Failure` of `Result<T>`) are separated by construction:

| Outer | Inner | Meaning | OpenAPI | EF column |
|---|---|---|---|---|
| `null` | — | Absent / not supplied | Optional, omitted from payload | NULL |
| `Result<T>` | `Success(v)` | Present and validated | Required, valid | NOT NULL with value |
| `Result<T>` | `Failure(err)` | Present but invalid | Required, failed validation | (rejected before insert) |

A request record using both:

```csharp
public sealed record CreatePolicyRequest(
	Result<PolicyNumber> PolicyNumber,        // required (must be present and parse)
	Result<EffectiveDate>? CommencementDate,  // optional (may be absent; if present, must parse)
	Result<Money> Premium,                    // required
	Result<PolicyNotes>? Notes);              // optional
```

OpenAPI generators mirror outer `?` to schema `required`. EF Core column nullability mirrors outer `?` to `NULL` / `NOT NULL`. Validation results live inside.

### 4.2 `Failure` and the `ParseFailure` reason

Primitives is smart about exactly one thing: turning a scalar (`ReadOnlySpan<char>`, a raw field) into a validated domain value. Its failure surface is scoped to that job and nothing else. A conversion either succeeds with a value or fails for one of a **closed set of conversion reasons**, expressed as an enum — not a rich union of domain or application categories.

```csharp
public enum ParseFailure
{
	Unspecified = 0,   // sentinel — never a real failure (CLAUDE.md §5 enum rule)
	Empty       = 1,   // required input was empty or whitespace
	Malformed   = 2,   // ISpanParsable<T>.TryParse returned false (includes a domain type rejecting its own input)
}
```

The enum is closed by design: adding a member is a deliberate breaking change — every exhaustive switch becomes a build error until updated, the §2.7 fail-upstream behavior we want. `Failure` carries the reason plus bounded diagnostics for logs and error rendering:

```csharp
public readonly record struct Failure(
	ParseFailure Reason,    // the closed-set conversion reason
	string Input,           // bounded; truncated to Failure.MaxInputLength (256)
	string ExpectedType,    // e.g., "DateOnly", "Int32", "PolicyNumber"
	string? Format,         // e.g., "yyyy-MM-dd" if an explicit format was given
	string? Detail)         // optional human-readable detail from richer parsers
{
	public const int MaxInputLength = 256;
}
```

**What is deliberately NOT here (the stream-uncrossing, 2026-06-07):** `NotFound`, `Conflict`, cross-field `Validation`, and authorization failures are **not** Primitives concerns and were removed from the old six-case `Error` union. A scalar→domain conversion cannot produce or even name them — they are *application* outcomes or *transport/host* conditions. Each relocates to the layer that actually owns it:

| Former `Error` case | New home | Shape |
|---|---|---|
| `ParseError` | **stays** — `ParseFailure` reason + `Failure` diagnostics | this section |
| `ValidationError` / `AggregateError` | **Mediator** — `ErrorCategory.Validation`, field-keyed, aggregated across the request's failed `Result<T>` fields | mediator spec §7 |
| `NotFoundError` | **Mediator** — `ErrorCategory.NotFound` | mediator spec §7 |
| `ConflictError` | **Mediator** — `ErrorCategory.Conflict` | mediator spec §7 |
| `UnauthorizedError` | **Norse host pipeline** — 401 (authn) / 403 (authz), rendered before the request reaches the mediator | hosting / mediator §7 |

The **bridge is one-directional and explicit**: at the request boundary the mediator's validate step inspects each `Result<T>` field on the request; any `Failure` is folded — aggregated across all failed fields — into a single `ErrorCategory.Validation` outcome. A Primitives `Failure` never travels deeper into the application as itself; it is translated once, at the edge. This is what makes "aggregate" a mediator concern, not a primitive one (decision-inputs: it was the old `AggregateError`, re-homed).

**Why a `record struct`:**

- Zero-allocation in the common path. `Result<int>` containing a `Failure` is a single boxed reference only if the union itself boxes; with the non-boxing access pattern enabled (§4.3), no allocation occurs.
- F# interop: a struct with named fields is the most portable shape across CLR languages.
- Equality is structural by default for `record struct`, which matches expected semantics ("same failure" = "same fields").

**Why `Input` is captured as a `string` rather than `ReadOnlySpan<char>`:** failures propagate across `async` boundaries and into log records. `Span` cannot cross those boundaries; allocation is the cost of correctness. The 256-character cap aligns with `EmailAddress`'s upper bound and covers any realistic boundary-parse input.

**Why `ExpectedType` is a `string`:** AOT-clean and reflection-free. The type *name* is the only piece consumers want; a `Type` reference adds reflection surface for no payoff.

### 4.3 Non-boxing access pattern

C# 15 unions default to a `Value` property of type `object?`, which boxes value-type case contents on read. Primitives opts every union into the **non-boxing access pattern** (`HasValue` + per-case strongly-typed accessors) so `Result<int>`, `Failure`, and its sibling structs are read without allocation.

Implementation detail handled by the union declaration; consumers see no API change. Benchmarks (§12) verify zero allocations on the success path for every BCL parser in the v1 set.

## 5. Pattern Matching `Result<T>`

C# 15 unions apply patterns to the union's `Value` property, not to the union itself. The canonical switch:

```csharp
return result switch
{
	Success<int>(var v) => UseValue(v),
	Failure f => HandleFailure(f),
};
```

**Footgun:** `result is Result<int> r` does **not** match. Patterns unwrap to the contained case type; `Result<int>` itself is not a case type. Consumers must match against `Success<T>` or `Failure` directly. The Primitives documentation calls this out explicitly in the type-level XML doc on `Result<T>`.

For language-agnostic exhaustive consumption that does not depend on union pattern matching, use `Match` (§7.1).

## 6. The Parser Gateway

### 6.1 API surface

A single static class with two entry points:

```csharp
public static class Parser
{
	public static Result<T> ParseRequired<T>(
		ReadOnlySpan<char> input,
		IFormatProvider provider)
		where T : ISpanParsable<T>, notnull;

	public static Result<T>? ParseOptional<T>(
		ReadOnlySpan<char> input,
		IFormatProvider provider)
		where T : ISpanParsable<T>, notnull;
}
```

Both methods declare `where T : ISpanParsable<T>, notnull`. The explicit `notnull` is required because `ISpanParsable<TSelf>`'s own constraint is `where TSelf : ISpanParsable<TSelf>?` — the trailing `?` permits nullable annotations, so the interface constraint alone does not satisfy `Result<T>`'s `notnull` requirement. Every BCL implementer is non-null in practice; the explicit constraint just makes the requirement visible at the type level.

**`ParseRequired<T>`** — input must be non-empty. Empty / whitespace input returns `Failure` (`ParseFailure.Empty`). Successful parse returns `Success(value)`.

**`ParseOptional<T>`** — input may be empty:
- empty / whitespace input → `null` (absent)
- non-empty input that parses → `Result<T>.Success(value)`
- non-empty input that fails → `Result<T>.Failure` (`ParseFailure.Malformed`)

`IFormatProvider` is **non-nullable**. Every parser invocation must declare a culture (`CultureInfo.InvariantCulture` is the typical choice at internal boundaries; ingestion paths declare the source's culture). This bakes §2.6 hard-fail-on-ambiguity into the type signature — code that did not declare a culture does not compile.

### 6.2 No `ParseExact` in v1

Format-required parsing (e.g., `DateOnly.ParseExact(input, "yyyy-MM-dd", culture)`) belongs to *domain types*, not the generic gateway. A `LossDate` value type wraps `DateOnly.ParseExact` internally with its declared format and implements `ISpanParsable<LossDate>`; consumers then call `Parser.ParseRequired<LossDate>(input, culture)` and get the format guarantee for free.

Adding a generic `ParseExact<T>(input, format, provider)` to the gateway invites a careless caller to mix cultures and formats, defeating §2.6. The format belongs in the domain type's definition where it is declared once and enforced uniformly.

### 6.3 BCL parser set

Every type in this list implements `ISpanParsable<T>` in .NET 10+ and is supported by the gateway with zero Primitives-side code:

- **Boolean:** `bool`
- **Integers:** `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `Int128`, `UInt128`, `nint`, `nuint`
- **Floating point:** `float`, `double`, `Half`, `decimal`
- **Identifiers:** `Guid`
- **Temporal:** `DateOnly`, `TimeOnly`, `DateTime`, `DateTimeOffset`, `TimeSpan`
- **Numeric (big):** `BigInteger`
- **Versioning:** `Version`
- **Network:** `IPAddress`, `IPNetwork`, `IPEndPoint`

**Intentionally excluded:** `Uri`. The BCL's `Uri.TryCreate` is permissive (accepts both absolute and relative; tolerates uncommon schemes) in ways that a parse-at-boundary use case usually wants to reject up front. The recommended path is a domain `Url` value type that wraps `Uri` with explicit validation (scheme allowlist, absolute-only, length cap) and implements `ISpanParsable<Url>` itself.

**Future additions** to the BCL parser set are not breaking changes (no existing call site changes); they simply enable `Parser.ParseRequired<NewType>` for new BCL types as they implement `ISpanParsable`.

## 7. Result Composition

All composition is provided as extension methods on `Result<T>` (and `Result<T>?` for the Present variants), keeping the case types and union declaration minimal.

### 7.1 Synchronous combinators on `Result<T>`

| Method | Signature | Use |
|---|---|---|
| `Map<T, U>` | `Result<T> → (T → U) → Result<U>` | Transform success value |
| `MapError` | `Result<T> → (Failure → Failure) → Result<T>` | Rewrap the failure (e.g., enrich `Detail`) |
| `Bind<T, U>` | `Result<T> → (T → Result<U>) → Result<U>` | Chain a fallible step |
| `Match<T, U>` | `Result<T> → (T → U) → (Failure → U) → U` | Exhaustive consumption to a single value |
| `GetValueOrThrow` | `Result<T> → T` | Boundary exit; throws `ResultFailureException` on Failure |
| `GetValueOrDefault` | `Result<T> → T → T` | Explicit fallback declared at the call site (deliberate, visible — *not* silent) |
| `IsSuccess` | `Result<T> → bool` | Boolean accessor (does NOT count as `[MustConsume]` consumption, §8) |
| `IsFailure` | `Result<T> → bool` | Boolean accessor (does NOT count as consumption) |

### 7.2 Asynchronous combinators on `Task<Result<T>>`

`MapAsync`, `BindAsync`, `MatchAsync` mirror the synchronous shapes for `Task<Result<T>>` so chains compose without manual unwrap/rewrap at every step. With `runtime-async=on` on the consumer assembly (§9), these compose without state-machine emission overhead.

### 7.3 Combinators on `Result<T>?` (the Present variants)

The outer nullability is part of the model (§4.1), so composition is provided at that level too:

| Method | Signature | Use |
|---|---|---|
| `MapPresent<T, U>` | `Result<T>? → (T → U) → Result<U>?` | Transform success when present; `null` passes through |
| `BindPresent<T, U>` | `Result<T>? → (T → Result<U>) → Result<U>?` | Chain when present; `null` passes through |
| `MatchPresent<T, U>` | `Result<T>? → (T → U) → (Failure → U) → U → U` | Exhaustive over three branches: success, failure, absent |

These exist in v1 because retrofitting them after consumers have written their own `null`-then-`Match` unwrap dance is more expensive than shipping them now.

### 7.4 Aggregation

```csharp
// Short-circuit on first failure
public static Result<ImmutableArray<T>> Combine<T>(IEnumerable<Result<T>> results);

// Tuple combinators for "parse N fields, fail if any" — arity 2 through 8
public static Result<(T1, T2)> Combine<T1, T2>(Result<T1> r1, Result<T2> r2);
public static Result<(T1, T2, T3)> Combine<T1, T2, T3>(Result<T1> r1, Result<T2> r2, Result<T3> r3);
// ... up through arity 8
```

**`Combine`** short-circuits on the first failure — the right default for parsing a fixed set of fields ("parse N, fail if any"). It returns a single `Failure`; there is no accumulation, because a `Result<T>` carries a single `ParseFailure` reason by design (§4.2).

**Accumulate-all is not a Primitives concern (2026-06-07 ruling).** The former `Collect` — which folded every failure into the now-deleted `AggregateError` — relocated to the **mediator's validate step**, where field-level failures across a request are aggregated into one `ErrorCategory.Validation` outcome (mediator spec §3.3). Batch ingest that must surface every row's failures (BDX-style files, bulk endpoints) aggregates at that layer, not here: the primitive stays single-failure; aggregation is an application concern.

### 7.5 Monad laws

The combinators satisfy the standard monad laws and are verified by property tests (§12):

- `Map(identity)` ≡ identity
- `Map(f).Map(g)` ≡ `Map(g ∘ f)` (composition)
- `Bind(Success)` ≡ identity (left-identity)
- `Success(x).Bind(f)` ≡ `f(x)` (right-identity)
- `r.Bind(f).Bind(g)` ≡ `r.Bind(x => f(x).Bind(g))` (associativity)

A future refactor that breaks any of these is a property-test failure, not a debugging session.

## 8. The `[MustConsume]` Contract

### 8.1 The attribute

```csharp
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
public sealed class MustConsumeAttribute : Attribute;
```

Applied to `Result<T>` itself. Other "must-be-checked" types in the platform may opt in by applying the attribute (e.g., a future `Reservation<T>` for distributed-resource acquisition).

### 8.2 The rule (implemented by `YGG201` in `Norse.Primitives.Architecture`)

The diagnostic emits an **error** (severity `Error`, not `Warning`) when:

1. A method returning a `[MustConsume]` type is called as an expression statement with no consumption (`Parser.ParseRequired<int>(input, ci);` standalone).
2. A local of `[MustConsume]` type is declared but never read before going out of scope.
3. An `out` parameter of `[MustConsume]` type is bound but never consulted by the caller.

### 8.3 What counts as consumption

- Pattern match against either case (`switch`, `is Success<T>`, `is Failure`).
- Any composition method call (`Map`, `Bind`, `Match`, `GetValueOrThrow`, `GetValueOrDefault`, the async siblings, the `*Present` variants).
- Returning the result from the enclosing method (obligation passes upward).
- Storing in a field or property (obligation passes to the owner).
- Explicit discard: `_ = result;` (deliberate, visible — §2.7 fail-loud).
- Passing to a method that itself accepts a `[MustConsume]` parameter (the receiving method becomes responsible).

### 8.4 What does NOT count

- **`IsSuccess` / `IsFailure` boolean reads alone.** A boolean check without extracting the value or error is not consumption; the caller looked at whether something succeeded but never engaged with the outcome. Tightening this rule beyond "compose, match, or discard explicitly" rules out a class of "yeah we checked, we just didn't act on it" anti-patterns at compile time.
- Expression-statement discard with no `_ =` assignment.
- Passing to a sink (`Console.WriteLine`, `Trace.WriteLine`) without prior consumption.

### 8.5 Why this is a build error rather than a warning

Warnings get ignored; warnings as errors gets toggled off when a build is "almost passing." Silent-failure parses are the exact mode §2.7 exists to prevent. A `[MustConsume]` violation is a compile-time error treated identically to "method returns void, you wrote `return 5;`": malformed code that does not run.

## 9. AOT and Runtime-Async Tier Policy

### 9.1 Primitives itself

- **Target framework:** `net11.0`
- **AOT-clean:** no reflection, no `Activator.CreateInstance`, no dynamic code generation. `<IsAotCompatible>true</IsAotCompatible>` in the csproj.
- **Trimmer-friendly:** `<IsTrimmable>true</IsTrimmable>`; all references explicit.
- **Runtime-async: OFF** on the Primitives assembly. The case types are sync; the async extensions (`MapAsync`, `BindAsync`, `MatchAsync`) compose `Task<Result<T>>` and let the *consumer's* runtime-async setting determine state-machine emission.

Leaving Primitives runtime-async-neutral means it can be consumed by both runtime-async-on (server) and runtime-async-off (WASM, MAUI) projects without conflict. Per .NET 11 documentation, runtime-async is incompatible with WebAssembly; enabling it on Primitives would break every Components project downstream.

### 9.2 Consumer tier policy

**Amendment (2026-07-25):** `Norse.ReferenceData.*`, listed as a peer realm namespace in this section and in §11 below, dissolved 2026-06-11 — temporal contracts moved to Asgard, implementations to Midgard, universal content to a thin library named when real. See `docs/codenames.md` and `docs/the-crooked-path.md` #8.

| Tier | Projects | `runtime-async` | AOT |
|---|---|---|---|
| Server | `Norse.Hosting.Web.Server`, `Norse.Hosting.Worker`, `Norse.Hosting.Migrations.Service`, `{Company}.{Context}.Server`, `{Company}.{Context}.Worker`, the `Norse.Hosting.*`, `Norse.Infrastructure.*`, `Norse.ReferenceData.*`, and `Norse.Warehouse.*` libraries, internal CLI | **on** | JIT (reflection allowed) |
| External CLI | the AOT-only public CLI | **on** | AOT (`PublishAot=true`) |
| Browser / Native | `Norse.Hosting.Web.Client`, `Norse.Hosting.App`, every `{Company}.{Context}.Components`, `Norse.Abstractions.Components`, `Norse.Infrastructure.UI.Composition.Components` | **off** (mandatory — WASM incompatibility) | AOT (`PublishAot=true` for MAUI; browser-AOT for WASM) |

### 9.3 Per-tier opt-in snippets

`Directory.Build.props` snippet for the server tier:

```xml
<PropertyGroup Condition="'$(NorseTier)' == 'Server'">
	<Features>runtime-async=on</Features>
</PropertyGroup>
```

`Directory.Build.props` snippet for the WASM/MAUI tier:

```xml
<PropertyGroup Condition="'$(NorseTier)' == 'Client'">
	<UseRuntimeAsync>false</UseRuntimeAsync>
</PropertyGroup>
```

Each project declares its tier via `<NorseTier>Server</NorseTier>` (or `Client` / `ExternalCli` / `Library`) in its csproj. The shared `Directory.Build.props` (in the meta-repo's root, inherited via MSBuild's implicit-import behavior) translates that tier into the right runtime-async setting. New projects opt in by setting one property; the rest follows. (The property name keeps the `Norse` prefix because Norse is the umbrella meta-repo every realm hangs from — the tier is a platform-wide concept, not specific to one realm.)

## 10. Project Structure and NuGet Packaging

### 10.1 Repository layout

`Norse.Primitives` is a git submodule under the Norse meta-repo.

```
Norse.Primitives/
├── .gitignore
├── Directory.Build.props                # repo-local; inherits from meta-repo's root
├── LICENSE                              # MIT (matches Norse / Curie OSS posture)
├── README.md
├── Norse.Primitives.slnx
├── src/
│   └── Norse.Primitives/
│       └── Norse.Primitives.csproj
├── tests/
│   ├── Norse.Primitives.Tests/
│   │   └── Norse.Primitives.Tests.csproj
│   └── Norse.Primitives.Aot.SmokeTests/
│       └── Norse.Primitives.Aot.SmokeTests.csproj
└── benchmarks/
	└── Norse.Primitives.Benchmarks/
		└── Norse.Primitives.Benchmarks.csproj
```

### 10.2 Assembly and package

- **Single assembly:** `Norse.Primitives.dll`. Contains every public type listed in this spec: `Result<T>`, `Success<T>`, `Failure`, `ParseFailure`, the `Parser` gateway, all composition extension methods, the `[MustConsume]` attribute.
- **Single NuGet package:** `Norse.Primitives`. No `Norse.Primitives.Abstractions` split — §2.5 says ceremony must justify itself; splitting one attribute into a separate assembly to "avoid pulling in the rest" pulls in nothing the consumer was not going to want.

### 10.3 Versioning

Independent NuGet versioning. Major version bumps when:
- A new member is added to the `ParseFailure` enum (breaks exhaustive switches).
- A new public type is added that consumers must reference.
- A composition method signature changes (rare; should not happen post-v1).

Minor version bumps for additive surface that doesn't break exhaustive switches (new BCL parser support as the BCL adds `ISpanParsable<T>` implementations, new composition extension methods).

Patch version bumps for bug fixes and performance work that does not change the public surface.

## 11. F# Consumer Support

Primitives — and every realm namespace (`Norse.Abstractions.*`, `Norse.Infrastructure.*`, `Norse.ReferenceData.*` [dissolved 2026-06-11, see §9.2 amendment], `Norse.Hosting.*`, `{Company}.*`) — is a **first-class consumer target for F#**. The design choices above already accommodate F# interop:

1. **Public case types** — `Success<T>`, `Failure`, and the `ParseFailure` enum — so F# code can see them as concrete types.
2. **No `out` parameters in the public surface** — the F# idiom for fallible operations is `Match`-style lambdas, which Primitives provides.
3. **`IEnumerable<T>` / `ImmutableArray<T>` over `params T[]`** in aggregation signatures — F# composes naturally with `seq` and `list`.
4. **Structural-equality `record struct` cases** — F# pattern-match equality works as expected.

### Known limitation

F# tooling support for C# 15 native unions is uncertain in the .NET 11 timeframe. F# can call into a C# union (it is a struct with a `Value` property at runtime), but F# pattern matching against a C# union may not work as a first-class language feature until F# tooling catches up.

**Workaround for F# consumers until native support arrives:** use `Match`, the lambda-based exhaustive consumer:

```fsharp
let policyNumber =
	Parser.ParseRequired<PolicyNumber>(input, CultureInfo.InvariantCulture)
		.Match(
			(fun v -> Some v),
			(fun err -> None))
```

This works from any CLR language without depending on language-level union pattern support and is the spec's recommended F#-side pattern until native support arrives.

## 12. Testing and Benchmark Strategy

### 12.1 Frameworks

- **xUnit** as the test runner.
- **Shouldly** for assertions (per CLAUDE.md §4; FluentAssertions is excluded due to commercial license).
- **NSubstitute** for test doubles (per CLAUDE.md §4).
- **FsCheck** for property-based tests (monad laws, parser invariants).
- **BenchmarkDotNet** with `[MemoryDiagnoser]` for the benchmark project.

### 12.2 `Norse.Primitives.Tests` (unit + property)

Coverage targets:

- Every public method on `Result<T>`, `Parser`, every composition extension.
- Every case type's equality and `ToString` behavior.
- `Failure` construction, projection, and pattern-match path; `ParseFailure` exhaustiveness.
- The pattern-match unwrapping rule documented in §5 (a test that exhaustively switches `Result<int>` and would fail to compile if the rule changed under us).
- The `IFormatProvider` non-nullable contract (a compile-only test: code that omits the parameter fails to compile).

**Property tests (FsCheck):**

- Monad laws (§7.5): five properties.
- `ParseRequired` round-trips every BCL type that has a canonical `ToString`: `Parser.ParseRequired<int>(value.ToString(), ci).Match(v => v == value, _ => false)` for arbitrary `int`, `decimal`, `Guid`, `DateOnly`, etc.
- `Combine`: aggregation laws (associativity, identity element is `Combine(empty)`).

**Naming:** `Should_{behavior}_when_{condition}` per CLAUDE.md §5.

### 12.3 `Norse.Primitives.Aot.SmokeTests`

Publishes with `<PublishAot>true</PublishAot>`. Executes the parser surface against every BCL type in the v1 set (§6.3) under AOT. Catches trim warnings, missing `[DynamicallyAccessedMembers]` annotations, and "JIT works, AOT fails" surprises that would otherwise surface only when a consumer publishes AOT.

CI runs this project on every PR — the cost of a 30-second AOT smoke test is dwarfed by the cost of an AOT regression discovered downstream.

### 12.4 `Norse.Primitives.Benchmarks`

BenchmarkDotNet with the `[MemoryDiagnoser]` attribute on every benchmark class. Targets:

- `Parser.ParseRequired<T>` success path for each common BCL type: `int`, `decimal`, `DateOnly`, `DateTimeOffset`, `Guid`. **Zero-allocation goal.**
- `Parser.ParseOptional<T>` empty-input path (early-return). Zero-allocation goal.
- `Parser.ParseRequired<T>` failure path: allocates the `Failure` struct (no heap allocation expected; `Failure` is a struct and only the `Input` string clone counts).
- `Map` and `Bind` chains at depth 3 and depth 5. Zero-allocation goal.
- `Match` consumption with both lambdas. Zero-allocation goal where lambdas are static or cached.
- `Combine` over `ImmutableArray<Result<int>>` at sizes 10, 100, 1000. Should allocate only the resulting `ImmutableArray`.
- (`Collect` benchmark removed — accumulation relocated to the mediator's validate step, §7.4.)

**CI integration:** benchmarks do not run on every PR (slow). They run on a nightly job and on-demand via a workflow dispatch. A baseline is committed to the repo; the nightly job posts a diff if any benchmark regresses by more than 10% or starts allocating where it did not before.

## 13. Open Questions / Future Work

These are deliberately deferred and listed so future contributors do not re-litigate them silently.

1. **`Result<T>` async-stream composition.** Should we provide `IAsyncEnumerable<Result<T>>` combinators (`MapAsyncStream`, `BindAsyncStream`) in v1, or wait until an ingestion path actually needs them? Deferred until a consumer hits the friction.

2. ~~**`Error` case extensibility for partner-specific errors.**~~ **MOOT after the 2026-06-07 shrink.** A partner integration error is not a scalar→domain conversion failure and never was a `ParseFailure` concern — it belongs to the egress transport vocabulary (`HttpResult<T>` / `EgressError`, egress spec) or to the mediator's `Outcome<T>` once it crosses into application logic. Primitives's `ParseFailure` is closed and stays closed.

3. **JSON serialization shape for `Result<T>`.** Once `{Company}.{Context}.JsonApi` projects appear, what wire shape does `Result<T>` (and the mediator's `Outcome<T>`) take in JSON request and response bodies? Options: a discriminator-tagged envelope, two parallel fields, an HTTP-status-only convention with the shape preserved only internally. Deferred to the API envelope spec (§5 of CLAUDE.md, Tier 2 specs).

4. **F# native union pattern matching.** If F# tooling lands native C# 15 union pattern-match support during the .NET 11 timeframe, update §11 and the F# guidance docs. If it lags through .NET 11 GA, `Match` remains the recommended F# pattern indefinitely.

5. **Benchmarks regression-detection threshold.** 10% is the placeholder ceiling for "this is a regression." A real value comes from running the benchmark suite for a few weeks and observing the natural variance band on the CI runners.

## 14. Acceptance

This spec is acceptable for implementation when:

- All §4 types compile under .NET 11 / C# 15 with native union syntax.
- The `Parser` gateway in §6 returns `Result<T>` and `Result<T>?` for every BCL type listed in §6.3.
- The `[MustConsume]` attribute is declared in Primitives (§8.1), and the `norse-primitives-architecture` companion spec is updated with a `YGG201` section (in the `Norse.Primitives.Architecture` analyzer package) that implements §8.2–§8.4.
- Tier policy in §9 is applied in the meta-repo's `Directory.Build.props`.
- `Norse.Primitives.Tests` covers §12.2; `Norse.Primitives.Aot.SmokeTests` covers §12.3; `Norse.Primitives.Benchmarks` covers §12.4.
- F# consumer support per §11 is documented in the README.
