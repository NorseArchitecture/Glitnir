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

## The inverse law on `Result<T>`

The forge's union has its own edge enforcement, mirror-imaged. Going out (client → wire), **only
proven scalars cross**: a success serializes as the scalar's own native wire form — the union never
rides the wire — and a failed or default `Result<T>` is illegal to write (one shared literal across
the gRPC, JSON, and XML legs: *"a failed or default Result<T> is illegal to write"*). The failure
arm has no wire representation, and that absence is the design: the validator gate — the same
FluentValidation class Blazilla runs before submit — makes a failure at the marshaller unreachable
on the sanctioned path, and the serializer's throw is the tripwire for everything else. Coming in,
deserialization is the parse event: the receiving side re-stamps every scalar through the platform's
one parsing door, so a hostile client that skips the gate buys nothing — the server-materialized
failure is converted to `Failed(Problem)` by the server-side run of the identical validator before
any handler executes. A handler that is executing holds only proven values, by construction.

The corollary completes the polarity: **`Problem` never flows client → server.** It is exclusively
the server's outbound vocabulary, riding `Outcome`'s translated edges. Requests carry data wearing
customs stamps; responses carry events translated at the border; neither union's failure state can
cross in the wrong direction.

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

## Receipts: the WASM login incident (2026-08-09)

*Why the inverse law on `Result<T>` (above) is not a hypothetical.*

`IAuthenticationService.Login` crashed in the WASM host with `System.NotSupportedException:
Arg_NotSupportedException`, thrown from inside the generated protobuf-net.Grpc client proxy
itself (`ProtoBuf.Grpc.Internal.Proxies.ClientBase.IAuthenticationService_Proxy_0.
IAuthenticationService.Login`) — before the request ever reached the wire, let alone the server.

**Root cause.** `LoginRequest.Email` (Heimdall, `AuthN.Services/LoginRequest.cs`) is
`[DataMember] Result<EmailAddress>` — exactly the wire-stamped-scalar shape the inverse law
above describes: a success unwraps to the scalar's own wire form, the union never rides the
wire. Making that true requires a protobuf-net surrogate per scalar type, registered once
against `RuntimeTypeModel.Default` — `Norse.Infrastructure.Web.Grpc.ResultSerializers.Register`
(Midgard). Both of Midgard's generated wiring emitters —
`Infrastructure.Web.Client.Generator/ClientRegistrationEmitter.cs` and
`Infrastructure.Web.Server.Generator/ServerRegistrationEmitter.cs` — called
`IdentifierSerializers.Register(model)` inside `RegisterNorseOutcomeSurrogates()` but never
called `ResultSerializers.Register(model)`. The only place that second call was ever made was a
test fixture (`Yggdrasil/tests/Hosting.Web.Server.Tests/WireModelFixture.cs`), manually working
around the exact hole it should have caught.

**Why WASM and not Blazor Server.** Blazor Server never triggers the failure because it never
serializes this call at all — Himinbjörg's real `AuthenticationService` is injected in-process
(Heimdall's per-host DI substitution: components inject `I{Contract}Service` directly). The
server host's "success" was never evidence the wire format was sound. WASM is the one host that
actually gRPC-marshals `LoginRequest`, so it's the one host where an unregistered
`Result<EmailAddress>` surrogate is reachable at all — and protobuf-net.Grpc doesn't fail loudly
there either: `ProxyEmitter` catches the `NotSupportedException` `RuntimeTypeModel.CanSerialize`
throws, downgrades the method to `TypeCategory.Invalid`, and bakes a hardcoded
`throw new NotSupportedException()` into that one proxy method instead of refusing to build the
proxy at all. A silent-fallback failure mode, inside a third-party library, running directly
counter to this platform's own §2.7 (fail fast, fail loud, fail hard).

**The fix.** One line, added symmetrically to both emitters:
`global::Norse.Infrastructure.Web.Grpc.ResultSerializers.Register(model);`, immediately after
`IdentifierSerializers.Register(model)`, before any `Outcome<T>` surrogate guard runs. TDD:
`Registers_the_result_serializers_alongside_the_identifier_serializers` added to both
`GrpcClientRegistrationGeneratorTests.cs` and `GrpcServerRegistrationGeneratorTests.cs`,
confirmed red against the un-patched emitters, green after. Full `Midgard.slnx` run: 624 tests,
0 failed, 4 pre-existing skips.

**Why this is the shape worth paying for.** The mainstream alternative — REST/JSON, a JS/TS
client, validation logic hand-written twice and kept in sync by discipline — doesn't hit this
failure mode. It hits a slower, quieter one instead: client and server validation drifting
apart over months, caught by neither the compiler nor CI. What this incident cost was one
afternoon and a two-line fix.

**Which part is actually the flex.** Two different things are stacked in "the same code runs
on Windows, macOS, Android, iOS, WASM, and the server," and only one of them is rare. "Same UI
code on five platforms" is table stakes — MAUI Blazor Hybrid is a first-party, documented
Microsoft pattern; Flutter, React Native, and Kotlin Multiplatform all sell the same pitch.
Nobody is impressed by "we wrote the buttons once" anymore. What's actually rare is that the
*domain layer* travels with the same fidelity as the UI. `EmailAddress.Parse`,
`Result<EmailAddress>`, `LoginRequest`, the FluentValidation rules, `Outcome<T>`'s failure
semantics — none of that is UI. That's business logic and wire contract, and it is identical
bytecode running in a Windows desktop shell, a macOS shell, an Android APK, an iOS binary, a
server process, and a browser tab. Most cross-platform frameworks solve "write the screen
once." This platform solves "write the rules once" — parsing, validation, and the algebra of
success/failure — and makes the type system enforce that every one of those hosts is
transporting the exact same shape, not five independently-maintained approximations of it.

Read that way, this incident is the argument *for* the design, not against it: the defect was
one missing line, in one place, in Midgard's generator. It did not require five separate fixes
for five separate platforms with five separate client SDKs each drifting slightly from the
others, because there aren't five implementations — there's one, radiating out. Compare that to
the counterfactual: a Windows WPF client, a native Android app, a native iOS app, and a web
frontend, each with its own hand-rolled email validator, silently disagreeing at the edges.
That's not hypothetical — it's the normal state of affairs at most companies with a
multi-platform footprint.

So the bomb isn't "runs everywhere." The bomb is: the same forge runs everywhere, and when the
forge breaks, it breaks in exactly one place.

## The payoff, fully materialized: one algebra, three wire formats

The WASM incident above proves the law holds under gRPC. It was never only a gRPC claim. The
same `Result<T>`/`Outcome<T>` algebra — one parse funnel, one failure semantics, one shared
"illegal to write" tripwire — now demonstrably drives REST content negotiation too: JSON *and*
XML, generated, not hand-authored, and proven byte-identical against the gRPC leg by an actual
running test host, not by argument.

**Futhark.** That's the doctrine name for the effort — narrative only, by the platform's own
naming law (§6.2, Glitnir CLAUDE.md: bounded contexts and cross-cutting mechanisms don't get
codenames bound to code). No repo, namespace, package, or type anywhere carries the word; the
actual home is Midgard's `Infrastructure.Web.Server.Xml` plus its `gen/
Infrastructure.Web.Server.Xml.Generator`. The name exists for the same reason this section
exists: to let the record talk about "the XML effort" as one thing without it leaking into a
single line of source. The spec's own ethos statement (`Glitnir/docs/Platform/specs/
2026-08-01-opinionated-xml-serialization-design.md`, §1.2) is worth quoting whole, because it's
the same starved-API instinct as `Outcome<T>`'s `Match`-only surface, aimed at a wire format
instead of a type:

> One way to write XML. Every scalar is an attribute. No exceptions. Every collection is N
> child elements. No wrapper elements, ever... The ethos is the acceptance test for every future
> feature request: if a proposal introduces a second way to write the same data, it is rejected
> before it is evaluated.

No `xsi:nil`, no namespaces, no vendor media types, no per-endpoint negotiated casing, no
`[EnumMember]`-style overrides, no polymorphism. Every axis a general-purpose XML serializer
would let a caller negotiate is fixed once, platform-wide — "take-it-or-leave-it is an API
posture *and* an implementation subsidy."

**The shared literal now has three confirmed authors, not two.** The inverse law's tripwire —
*"a failed or default Result\<T\> is illegal to write"* — was already the gRPC (`ResultSerializer<T>`)
and JSON (`ResultJsonConverter`) message. It is now, verified against the shipped generator
source, the exact string the generated XML writer throws too: `WriterEmitter.WriteAttribute`
recurses into every `Result<T>` member, unwraps a `Success<T>` cleanly, and throws that literal,
character-for-character, on `Failure` or `default`. One message source, three independently
negotiated wire formats, zero drift — because there is exactly one place the string is spelled.

**Zero hand-authored XML per contract type.** The split is the same shape as the gRPC story:
a small, hand-written, type-agnostic engine, and a source generator that does everything that
would otherwise be per-type boilerplate.

- Hand-written once: `XmlLexical` (the canonical scalar-formatting engine — bool, decimal,
  Guid, the date/time family via `XmlConvert`, non-finite float/double rejection, XML-legal
  char guards), and the type-agnostic MVC formatter pair
  (`XmlContractInputFormatter`/`XmlContractOutputFormatter`) that looks a runtime `Type` up in a
  registry and throws if it isn't there — no per-contract branch anywhere in either formatter.
  `ProblemXmlWriter` (RFC 9457 `problem+xml`) is the one deliberate hand-written exception,
  because `ProblemDetails.Extensions` is a bare `IDictionary<string, object?>` with no shape a
  generator could derive.
- Generated at host-build time, once per distinct `[DataContract]` type reachable from a facade
  controller: `XmlShapeGenerator` walks the closure (`ClosureWalker`), classifies every member,
  and emits one `{Contract}XmlShape : IXmlShape<{Contract}>` per type via `WriterEmitter`/
  `ReaderEmitter` — declaration-order attributes, then children, `Result<T>` unwrap-or-throw
  inline, enum dispatch through a shared generated name table. The shape law is enforced as
  build **errors** (`NORSE022`–`NORSE029`), not review comments: "you cannot compile an
  exposure Futhark cannot round-trip."

**The enum wire law rides the same generated table on both text channels.** Non-flags enums
serialize as their case-styled member name — never a numeral — on JSON and XML alike, off one
generator-emitted name table (`RegistrationEmitter.EmitEnumRegistration`), never `Enum.Parse`,
never reflection at runtime. `[Flags]` enums are refused in the facade closure outright
(`NORSE029`) — "flags don't translate to strangers." A multi-select becomes the platform's one
role-named-record collection shape on every channel, gRPC included: a smaller, more consistent
surface, paid for knowingly, exactly like `Result<T>`'s CS9385/CS9386 plumbing tax above.

**The proof isn't argued, it's tested — the tri-protocol swoop.** `Yggdrasil/tests/
Hosting.Web.Server.Tests/Swoop/TriProtocolSwoopTests.cs` wires one live host — gRPC, REST-JSON,
REST-XML, and OpenAPI, all four simultaneously — against one real `ParityService` behind the
real mediator pipeline, and drives the *same logical request* through all three wire protocols
against the live host. The test names are the receipt:

- `Success_parity_the_same_request_renders_a_structurally_equal_report_on_all_three_channels` —
  a real typed gRPC client call, a REST-JSON POST, and a REST-XML POST against the same request,
  asserted structurally equal.
- `Failure_parity_three_malformed_scalars_render_identical_errors_arrays_on_json_and_xml` — the
  same malformed input, the same `errors` array, on both text channels.
- `Required_absent_detail_wording_is_literally_equal_across_all_three_channels` — gRPC, XML, and
  JSON all render the exact string `"required value missing"` for the same absent required
  `Result<T>` member. One `FailureDetail.Render` message source, proven identical across three
  unrelated wire protocols by a live assertion, not a shared-constant argument.
- `Opt_in_law_the_undecorated_binding_shadow_is_invisible_on_every_channel` — the `[DataMember]`
  opt-in law, proven on both text channels through one live host.

The fixture's own history is part of the receipt: an earlier revision used a hand-written
stand-in for the generated shapes after a suspected generator-starvation bug; the real root
cause (a Midgard package-bundling gap) was found and fixed, and the fixture now exercises the
actual generated `NorseXmlShapeRegistration.Build()` output — the swoop tests prove the real
generator, not a double standing in for it.

**OpenAPI cardinality, for free, on the third format too.** `ResultSchemaTransformer` replaces
a `Result<T>` member's reflected union shape with the underlying scalar's schema and derives
`required`/`writeOnly` straight from the same nullability the C# compiler already enforces —
verified against the real generated OpenAPI document, not assumed. `XmlMetadataTransformer`
stamps every member with `NodeType = Attribute` and the identical case-styled name the
generated shapes actually emit on the wire — "the same casing the generated shapes actually
emit, never a second independently-drifting rule." A `Result<DateOnly>` request member shows up
in the OpenAPI document as a required-or-optional `date` scalar, `writeOnly`, XML-attribute-
stamped, with zero hand-authored schema code, off the same registries the formatters already
populate.

**The point was never XML.** Futhark's own ethos statement says as much: "text channels are for
strangers" — the internal platform stays gRPC end-to-end; XML content negotiation exists for
third-party integrators who ask for it, and vanishingly few of them do in 2026. Building an
opinionated, fully generated, zero-hand-authored XML wire format nobody strictly needs was never
about XML. It was proof by construction that the algebra travels intact regardless of the
representation asked of it — and that when a client asks for the one format practically nobody
wants anymore, the platform still doesn't hand-write a byte of it to answer.

## The parse gate is also a free traffic filter

`Result<T>` living client-side (the same dwarves riding the browser and MAUI shells above) pays
rent in one more place nobody asked it to cover: no expensive, database-hitting async validation
call is ever attempted while a field's `Result<T>` is still `Failure`. Nobody wrote a debounce
timer for this. It falls out of ordering two things the platform already had.

**The mechanism is `Cascade(CascadeMode.Stop)`, not a rule-set.** `RegisterRequestValidator`
(Heimdall, `AuthN.Components/RegisterRequestValidator.cs:42–68`) — the single declaration
Blazilla runs client-side and Himinbjörg's generated `CommandRequestValidator<RegisterCommand,
RegisterRequest, RegisterResult>` re-runs unmodified server-side — puts the whole `Email` rule
in one cascade:

```csharp
RuleFor(x => x.Email)
    .Cascade(CascadeMode.Stop)
    .Must(email => !(email.TryGetValue(out Failure failure) && failure.Reason == ParseFailure.Empty))
    .WithMessage("Enter your email address.")
    .Must(email => email.TryGetValue(out Success<EmailAddress> _))
    .WithMessage("Enter a valid email address (local@domain.tld).")
    .CustomAsync(async (email, context, cancellationToken) =>
    {
        // unproven input never buys this round trip or its database query.
        var outcome = await authenticationService.EmailExists(new() { Email = email }, cancellationToken)...
    });
```

Both `Must` checks read nothing but the `Result<EmailAddress>` discriminant — no regex, no
second format authority (the class's own doc-comment records that the former `EmailAddress()`
regex was deleted as exactly that). `CustomAsync` — the gRPC call plus the server's uniqueness
query — sits last in the same cascade, so it is structurally unreachable until the union is
already `Success<EmailAddress>`. The doc-comment names the road not taken, deliberately:
rule-set gating to a submit-only pass was tried and rejected (ruled 2026-08-06) as unbuildable
against Blazilla's actual `MemberNameValidatorSelector`, which carries no rule-set guard on a
field-change pass — `Cascade(Stop)` ordering is the mechanism that actually works, not the
first one reached for.

**It protects the server as much as the client.** Because the identical validator class runs
again inside Himinbjörg's `CommandRequestValidator` adapter, a request that skips client
validation entirely (a hostile caller going straight at the gRPC surface) hits the exact same
cascade order server-side: the sync shape check runs before the database query gets anywhere
near it. The traffic filter isn't a UX nicety layered on top of the real validation — it *is*
the real validation, run twice, and the ordering law travels with it both times.

**What it is not: `Result<T>` filtering keystrokes.** The codebase is precise about this and
worth being equally precise about here. `FluentTextInput` (`Login.razor`, `Register.razor`)
never sets `Immediate="true"`, so FluentUI's own per-keystroke handler is a no-op regardless of
validation wiring — the field commits, and therefore re-parses, on blur, not on every
keystroke. That's a separate mechanism (default input binding), not the union. An earlier
revision of `RegisterTests.cs` asserted "no call on keystroke" directly and was removed once the
team noticed the assertion could never fail either way — it was proving the binding, not the
gate. The two effects stack: blur-commit binding limits how often the field even re-parses;
`Cascade(Stop)` then decides whether that blur is allowed to spend a network call and a database
query. Two tests carry the actual, narrower claim as a live assertion, not an argument:
`Heimdall/tests/AuthN.Components.FluentUI.Tests/RegisterTests.cs` —
`The_email_exists_check_fires_on_blur_and_submit_and_stops_before_the_service_on_malformed_input`
— and `Heimdall/tests/AuthN.Components.Tests/RegisterRequestValidatorAsyncTests.cs` —
`A_malformed_email_short_circuits_before_the_service_is_called`, both asserting
`DidNotReceiveWithAnyArgs()` on `EmailExists` for malformed input.

**No hand-rolled debounce exists anywhere in Heimdall, and none was needed.** A grep for
`Debounce`/`Task.Delay`/`Timer` across the realm turns up nothing but an unrelated test fixture.
FluentUI ships its own timer-based debounce (`FluentInputImmediateBase.Immediate`/
`ImmediateDelay`, 200ms default) for exactly the per-keystroke case these pages don't opt into —
and don't need to, because the union's own discriminant plus one cascade already does the job.
Same throughline as "The shared design move" above: never invent a channel — or in this case, a
timer — when machinery the ecosystem already ships (FluentValidation's cascade, the input
binding's own default) can carry the law for free.
