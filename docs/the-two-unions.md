# The Two Unions

*Doctrine. Governs every design touching `Result<T>` (Svartalfheim) or `Outcome<T>` (Asgard).
These types rhyme — both are discriminated unions with success and failure arms — and they are
opposites. Treating them as siblings with a style drift is the root of an entire class of bad
proposal; this page exists so that class of proposal dies in review.*

## The polarity

| | `Result<T>` — Svartalfheim | `Outcome<T>` — Asgard |
|---|---|---|
| **Faces** | The boundary. The untrusted outside world. | The interior. Operations inside Yggdrasil. |
| **Describes** | **Data** — a claim about whether a thing from outside is real. | **An event** — "this operation ran; here is how it went." |
| **Representation** | `readonly record struct` via the `[Union]` provider pattern, hand-forged so parsers running tight loops over 40,000-row files pay zero allocation and zero boxing. The compiler's price for hand-rolling (public untyped `object? Value` window, creation members per CS9385/CS9386) is paid knowingly. | `[Union]` provider-pattern `sealed class`. The C# 15 `union` keyword is unavailable here — it compiles exclusively to an opinionated record struct, which cannot cross gRPC client machinery (`where TResponse : class`) and carries equality semantics wrong for an event. So the interior envelope hand-declares its class provider and pays the same plumbing price as `Result<T>`, in the other direction. Created once per operation adjacent to I/O; one allocation is silence. Never stored, never compared, never `with`-mutated. Pleasing inversion: the language's stock union is struct-shaped — forge-jurisdiction physics — while the Aesir hand-roll. |
| **Serializes?** | **Serializes scalars.** `Result<T>` wraps individual scalar fields (and scalar fusions) on consuming types — `ApiRequest(Result<UsState> State, Result<ZipCode> ZipCode, Result<Latitude> Latitude, Result<Longitude> Longitude)` — while the consuming record itself, and any child objects, remain pure C# records with no union wrapper. The union's serialized form is the scalar's own representation; nullability on the field carries cardinality into OpenAPI. It is never the envelope of a whole object — it is the customs stamp on each scalar crossing the border. | **Never.** Not to a wire, not to a database, not to a log line. The gRPC surrogate does not serialize it — it ERASES it (payload bytes only). The JSON converter is a tripwire that throws. There is no EF mapping and never will be. |
| **Nullability means** | Cardinality. `Result<Ssn>` is a required field; `Result<Ssn>?` is optional. The C# nullable system does required/not-required enforcement with zero effort from the object author, and OpenAPI reads the same annotations. | Nothing — an `Outcome` reference must never be null. Null is not a fourth state; it is a bug that nullable annotations make loud. |
| **Storage** | Narrow, sanctioned: an EF converter may unwrap the success value for insert-only/logging scenarios. Putting it on a live entity is not recommended. | Prohibited absolutely. Storing an event as data is a category error. |
| **Consumption** | Compose, project, convert — it is a value and behaves like one. | `Match` (and the `[MustConsume]`-blessed escapes) only. The API surface is deliberately starved: `Ok`, `Err`, `Match`, nothing else. |
| **Purpose** | Parse, don't validate: turn the world's disjointed crap into proper scalars, and report violations as data instead of dying on the first bad cell. | Force the caller to look into the void and handle the unhappy path without papering over it. |

## The three enforcements are one law

`Outcome<T>` has no representation outside the process that experienced it. Three mechanisms
police this from different angles, and they are the same law enforced thrice:

1. **The protobuf surrogate** serializes the payload's own contract, byte-identical to the bare
   payload — the union never exists on the wire. A `Failed` arm reaching any marshaller throws:
   it means the transport's translation point (`OutcomeServerInterceptor` for gRPC, the result
   filter for REST) was not registered.
2. **The JSON tripwire** (`JsonConverter<Outcome<T>>`) throws on Write AND Read. The REST filter
   unwraps the union before serialization ever sees it; no Norse client speaks JSON, so
   deserializing a DU from JSON is illegal by definition.
3. **The absence of storage support** is not an omission awaiting a contributor — it is the
   design. No EF value converter, no document mapping, no message-body serialization of the
   union itself.

## The starved API is the enforcement mechanism

Immutable once constructed: you have the thing, and that's it. The ban targets TYPED happy-path
accessors — no `.Value` typed as `T`, no `.Problem`, no `.Succeeded` boolean — because the
moment one exists, "forcing you to cope" degrades into "politely suggesting you glance."
Compiler-mandated union plumbing is exempt and is not a violation: the provider pattern's
untyped `object? Value` window (required by CS9386, present on `Result<T>` by precedent and on
`Outcome<T>` by necessity) is language machinery — it boxes, it is
undiscoverable by intent, and nobody papers over the void through an `object?` cast without it
reading as violence. Construction is `Ok`/`Err`; consumption is `Match` (plus the
`[MustConsume]`-blessed escapes). The one sanctioned violence in application reach is the
explicit unwrap operator (`(T)outcome`), which throws on `Failed` and is explicit-cast-shaped
precisely so it reads as violence at the call site. Its legitimate callers are machinery (the
serialization surrogate), not application code.

## The shared design move

Both unions exploit machinery the ecosystem already understands instead of inventing parallel
channels. `Result<T>` rides the C# nullable reference system: one language feature yields
required-field enforcement, OpenAPI cardinality, and analyzer coverage with zero author effort.
`Outcome<T>` rides transport idioms: the DU exists only in C#, and each edge translates it into
the transport's native tongue (gRPC status + ErrorInfo trailers, REST ProblemDetails, the
circuit's identity function — the DU *is* the circuit's idiom). Never invent a channel when an
existing, universally-understood one can carry the law.

## Consequences for designers

- A proposal to serialize, persist, cache, or compare `Outcome<T>` is wrong by doctrine, not by
  taste. The correct move is always to translate at the edge or carry the payload.
- A proposal to make `Result<T>` ephemeral or strip its serialization is equally wrong in the
  other direction — its whole value is that it composes into consuming types and carries its
  cardinality in its nullability.
- When a new transport or storage concern meets `Outcome<T>`, the question is never "how do we
  represent the union here" — it is "where does the translation point live, and what throws if
  the union leaks past it."
- Jurisdictional performance physics: representation choices (struct vs class) follow the
  realm's workload, not consistency between the unions. The forge optimizes for tight loops;
  the boundary envelope optimizes for honesty.
