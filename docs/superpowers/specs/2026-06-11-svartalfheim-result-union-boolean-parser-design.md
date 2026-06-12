# Svartalfheim — `Result<T>` Union + BooleanParser (First Increment)

**Date:** 2026-06-11
**Status:** Approved for planning
**Owner:** Buvy
**Parent spec:** Glitnir `2026-05-20-svartalfheim-primitives-design.md` (as amended 2026-06-07)
**Scope ruling:** minimal vertical slice — this document narrows the parent spec to the first shippable increment and amends it where .NET 11 preview 5 reality diverges from its assumptions.

> **Amended 2026-06-11 (implementation findings):** three rulings made during implementation, each recorded inline: (1) the §6.1 gate resolved the *other* way — preview 5's runtime **ships** `UnionAttribute`/`IUnion` (the ref-pack XML-doc probe false-negatives; CS0436 at first use proved it), so no local declarations exist; (2) the compiler rejects `result is Result<T>` with **CS8121 at compile time** — stronger than the runtime-`false` the parent spec predicted — so that pin became XML documentation instead of a runtime test (§8.1); (3) two fail-loud hardenings from review: `Failure` gained a span-accepting constructor overload that bounds input before any string allocation (§4.3), and `Result(Failure)` rejects a `default(Failure)` smuggling the `Unspecified` sentinel (§4.1).

> **Amended 2026-06-11 (court filing and naming conformance):** three post-implementation rulings: (1) **this record and its plan were hoisted from Svartalfheim `docs/superpowers/` into Glitnir** — the court holds the record, the realms hold code; filed with the realm-prefixed names the corpus uses. (2) **The brand prefix is build-injected, never file-encoded:** project folders and `.csproj` files went brand-free (`src/Primitives/Primitives.csproj`, `tests/Primitives.Tests/Primitives.Tests.csproj`); the realm's `Directory.Build.props` sets `AssemblyName`/`RootNamespace` to `Norse.$(MSBuildProjectName)`, so a fork rebrands by changing `Norse` once — assembly identity (`Norse.Primitives.dll`) and namespace are unchanged. (3) **The realm solution file is lore-named:** `Svartalfheim.slnx` replaces `Norse.Primitives.slnx`, matching the repo. §6's scaffold tree is updated inline to the current shape; the plan remains the verbatim record of the original execution.

---

## 1. Motivation

Svartalfheim (`Norse.Primitives`) is the basis for every boundary crossing into the Norse ecosystem from untrusted sources. This increment proves the foundation end to end with the smallest honest slice: the `Result<T>` discriminated union built on native C# 15 union semantics, one hot-path parser (`BooleanParser`), and the tests that pin both — including the compiler contract itself.

Everything here deals in **scalar values only**: `"true"` becomes `true`, `"0"` becomes `false`, `"Y"` becomes `true`, `"No"` becomes `false`. Application-level error categories, aggregation, and transport conditions live elsewhere by prior ruling (parent spec §4.2, 2026-06-07 reconciliation).

## 2. Scope

### In scope

- `Result<T>` as a custom-pattern native union (`[Union]` + `IUnion`), zero-boxing on both paths.
- `Success<T>`, `Failure`, the closed `ParseFailure` enum.
- The `[MustConsume]` attribute (contract only; the `YGG201` analyzer ships from the sibling architecture repo later).
- `BooleanParser` with `ParseRequired` / `ParseOptional` entry points and the full Crucible vocabulary.
- `Norse.Primitives.Tests` covering union semantics, well-formedness, the `default` footgun, and the ported Crucible boolean matrix.
- Repository scaffold: `global.json`, `.editorconfig`, `Directory.Build.props`, `Norse.Primitives.slnx`, `src/`, `tests/`.

### Out of scope (later increments, in rough order)

- The generic `Parser.ParseRequired<T>` / `ParseOptional<T>` gateway over `ISpanParsable<T>`.
- Composition combinators (`Map`, `Bind`, `Match`, `Combine`, async siblings, `*Present` variants) and their FsCheck monad-law properties — they ship together so the laws are tested the moment the surface exists.
- `Norse.Primitives.Aot.SmokeTests` and `Norse.Primitives.Benchmarks`.
- Remaining hot-path parsers and domain value types.

## 3. The preview-5 amendment: custom union pattern, not the shorthand

The parent spec (§4.1/§4.3) assumed the shorthand declaration `public union Result<T>(Success<T>, Failure);` could opt into non-boxing access. The .NET 11 preview 5 documentation is explicit that it cannot: **a shorthand union declaration always lowers to a struct that stores its contents as `object?`** — every value-type case boxes on creation. `Result<bool>` via the shorthand would allocate on every successful parse, failing the parent spec's own zero-allocation goals (§4.3, §12.4) by construction.

The fix is the **custom union pattern**, which the language defines precisely for this case: any struct carrying `[System.Runtime.CompilerServices.Union]` that implements the basic union pattern (public single-parameter constructor per case type + `public object? Value { get; }`) is a full union type — implicit case conversions, pattern-match unwrapping, and exhaustive `switch` all included. Adding the **non-boxing access pattern** (`HasValue` + a `TryGetValue` overload per case type) makes the compiler route pattern matching through the typed accessors instead of `Value`, so nothing boxes.

Consumers are unaffected by this substitution: they write `Result<T>` and switch over `Success<T>` / `Failure` exactly as the parent spec promised. The cost is ~40 lines we own once, plus responsibility for the union well-formedness rules (soundness, stability, creation equivalence, access-pattern consistency) — all pinned by tests (§8).

## 4. Type surface

Five public types, namespace `Norse.Primitives`, single assembly `Norse.Primitives.dll`.

```csharp
namespace Norse.Primitives;

public readonly record struct Success<T>(T Value) where T : notnull;

// Non-positional (2026-06-11 amendment): explicit guarded constructors and get-only
// properties — no init accessors, no Deconstruct, so `with` can't bypass the guards.
public readonly record struct Failure
{
	public const int MaxInputLength = 256;

	public Failure(ParseFailure reason, string input, string expectedType,
		string? format = null, string? detail = null) { /* guards + truncation, §4.3 */ }
	public Failure(ParseFailure reason, ReadOnlySpan<char> input, string expectedType,
		string? format = null, string? detail = null) { /* bounds before allocating, §4.3 */ }

	public ParseFailure Reason { get; }       // the closed-set conversion reason
	public string Input { get; }              // bounded: truncated to MaxInputLength
	public string ExpectedType { get; }       // CLR type name, e.g. "Boolean"
	public string? Format { get; }            // declared format when one was given; null otherwise
	public string? Detail { get; }            // optional human-readable detail from richer parsers
}

public enum ParseFailure
{
	Unspecified = 0,   // sentinel — never produced by any parse path
	Empty       = 1,   // required input was empty or whitespace
	Malformed   = 2,   // input was present but not recognizable as the target type
}

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
public sealed class MustConsumeAttribute : Attribute;
```

### 4.1 `Result<T>` — the custom union

```csharp
[MustConsume]
[Union]
public readonly record struct Result<T> : IUnion where T : notnull
{
	readonly Success<T> _success;
	readonly Failure _failure;
	readonly State _state;          // private enum : byte { Default = 0, Success, Failure }

	// Union constructors — these define the case types.
	public Result(Success<T> value) { _success = value; _state = State.Success; }
	public Result(Failure value)    { _failure = value; _state = State.Failure; }

	// Basic union pattern. Boxes only when read directly — pattern matching never does.
	public object? Value => _state switch
	{
		State.Success => _success,
		State.Failure => _failure,
		_ => null,
	};

	// Non-boxing access pattern — the compiler prefers these for `switch`.
	public bool HasValue => _state != State.Default;
	public bool TryGetValue(out Success<T> value) { ... }
	public bool TryGetValue(out Failure value) { ... }

	public override string ToString() { ... }   // "Success(true)" / "Failure(Malformed, ...)"
}
```

Decisions:

- **`readonly record struct`** — structural equality over the typed fields comes free and matches expected semantics ("same outcome" = "same fields"). `ToString` is overridden manually so the record printer never touches the boxing `Value` property.
- **`Result(Failure)` guards the sentinel** *(2026-06-11 hardening)*: a `default(Failure)` — `Reason == Unspecified`, null strings — would smuggle past `Failure`'s own constructor guards via the implicit union conversion (`Result<bool> r = default(Failure);`). The constructor throws `ArgumentOutOfRangeException`, closing the one hole in the "no way to construct an invalid value" story. The cost is one branch on the failure path only — never the success hot path.
- **Both cases stored inline.** `Result<bool>` is a fat-ish struct (≈56 bytes: `Failure` is an enum plus four references), but copying beats allocating at BDX row volumes. Zero heap allocation on the success path; on the failure path only the `Input` string counts (parent spec §12.4 budget). The benchmarks increment may revisit storage if measurement disagrees.
- **`where T : notnull`** — forbids `Result<T?>` at the type system level. Presence (outer `Result<T>?`) and validity (inner case) stay orthogonal per parent spec §4.1.
- **No user-defined conversions.** Implicit conversions from `Success<T>` and `Failure` are union conversions supplied by the compiler. The Crucible's implicit `string → Result<T>` operator is deliberately **not** carried forward: it hid the parse behind an assignment and dragged a runtime parser registry behind it. Parsing is always an explicit, named call.
- **`[MustConsume]` applied** — the contract ships now; enforcement (`YGG201`) ships from the sibling architecture repo per parent spec §8.

### 4.2 The `default(Result<T>)` footgun

A defaulted struct union has `Value == null` — neither case matches, so an exhaustive two-arm switch over `default(Result<T>)` throws `SwitchExpressionException` at runtime. The union well-formedness rules require `default` to produce a null `Value`; making it masquerade as a `Failure` would violate soundness and break compiler assumptions. This is the same class of footgun as `default(ImmutableArray<T>)` and is handled the same way:

1. Documented prominently in the type-level XML doc.
2. Pinned by a test asserting the exact behavior (§8).
3. A future `YGG` analyzer rule flags `default(Result<T>)` and `new Result<T>()` at compile time (recorded for the architecture-analyzers spec).

### 4.3 `Failure` construction

The explicit constructor truncates `Input` to `MaxInputLength` (256) and fails loudly on bad arguments: the `Unspecified` sentinel is rejected (`ArgumentOutOfRangeException`), `input` must be non-null, and `expectedType` must be non-whitespace. Truncation of diagnostics is bounded-capture, not a silent fallback: the parse outcome is unaffected, and the cap keeps failures log-safe and span-source-friendly. No parse path ever produces `ParseFailure.Unspecified`; it exists only as the CLR-default sentinel so an uninitialized enum is distinguishable from a real reason.

*(2026-06-11 addition)* A second constructor overload accepts `ReadOnlySpan<char> input` and bounds it **before** materializing a string — at most `MaxInputLength` characters are ever allocated. Parsers pass their trimmed span directly; truncation knowledge lives in `Failure` once instead of being copied into every parser. String call sites bind to the string overload (exact match beats the implicit span conversion).

## 5. `BooleanParser`

The expert on exactly one thing: scalar text → `bool`. Smart about that subject, deliberately dumb about everything else — no culture, no format, no column context.

```csharp
namespace Norse.Primitives;

public static class BooleanParser
{
	public static Result<bool> ParseRequired(ReadOnlySpan<char> input);
	public static Result<bool>? ParseOptional(ReadOnlySpan<char> input);
}
```

### 5.1 Signature rulings

- **Honest signature** — no `IFormatProvider`. Boolean text is culture-insensitive; a parameter documented as ignored is a lie in the signature (the Crucible carried a dead `FormatHint` parameter behind a pragma — lesson applied). This sets the precedent for every culture-insensitive hot-path parser. The future generic gateway keeps its uniform non-nullable provider and simply does not forward it when routing `bool`.
- **Span-only entry points.** `string` converts implicitly to `ReadOnlySpan<char>`, and a null string becomes the empty span, which lands in the Empty/absent branch naturally. One pair of entry points covers every call shape.

### 5.2 Recognition algorithm

1. Trim leading/trailing whitespace from the span.
2. `bool.TryParse` — covers `true`/`false` in any casing.
3. Vocabulary lookup, ordinal-ignore-case, via `FrozenSet<string>.GetAlternateLookup<ReadOnlySpan<char>>` — the Crucible's alternate-lookup technique upgraded from `HashSet` to `FrozenSet`, since the sets are static readonly and frozen collections are optimized for exactly this read-only hot path. Zero allocation for every recognized input.

### 5.3 Vocabulary — the full Crucible set

Battle-tested against real MGA files; the vocabulary exists because untrusted sources actually send these.

| True | False |
|---|---|
| `true` | `false` |
| `t` | `f` |
| `yes` | `no` |
| `y` | `n` |
| `1` | `0` |
| `on` | `off` |
| `enabled` | `disabled` |
| `active` | `inactive` |
| `checked` | `unchecked` |
| `in` | `out` |

Matching is case-insensitive. Anything else is `Malformed` — no fuzzy matching, no numeric coercion beyond the literal `1`/`0` tokens (`"2"` fails loudly).

### 5.4 Outcome mapping

| Input | `ParseRequired` | `ParseOptional` |
|---|---|---|
| Empty / whitespace / null string | `Failure(Empty, "", "Boolean", null, null)` | `null` (absent) |
| Recognized vocabulary | `Success(value)` | `Success(value)` |
| Anything else | `Failure(Malformed, <trimmed input, truncated>, "Boolean", null, null)` | same `Failure` |

`Format` stays null (booleans have no format axis). `Detail` stays null (the reason plus the captured input says everything; prose would be maintenance without information).

### 5.5 Future gateway integration (recorded, not built)

When the generic `ISpanParsable<T>` gateway arrives, `Parser.ParseRequired<bool>` routes here via a `typeof(T) == typeof(bool)` branch — JIT-eliminated, AOT-clean, resolved at compile time. This replaces the Crucible's `ConcurrentDictionary` delegate registry, which deferred "no parser registered" to a runtime `InvalidOperationException`. Same specialist parsers; dispatch moved from runtime guessing to compile-time certainty.

## 6. Repository scaffold

```
Svartalfheim/                      # (tree as amended 2026-06-11 — brand-free project names, lore-named slnx)
├── global.json                    # SDK 11.0.100-, rollForward latestFeature, allowPrerelease; MTP test runner
├── .editorconfig                  # derived from Bifrost root — tabs, warnings ratcheted to errors
├── Directory.Build.props          # net11.0, LangVersion preview, Nullable enable, TreatWarningsAsErrors;
│                                  # AssemblyName/RootNamespace = Norse.$(MSBuildProjectName) — the one
│                                  # place the brand prefix exists (fork-rebrand point)
│                                  # (IsAotCompatible lives in the src csproj only — the test exe
│                                  #  shouldn't claim AOT compat; IsTrimmable is implied by it)
├── Svartalfheim.slnx
├── src/
│   └── Primitives/
│       └── Primitives.csproj      # assembly Norse.Primitives.dll
└── tests/
    └── Primitives.Tests/
        └── Primitives.Tests.csproj
```

- The repo must **build standalone** (it is its own clone target, not only a Bifrost submodule), so `global.json`, `.editorconfig`, and `Directory.Build.props` live here even though Bifrost carries siblings.
- AOT/trim properties are on from day one; the dedicated AOT smoke-test project arrives in its own increment.
- No NuGet packaging metadata in this increment; the package ships when the surface stabilizes.

### 6.1 Implementation gate: `UnionAttribute` / `IUnion` availability

First implementation step: verify whether the installed preview 5 runtime ships `System.Runtime.CompilerServices.UnionAttribute` and `IUnion` (preview 2 did not; the documentation says later previews would). If absent, declare them in the project exactly as the documentation sanctions, marked with a removal note for when the runtime catches up. Either way the public surface is identical.

**RESOLVED (2026-06-11): the runtime ships them.** The ref-pack XML-doc probe (`System.Runtime.xml`) false-negatived — the doc file lags the ref assembly — and temporary local declarations triggered CS0436 at `Result<T>`'s declaration, proving `System.Runtime 11.0.0.0` carries both types. The local declarations were deleted per their own lifecycle note; the assembly consumes the runtime types directly. Lesson recorded: probe ref *assemblies*, not their XML docs.

## 7. Error handling

There is exactly one failure vocabulary in this assembly: `Failure` carrying a closed `ParseFailure` reason. Per the 2026-06-07 reconciliation, validation/not-found/conflict belong to the mediator's `Outcome<T>`, authorization and transport conditions to the host pipeline, and batch accumulation to the mediator's validate step. Nothing in this increment throws on the parse path; exceptions are reserved for malformed *consumption* (`SwitchExpressionException` on a defaulted union — §4.2), never for malformed *input*.

## 8. Testing strategy

`Norse.Primitives.Tests` — xUnit v3 on Microsoft.Testing.Platform, Shouldly assertions, `Should_{behavior}_when_{condition}` naming.

### 8.1 Union semantics (pinning the compiler contract)

These tests guard against preview-compiler drift as much as against our own regressions:

- Implicit union conversion from each case type produces the expected case.
- Pattern-match unwrapping: `Success<bool>(var v)` and `Failure f` arms match contents. *(2026-06-11 amendment:)* `result is Result<bool>` is rejected by the compiler with **CS8121** — the parent spec §5 footgun is enforced at compile time, stronger than the predicted runtime-`false`. Since a compile error cannot be pinned by a runtime test, the planned test was removed and the behavior is documented in `Result<T>`'s XML remarks instead.
- An exhaustive two-arm switch compiles without a discard arm; nested property patterns (`Failure { Reason: … }`) lower correctly through the union unwrap.
- Wider instantiations (`Result<decimal>`, `Result<string>`) guard against `bool`-specific accidents; hash codes agree with equality.
- Well-formedness: `HasValue` and both `TryGetValue` overloads are observably consistent with `Value`; `Value` returns the boxed case when read directly.
- `default(Result<bool>)`: `Value` is null, `HasValue` is false, both `TryGetValue` overloads return false, and an exhaustive switch throws `SwitchExpressionException` (§4.2 pinned as documented behavior).
- Structural equality across both cases; `ToString` shapes; `[MustConsume]` is present on `Result<T>`.

### 8.2 `Failure`

- Constructor truncates `Input` at 256 characters; shorter inputs pass through untouched.
- Structural equality; no parse path produces `Unspecified`.

### 8.3 `BooleanParser` (the Crucible matrix, ported and extended)

- Every vocabulary pair across casings (`true`/`True`/`TRUE`, `InAcTiVe`, …) including the `\ttrue\n` whitespace cases — the Crucible theory matrix is the regression floor.
- `ParseRequired` on empty/whitespace/null-string → `Failure` with `Reason == Empty`.
- `ParseOptional` on empty/whitespace/null-string → `null`.
- Malformed inputs (`"maybe"`, `"2"`, `"unknown"`, `"truee"`, `"\tyess\n"`) → `Failure` with `Reason == Malformed`, `Input` and `ExpectedType` populated — assertions on the `Failure` shape that the Crucible tests never made.
- Zero-allocation verification for the recognized-input paths is deferred to the benchmarks increment; correctness only here.

## 9. Acceptance

This increment is complete when:

- `Norse.Primitives` compiles under .NET 11 preview 5 / C# preview with the custom union pattern, warnings as errors, AOT/trim analyzers clean.
- The five public types in §4 and `BooleanParser` in §5 exist with exactly the surfaces specified — nothing more.
- Every test category in §8 is implemented and green via `dotnet test` on Microsoft.Testing.Platform.
- The §6.1 union-runtime-types gate is resolved and recorded in the code (either consuming the runtime types or declaring sanctioned local copies).

## 10. Deferred decisions (recorded so nobody re-litigates silently)

> **Ledger update (2026-06-11, same day):** items 1, 3, and 4 are taken up by the second-increment spec, `2026-06-11-svartalfheim-pathway-proof-design.md` (gateway, combinators, evidence rigs). Item 2 remains with the architecture-analyzers spec.

1. **Storage strategy revisit** — inline `Failure` vs boxed, pending benchmark evidence (§4.1).
2. **`YGG` analyzer rule for `default(Result<T>)`** — belongs to the architecture-analyzers spec (§4.2).
3. **Gateway routing for hot-path specialists** — `typeof(T)` branch recorded in §5.5; designed when the gateway increment begins.
4. **Combinators + FsCheck monad laws** — next increment after this one proves the union shape.
