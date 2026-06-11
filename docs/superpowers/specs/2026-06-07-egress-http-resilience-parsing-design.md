# Egress: External HTTP, Resilience, and Parsing — Design

**Date:** 2026-06-07
**Status:** Spec — awaiting plan greenlight
**Realms:** Abstractions (contracts) · Infrastructure (implementations) · Norse Hosting/ServiceDefaults (connective tissue)

---

## 1. Context and Motivation

Every external HTTP integration written so far hand-rolls the same boilerplate. The `aichatweb` POC's `VoyageEmbeddingGenerator.GenerateAsync` is the canonical example: it calls `PostAsJsonAsync`, manually checks `IsSuccessStatusCode`, manually extracts an error body, manually throws a typed exception, manually guards an empty response, and manually validates batch alignment. Every future partner client would copy that shape — and in copying it, each author re-decides error handling, gets resilience subtly wrong, and sprinkles logging unevenly.

Separately, `Norse.Hosting.ServiceDefaults` already carries **two** resilience shapes — the stock `AddStandardResilienceHandler` (30-second total ceiling) and a bespoke `AddRetryAfterResilience` (2-minute ceiling, honors `Retry-After`) — because the standard pipeline strangles rate-limited APIs. "One pipeline for all HttpClients" is already disproven in our own code.

And partners lie about status codes. A real, production-observed example: **Nexsure returns `500 Internal Server Error` on a `GET` when the record (a branch) simply isn't there.** The scar-tissue workaround was `if (response.StatusCode == HttpStatusCode.InternalServerError) return default;` — a silent fallback that (a) reads a genuine outage as "not found" with no signal, and (b) would be *retried* three times by a standard resilience pipeline before anyone realized the answer is a business outcome that will never change on retry. "Which status code means what" is not universal; it is a per-partner fact.

This spec makes the **pit of success** (§2.1) the only path: a service author declares one external API, consumes a typed result, and gets resilience, logging, tracing, and parsing **for free by being on the platform** — never by writing or inheriting any of it.

The repetition is real, the seam already half-exists, and Primitives' `Parser`/`Result<T>` foundation gives us the failure vocabulary. This spec unifies all three.

---

## 2. Scope and Non-Goals

### In scope

- A composed, `sealed` typed HTTP client (`IHttpEgress`) for **outbound calls to external/third-party APIs** — the anti-corruption boundary at the system edge.
- A typed result union (`HttpResult<T>`) that makes "not found" and parse failure first-class, compiler-enforced cases.
- A per-client response-classification seam (`EgressClassifier`) so a partner's status-code quirks (e.g. Nexsure's `500`-means-not-found) are declared once and drive both case mapping and retry eligibility.
- A pluggable parser seam accepting four shapes — including a plain `Func<string, T>` delegate that an F# type provider can satisfy idiomatically.
- A named **resilience profile catalog** (Norse Hosting) plus the registration seam to add more.
- A `DelegatingHandler` pipeline that supplies resilience, logging, tracing, auth, and correlation with zero service-author code.

### Non-Goals

- **Cross-service / inter-context HTTP. Forbidden, permanently.** Contexts integrate via published events (RabbitMQ) and, on the client tiers only, gRPC to their *own* service (§3 of CLAUDE.md). The minute cross-service HTTP exists, we have a big ball of mud. This layer is **egress to the outside world only**.
- **gRPC.** Inter-service RPC is protobuf-net.Grpc via `I{Context}Api`, a different transport. Out of scope.
- **The F# schema-drift capture-replay workflow.** "Capture the JSON that broke the map → add to the type-provider sample corpus → let the F# compiler throw at our map → fix → redeploy → reprocess the message off the queue" is its own spec (a poison-message / schema-evolution workflow touching NServiceBus dead-letter and `[MustConsume]`). **This spec only builds the seam** that makes a type-provider-backed parser plug in cleanly and captures the offending payload at the failure boundary.
- **A source-generated Refit-style client surface** (Approach 3, §11). Earmarked, not built.
- **Proactive rate limiting** (sliding-window/token-bucket pacing). Parked in `poc/aichatweb/FINDINGS.md §0` until a partner forces it; adding it is an additive third profile.

---

## 3. Realm Placement and Assembly Layout

| Realm | Assembly | Contents |
|---|---|---|
| **Abstractions** (declared law) | **`Norse.Abstractions.Egress`** (new) | `HttpResult<T>`, `EgressError`, `FailureKind`, `IResponseParser<T>`, `IHttpEgress`, the delegate-registration seam types |
| **Infrastructure** (embodied law) | `Norse.Infrastructure.Egress` | `JsonResponseParser<T>` (System.Text.Json source-gen), `XmlResponseParser<T>`, `sealed HttpEgress` facade, `AddExternalApi(...)` registration extensions |
| **Norse Hosting** (connective tissue) | `Norse.Hosting.ServiceDefaults` | The `DelegatingHandler` pipeline (resilience, logging, tracing, auth, correlation) and the named resilience profile catalog |

`Norse.Abstractions.Egress` is a new peer assembly, **not** folded into `Norse.Abstractions.Infrastructure` — that assembly is the repository/entity-marker/audit contract family, and egress is a distinct concern. Keeping it separate honors §2.5 (clear single purpose per unit).

The §2.4 walls hold without exception: a `.Worker` ingestion handler declares one named external API in its `{Context}WorkerPlugin` (plugins already declare `HttpClient` registrations per CLAUDE.md §4 → Hosting) and consumes `HttpResult<T>`. It never references Polly, never writes an `ILogger` line for the call, never touches `HttpClient` directly.

---

## 4. The Contracts (`Norse.Abstractions.Egress`)

### 4.1 `HttpResult<T>` — the return union

`[MustConsume]` (Primitives attribute) — the compiler forces the caller to handle every case.

| Case | Means | Carries |
|---|---|---|
| `Found(T)` | 2xx and the body parsed | the value |
| `NotFound` | the per-client classifier mapped the response to "not there" (default: 404 / 410) | nothing |
| `Failure(EgressError)` | everything else | the error |

**`NotFound` is its own case, never `null`.** This is the §8 / §2.6 reconciliation of the original "null on 404" wish: the compiler forces the caller to decide what "the partner doesn't have it" means, and "not found" can never be silently confused with "200 + empty body" (which is a `Failure(EmptyBody)` — a happy-path call returning nothing is a contract violation worth failing on, not a null).

**Which status means "not found" is a per-partner decision, not a fixed 404** (§4.4) — Nexsure says it with a 500.

```csharp
[MustConsume]
public readonly struct HttpResult<T>
{
	// non-boxing union access pattern (Primitives §4.3): HasValue + per-case accessors
	public bool IsFound { get; }
	public bool IsNotFound { get; }
	public bool IsFailure { get; }
	// Match(onFound, onNotFound, onFailure) is the consumption surface
}
```

### 4.2 `EgressError` and `FailureKind`

```csharp
public enum FailureKind
{
	Unspecified = 0,	// §5 sentinel — never a real state
	Transport   = 1,	// resilience exhausted: network failure / timeout
	Status      = 2,	// non-success, non-404 HTTP status
	EmptyBody   = 3,	// 2xx but no body to parse
	Parse       = 4,	// body arrived, the parser rejected it
}

public readonly record struct EgressError(
	FailureKind Kind,
	HttpStatusCode? StatusCode,	// null for Transport
	string RawBody);		// see capture rules below
```

**`RawBody` capture rules:**
- `Parse` — holds the **full** offending body. This is the seed for the future F# drift corpus; the whole point of the seam is that the payload that broke the map is preserved verbatim.
- `Status` — holds a **bounded** diagnostic snippet (mirrors `ParseError.MaxInputLength` posture; large error pages are not log fodder).
- `Transport` / `EmptyBody` — empty `RawBody`.

**The logging handler logs only bounded fields and never the full `RawBody`.** The full body is consumed by the message dead-letter path (the future drift spec), not the log. PII posture: a `RawBody` that may contain PII is a deliberate downstream concern of the drift/dead-letter spec, which lands in encrypted storage (`EncryptedString`); this spec captures it in-process only and does not persist it.

### 4.3 The parser seam — four shapes

All shapes normalize internally to `Result<T>` (Primitives). The facade combines the transport outcome with the parse outcome to produce `HttpResult<T>`.

| # | Shape | Use | AOT |
|---|---|---|---|
| 1 | `Func<ReadOnlySpan<byte>, T>` | Zero-alloc C# JSON via `Utf8JsonReader`. **The default.** | Clean |
| 2 | `Func<ReadOnlySpan<char>, T>` | Char-span variant | Clean |
| 3 | `Func<string, T>` | **F# / type-provider / messy-XML escape hatch.** | Blocker (isolated, §8) |
| 4 | `IResponseParser<T>` | Full interface for stateful/configurable parsers (e.g. `XmlResponseParser<T>`) | Per impl |

**Shape 3 is the F# scar-tissue hatch.** A delegate like

```fsharp
[<CompiledName("ParseAttachmentXml")>]
let parseAttachmentXml (xml: string) =
	NexsureEaiProvider.Parse(xml).Attachment |> mapFromAttachment
```

drops straight in. The framework materializes the body to a `string`, calls the delegate inside the **one sanctioned `try/catch`** (the parse boundary — the single place in the platform a catch-all is permitted, because a third party's malformed payload is exactly "something we cannot validate at compile time"), and maps a thrown parse failure → `Result.Failure` carrying the full `RawBody`.

**Throwing vs. `Result`-returning delegates.** Delegates that throw on bad input (the F# type-provider case) are wrapped: the framework's catch converts the throw to `Failure`. Delegates that already return `Result<T>` pass through unwrapped (the disciplined-C# case that wants explicit control and no catch). Both registration overloads exist, so F# scar tissue and span-native C# both have an idiomatic home.

**Why `Func<string, T>` and not span-only:** F# cannot idiomatically author `ReadOnlySpan<char> -> T` lambdas (ref structs cannot be captured in F# closures), and type providers parse from `string`. Forcing a span signature would push F# consumers out of the pit of success (cf. the F# Consumer Support principle: design APIs F# can call idiomatically). The string path trades allocation for reach, and it is the *messy-partner, off-the-queue* path where throughput is not the constraint.

### 4.4 Response classification — the `EgressClassifier` seam

Partners disagree on what HTTP status codes mean. The classifier is the **single per-client seam** that maps a raw response to a disposition *before* the body is parsed, and it is the **one place** that decides both the `HttpResult` case and whether resilience retries — so a partner's status quirk is declared once and both behaviors follow.

```csharp
public delegate ResponseDisposition EgressClassifier(HttpResponseMessage response);

public enum ResponseDisposition
{
	Unspecified = 0,	// §5 sentinel — never returned
	Success     = 1,	// proceed to parse → Found, or EmptyBody if no body
	NotFound    = 2,	// → HttpResult.NotFound (no parse, no retry)
	Transient   = 3,	// retryable; if resilience exhausts retries → Failure(Transport)
	Permanent   = 4,	// terminal → Failure(Status) (no retry)
}
```

**The default classifier** (used when none is registered — the well-behaved-partner path):

| Response | Disposition |
|---|---|
| 2xx | `Success` |
| 404, 410 | `NotFound` |
| 408, 429, 5xx | `Transient` |
| other 4xx | `Permanent` |

**Nexsure's classifier** overrides exactly one thing — `500 → NotFound`:

```csharp
classify: Classify.NotFoundOnStatus(HttpStatusCode.InternalServerError)
```

`Classify.NotFoundOnStatus(...)` returns the default classifier with the named statuses remapped to `NotFound`. The result: a Nexsure 500 maps to `HttpResult.NotFound` **and** is not retried — from one declaration, eyes open to the tradeoff that a genuine Nexsure outage will also read as `NotFound` (the partner overloaded the status; we take them at their word, and the author opts in per-partner). Richer body-signature classification (e.g. 200 with a SOAP fault body that means "not found") is a documented future extension to the classifier delegate — not designed ahead of demand.

**The classifier is shared between the facade and the resilience handler.** The resilience handler's `ShouldHandle` predicate is derived from it (`disposition == Transient`); the facade's case mapping is derived from it (`Success` → parse, `NotFound` → `NotFound`, `Permanent` → `Failure(Status)`, an exhausted `Transient` → `Failure(Transport)`). One classifier, one source of truth, no drift between "do we retry this?" and "what does this status mean?"

### 4.5 `IHttpEgress` — the injection surface

```csharp
public interface IHttpEgress
{
	Task<HttpResult<T>> GetAsync<T>(
		string path, CancellationToken ct = default);

	Task<HttpResult<TResponse>> PostAsync<TResponse>(
		string path, object body, CancellationToken ct = default);

	// per-call parser override for partners whose endpoints disagree with each other
	Task<HttpResult<T>> GetAsync<T>(
		string path, IResponseParser<T> parser, CancellationToken ct = default);
}
```

The parser is resolved **per registration** in the common case (one partner, one shape), with a per-call override for partners whose endpoints are internally inconsistent.

---

## 5. The Implementations (`Norse.Infrastructure.Egress`)

- **`JsonResponseParser<T>`** — System.Text.Json **source-gen** (AOT-clean). The shape-1 default. A registered `JsonSerializerContext` per response type.
- **`XmlResponseParser<T>`** — shape-4 `IResponseParser<T>` for well-structured XML partners that do not need a type provider.
- **`sealed HttpEgress`** — implements `IHttpEgress` over a named `HttpClient` obtained from `IHttpClientFactory`. Owns the transport→parse→`HttpResult` assembly logic that the POC's `VoyageEmbeddingGenerator` hand-rolled, exactly once.
- **`AddExternalApi(...)`** — registration extension (called from a `{Context}WorkerPlugin`):

```csharp
services.AddExternalApi(
	name:     "nexsure",
	profile:  ResilienceProfile.RetryAfterTolerant,	// required — no default (§6)
	baseAddress: cfg.NexsureBaseUrl,
	auth:     EgressAuth.Bearer(cfg.NexsureToken),
	classify: Classify.NotFoundOnStatus(HttpStatusCode.InternalServerError),	// §4.4 — Nexsure says "not found" with a 500
	parser:   ResponseParser.FSharp(NexsureXml.ParseAttachmentXml));	// shape 3
```

---

## 6. Resilience Profiles and the Handler Pipeline (`Norse.Hosting.ServiceDefaults`)

### 6.1 The named profile catalog

"One pipeline for all clients" is disproven (§1), so the spec ships a small named catalog and a seam — not a single default.

| Profile | Shape | For |
|---|---|---|
| `Standard` | ~30s total; 3× exponential + jitter on transient (5xx / 408 / network); per-attempt timeout | Well-behaved partners |
| `RetryAfterTolerant` | 2-min total ceiling; 5× retry; **honors `Retry-After`** (429 / 503); 60s per-attempt | Rate-limited partners (lifted verbatim from the POC's `AddRetryAfterResilience`) |

**The profile sets the retry *shape* (counts, backoff, ceilings); the per-client classifier (§4.4) sets retry *eligibility*.** The resilience handler's `ShouldHandle` is `disposition == Transient`, so a status a partner has overloaded to mean a terminal outcome (Nexsure's `500 → NotFound`) is never retried regardless of profile. The two compose: pick a profile for *how* to retry, declare a classifier for *what* is worth retrying.

### 6.2 Profile selection is required — no silent default

`AddExternalApi`'s `profile` parameter has **no default value**. Omission won't compile. This is the one place "convention over configuration" (§2.5) yields to "fail loud" (§2.6 / §8): choosing a partner's resilience posture blind is exactly the silent coercion the platform outlaws. The author must look the partner in the eye and decide.

Adding a profile is additive: register a named profile in the catalog; existing clients are untouched. The proactive sliding-window limiter (parked in `FINDINGS.md §0`) becomes a third named profile *if and when* a partner forces proactive pacing — corner-case profiles are not designed ahead of demand (dragon-sizing).

### 6.3 The handler pipeline — the "free stuff"

Ordered `DelegatingHandler`s, attached once at registration (never at the call site, so it is structurally impossible for an author to forget any of it):

```
auth-header injection
  → correlation / trace-context propagation
    → logging  (source-gen [LoggerMessage], bounded fields only, never RawBody)
      → resilience  (the named profile)
        → socket
```

OTel `AddHttpClientInstrumentation` (already enabled in `ServiceDefaults`) supplies tracing and metrics with no extra code. Logging uses source-generated `[LoggerMessage]` methods registered in the `LogEvents` registry (per the 2026-06-05 performance-posture spec), co-located per the sanctioned `partial` exception.

---

## 7. What a Service Author Writes — End to End

**C# JSON partner:**
1. A `sealed record` response shape.
2. One `AddExternalApi(..., ResponseParser.Json<TResponse>())` line in the worker plugin.
3. Consume `HttpResult<T>` via `Match`.

**Messy partner (F# type provider):**
1. An F# `string -> T` parse delegate (type provider behind it).
2. One `AddExternalApi(..., ResponseParser.FSharp(delegate))` line.
3. Consume `HttpResult<T>` via `Match`.

In **neither** case does the author write a `try/catch`, an `ILogger` call, a Polly policy, or any `HttpClient` handling. They get all of it by being on the platform.

---

## 8. AOT Posture

- The default JSON path (shapes 1–2) is source-gen and **AOT-clean**.
- The F# type-provider path (shape 3) is a **documented, isolated AOT blocker**, registered in the performance-posture spec's blocker register and confined to the single egress client that opts in. The "no new blockers" rule holds — this is a *known, bounded* opt-in, not creep. The escape hatch earns its non-AOT cost only when a partner's mess justifies it.

---

## 9. Testing

- **Parser contract tests** — round-trip success per parser shape; failure path produces `Result.Failure` with the offending payload captured (the drift-corpus seam).
- **`HttpResult` union** — `[MustConsume]` enforcement; `NotFound` vs. `EmptyBody` distinction; `Match` exhaustiveness.
- **`EgressClassifier`** — default disposition table; `Classify.NotFoundOnStatus` remaps the named status and **suppresses retry** for it (the Nexsure `500 → NotFound` case: assert one 500 yields `NotFound` with zero retry attempts on the stub handler).
- **Resilience profiles** — driven through a stub `HttpMessageHandler` (the sanctioned BCL port — §8 "wrap and mock the wrapper"; reuse the existing `StubHttpHandler` pattern from `Voyage.Extensions.AI.Tests`). Assert retry counts, `Retry-After` honoring, total-ceiling behavior.
- **Live smoke tests** — gated like `VoyageLiveSmokeTests` (skipped without credentials).

Shouldly + NSubstitute (§4 → Testing). Tests reach internals through the standard `InternalsVisibleTo` door (§2.3).

---

## 10. Naming and Glossary

- **`Norse.Abstractions.Egress` / `IHttpEgress`.** "Egress" leaves nothing to inference — a DevOps reader groks it instantly. For the peanut gallery, the README/glossary carries the definition:
  > **Egress** — the platform's outbound anti-corruption boundary to external/third-party APIs. The *only* sanctioned way the platform talks HTTP to the outside world. Cross-service traffic is events + gRPC, never egress.

---

## 11. Future Work / Earmarks

- **Approach 3 — source-generated Refit-style clients.** Declare `interface INexsureApi { [Get("/x")] Task<HttpResult<T>> GetX(); }`; a generator emits the impl on top of this layer. Maximum call-site ergonomics, compile-time, no reflection (fits the source-gen-over-reflection posture and the existing mediator generator). **Deferred** — earned once enough partners justify the generator. Built *on* `IHttpEgress`, not instead of it.
- **F# schema-drift capture-replay workflow.** Its own spec (§2 Non-Goals). This layer's `EgressError.RawBody` (Parse kind) is the hand-off point.
- **Proactive rate limiting.** Third named profile when a partner forces it (`FINDINGS.md §0`).

---

## 12. Rejected Alternatives

- **Approach 2 — abstract `HttpEgressClientBase` consumers inherit.** Closer to the original "inheritance tree" phrasing, but it breaks sealed-by-default (§2.3), the base accretes every concern over time, and it is harder to test and compose. The "free stuff" must arrive by composition (handler pipeline + DI), not by what a class inherits. Rejected.
- **Bare `T?` null-on-404.** The original wish. Rejected: `null` conflates "404 not found" with "200 + empty body," and a bare-null fallback is the §8 / §2.6 silent-coercion smell the platform outlaws. `HttpResult<T>.NotFound` types the absence instead.
- **Folding egress contracts into `Norse.Abstractions.Infrastructure`.** Muddies a single-purpose assembly (§2.5). Rejected in favor of a dedicated `Norse.Abstractions.Egress`.

---

## 13. Decisions Made in This Session

1. Return contract: `HttpResult<T>` union (`Found` / `NotFound` / `Failure`), `[MustConsume]` — not null-on-404.
2. F# drift workflow: seam here (`Func<string, T>` + payload capture), full workflow its own spec.
3. Scope: external egress only. No cross-service HTTP, ever.
4. Core architecture: composed typed facade (Approach 1); Approach 3 earmarked; Approach 2 rejected.
5. Names: `Norse.Abstractions.Egress` / `IHttpEgress`, defined in the README for non-DevOps readers.
6. `EmptyBody` on a happy-path call is a `Failure`, not a null.
7. Resilience profile selection is a required parameter — no silent default.
8. F# type-provider path is a known, isolated, documented AOT blocker.
9. Status-code meaning is per-partner: the `EgressClassifier` seam (§4.4) maps a response to `Success` / `NotFound` / `Transient` / `Permanent`, driving **both** the `HttpResult` case and retry eligibility from one declaration. Default handles well-behaved partners; Nexsure's `500 → NotFound` is the proving case.
