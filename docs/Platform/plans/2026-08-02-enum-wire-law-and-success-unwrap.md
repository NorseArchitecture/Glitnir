# Enum Wire Law + Success-Unwrap on Serialize — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship both ratified 2026-08-02 specs in one pass: `../specs/2026-08-02-futhark-enum-wire-law-design.md` (generated enum name tables + one `EnumLexical` mechanism + flags banned from the facade closure + governed OpenAPI lists) and `../specs/2026-08-02-result-success-unwrap-on-serialize-design.md` (success unwraps on serialize on all three channels, failure/default still throw; implicit `T → Result<T>`; the `[DataContract]` opt-in law on every channel).

**Architecture:** Svartálfheim gains the implicit conversion. Midgard owns everything else mechanical: the gRPC serializers gain success-write branches; the JSON leg gains the opt-in `TypeInfoResolver` modifier, enum converters over a generated `EnumNameRegistry`, and success-write; the XML generator gains the `[DataMember]` filter, the NORSE029 flags ban, table+registration emission, and folds its per-enum parse/write emission onto Midgard's new runtime `EnumLexical`; the OpenAPI transformers stamp governed `enum:` lists. Yggdrasil proves it end-to-end: `ParityRequest` gains the §7 enum row (corpus 13 of 13), an undecorated binding-shadow property proves the opt-in law on all three channels, and the swoop's mirror-contract machinery is replaced by the real typed client proxy the success-unwrap ruling finally makes legal.

**Tech Stack:** .NET 11 preview / C# 15, Roslyn incremental generators + `Norse.Abstractions.Emit` (`AppendCSharp`), System.Text.Json (contract customization via `DefaultJsonTypeInfoResolver` modifiers), protobuf-net 3.x custom serializers, `Microsoft.AspNetCore.OpenApi` transformers, xUnit v3 + Shouldly on Microsoft.Testing.Platform.

## Global Constraints

- Both specs are law. Where this plan and a spec disagree, the spec wins — halt and flag, don't improvise.
- Tabs. `var` for return assignments; target-typed `new()` for construction; collection expressions; expression-bodied members with arrow-on-declaration-line; `internal sealed` default (omit default accessibility); US English. Full law: `../../house-rules.md` — read it before any task if dispatched cold.
- Warnings ratcheted to errors. IDE0005: delete, never suppress. String concatenation banned (interpolation / `StringBuilder`).
- Generator emitters call `sb.AppendCSharp(...)` with raw string literals; emitted code fully-qualifies (`global::`); generator output is BOM-free UTF-8 LF.
- `ConfigureAwait(false)` in all `src/` async code; never in tests.
- Tests: xUnit v3 + Shouldly, no accessibility modifiers on test methods, `RandomNumberGenerator` never `System.Random`, one test project per package.
- **`src/Directory.Build.props`, `tests/Directory.Build.props`, and `.editorconfig` in every realm are scatter-managed and immutable. Editing any of them is halt-and-ask.** Restate this in every subagent dispatch.
- Realms branch `feature/enum-wire-and-success-unwrap`; subagents may commit on the local unpushed feature branch, never master, never push. **Bifröst itself is never branched and never touched.**
- Local dev runs `UseProjectReferences=true`; cross-realm ship gates (PR → CI → tag → publish, Buvy-run) come at the end in dependency order: Svartálfheim → Midgard → Yggdrasil.
- **The illegal-write message, pinned:** `"a failed or default Result<T> is illegal to write"` — byte-identical in `Infrastructure.Web.Grpc` (const `ResultSerializers.IllegalWriteMessage`), the JSON converters' literals, and the XML generator's emitted throw. The undefined-enum-write message stays the emitter's existing form: `'{value}' is an undefined value of '{enumType}' and is illegal to write.`
- **The union never rides the wire.** Success-unwrap emits the naked `T` in each channel's established wire form. Failure and default throw, every channel. Read paths are untouched everywhere except where enum parsing folds onto `EnumLexical`.
- Diagnostic ID for the flags ban: **NORSE029** — verified free (NORSE022–028 live in this generator; Urðarbrunnr owns NORSE030–034).

## File Structure (locked decisions)

```
Svartalfheim/src/Primitives/Result{T}.cs                 (modify — implicit operator)
Midgard/src/Infrastructure.Web.Grpc/ResultSerializer.cs   (modify — success write dispatch)
Midgard/src/Infrastructure.Web.Grpc/ResultEnumSerializer.cs (modify — success write, undefined-write throw)
Midgard/src/Infrastructure.Web.Grpc/ResultSerializers.cs  (modify — const rename/reword)
Midgard/src/Infrastructure.Web.Server/Json/ResultJsonConverter.cs        (modify — success write)
Midgard/src/Infrastructure.Web.Server/Json/ResultJsonConverterFactory.cs (modify — enum routing replaces ThrowIfEnum)
Midgard/src/Infrastructure.Web.Server/Json/OptInContractModifier.cs      (create — §4b STJ enforcement)
Midgard/src/Infrastructure.Web.Server/Json/EnumLexicalJsonConverters.cs  (create — plain + Result enum converters)
Midgard/src/Infrastructure.Web.Server/Json/MvcBuilderExtensions.cs       (modify — AddNorseJson(EnumNameRegistry))
Midgard/src/Infrastructure.Web.Server/Xml/EnumNameTable.cs               (create — the generated-data shape)
Midgard/src/Infrastructure.Web.Server/Xml/EnumNameRegistry.cs            (create)
Midgard/src/Infrastructure.Web.Server/Xml/EnumLexical.cs                 (create — the one mechanism)
Midgard/src/Infrastructure.Web.Server/OpenApi/EnumSchemaTransformer.cs   (create — governed enum: lists)
Midgard/gen/Infrastructure.Web.Server.Xml.Generator/ClosureWalker.cs     (modify — [DataMember] filter, NORSE029)
Midgard/gen/Infrastructure.Web.Server.Xml.Generator/Diagnostics.cs       (modify — NORSE029)
Midgard/gen/Infrastructure.Web.Server.Xml.Generator/{XmlShapeGenerator,ReaderEmitter,WriterEmitter,RegistrationEmitter}.cs
                                                          (modify — table emission, EnumLexical fold, Result unwrap write)
Yggdrasil/tests/Hosting.Web.Server.Tests/Parity/*.cs      (modify — enum row, shadow property, validator, handler)
Yggdrasil/tests/Hosting.Web.Server.Tests/Swoop/*.cs       (modify — typed proxy, corpus row 13, OpenAPI enum test)
```

Residence note (spec plan-detail 2 resolved): `EnumNameTable`/`EnumNameRegistry`/`EnumLexical` live in `Xml/` beside `XmlLexical` and `FailureDetail` — the established home for channel-shared lexical machinery that `Json/` already reaches into (`LexicalJsonConverters` → `XmlLexical` precedent). All three are `public` (generated host code and DI consume them — the same deliberate exception the registry/options types already carry).

Execution order: Task 0 → Tasks 1–4 (independent) → Task 5 → Tasks 6–7 (independent) → Task 8 → Task 9 → Task 10 → Task 11. Commits per task on each realm's `feature/enum-wire-and-success-unwrap`.

---

### Task 0: Svartálfheim — implicit `T → Result<T>`

**Files:**
- Modify: `Svartalfheim/src/Primitives/Result{T}.cs` (add one operator beside the two union-case constructors)
- Test: `Svartalfheim/tests/Primitives.Tests/` (extend the existing `Result` test file — read it first and match its naming/idiom)

**Interfaces:**
- Produces: `public static implicit operator Result<T>(T value)` — wraps in `Success<T>`. Every later task's client-authoring code (`Amount = 1234.56m`) compiles against this.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
void Implicit_conversion_from_value_is_the_success_case()
{
	Result<int> result = 42;

	result.TryGetValue(out Success<int> success).ShouldBeTrue();
	success.Value.ShouldBe(42);
}

[Fact]
void Implicit_conversion_lifts_to_nullable_result()
{
	Result<decimal>? result = 1234.56m;

	result.HasValue.ShouldBeTrue();
	result.Value.TryGetValue(out Success<decimal> success).ShouldBeTrue();
	success.Value.ShouldBe(1234.56m);
}

[Fact]
void Implicit_conversion_from_string_is_the_success_case()
{
	Result<string> result = "Bifrost";

	result.TryGetValue(out Success<string> success).ShouldBeTrue();
	success.Value.ShouldBe("Bifrost");
}
```

- [ ] **Step 2: Run to verify fail** — `dotnet test Svartalfheim/tests/Primitives.Tests`. Expected: compile failure (no conversion exists).
- [ ] **Step 3: Implement** — beside the `Result(Success<T>)` constructor:

```csharp
/// <summary>
/// Wraps a validated value as the success case. The second legitimate author of the union
/// (spec 2026-08-02-result-success-unwrap-on-serialize §2): a first-party client holding a
/// compile-time-typed value states it as plain assignment.
/// </summary>
/// <param name="value">The validated value.</param>
public static implicit operator Result<T>(T value) =>
	new(new Success<T>(value));
```

- [ ] **Step 4: Run the full Svartálfheim suite** — `dotnet test Svartalfheim`. Expected: all green. This run **is** the spec's flagged overload-interference verification: any existing call site where the new conversion creates ambiguity fails compilation here. If ambiguity appears, halt and flag per spec §4 (fallback is `Success<T>` construction) — do not resolve it unilaterally.
- [ ] **Step 5: Commit**: `feat: implicit T -> Result<T> — the client-author conversion`

---

### Task 1: Midgard gRPC — success-unwrap in `ResultSerializer<T>` + message rename

**Files:**
- Modify: `Midgard/src/Infrastructure.Web.Grpc/ResultSerializers.cs` (const rename + reword), `ResultSerializer.cs` (Write branch + `WriteScalar`)
- Test: `Midgard/tests/Infrastructure.Web.Grpc.Tests/ResultSerializerTests.cs`

**Interfaces:**
- Consumes: `GuidWire.Read`/`GuidWire.Write` (existing), `BclHelpers` Level300 read/write pairs, the existing `Envelope<T>`/`PlainEnvelope<T>` fixtures.
- Produces: `internal const string ResultSerializers.IllegalWriteMessage = "a failed or default Result<T> is illegal to write";` (renamed from `DeserializationOnlyMessage`, reworded — update the two existing references in `ResultSerializer.cs`/`ResultEnumSerializer.cs` and the test-local const). `ResultSerializer<T>.Write`: success → the scalar's own wire form; failure/default → throw.

- [ ] **Step 1: Write the failing tests** — replace `Writing_any_state_of_a_required_Result_throws` (success no longer throws) and add the byte-oracle:

```csharp
[Theory]
[MemberData(nameof(IllegalWriteStates))]
void Writing_a_failed_or_default_Result_throws(string label, Result<int> value)
{
	var exception = Should.Throw<InvalidOperationException>(() =>
		TestModel.Serialize(TestModel.Create(), new IntEnvelope { Value = value }));
	exception.Message.ShouldBe(IllegalWriteMessage, label);
}

public static TheoryData<string, Result<int>> IllegalWriteStates() => new()
{
	{ "failure", new Failure(ParseFailure.Malformed, "x", "Int32") },
	{ "default", default },
};

[Fact]
void Writing_a_success_emits_the_plain_fields_exact_wire_bytes()
{
	// The success-unwrap law's oracle: Envelope<T>{Success(v)} and PlainEnvelope<T>{v} must be
	// byte-identical — the union never rides the wire (spec §2). One assertion per taxonomy row.
	AssertSuccessWriteMatchesPlain(true);
	AssertSuccessWriteMatchesPlain((byte)200);
	AssertSuccessWriteMatchesPlain((sbyte)-100);
	AssertSuccessWriteMatchesPlain((short)-12345);
	AssertSuccessWriteMatchesPlain((ushort)54321);
	AssertSuccessWriteMatchesPlain(-123456);
	AssertSuccessWriteMatchesPlain(3000000000U);
	AssertSuccessWriteMatchesPlain(-123456789012345L);
	AssertSuccessWriteMatchesPlain(18000000000000000000UL);
	AssertSuccessWriteMatchesPlain(3.14f);
	AssertSuccessWriteMatchesPlain(2.71828182845);
	AssertSuccessWriteMatchesPlain(1234.56m);
	AssertSuccessWriteMatchesPlain('Z');
	AssertSuccessWriteMatchesPlain("hello, Norse!");
	AssertSuccessWriteMatchesPlain(Guid.NewGuid());
	AssertSuccessWriteMatchesPlain(new DateOnly(2026, 8, 2));
	AssertSuccessWriteMatchesPlain(new TimeOnly(23, 59, 59, 999));
	AssertSuccessWriteMatchesPlain(new DateTime(2026, 8, 2, 1, 2, 3, DateTimeKind.Utc));
	AssertSuccessWriteMatchesPlain(new DateTimeOffset(2026, 8, 2, 1, 2, 3, TimeSpan.FromHours(-5)));
	AssertSuccessWriteMatchesPlain(new TimeSpan(3, 4, 5, 6));
}

static void AssertSuccessWriteMatchesPlain<T>(T value) where T : notnull
{
	var model = TestModel.Create();
	var wrapped = TestModel.Serialize(model, new Envelope<T> { Value = new Success<T>(value) });
	var plain = TestModel.Serialize(model, new PlainEnvelope<T> { Value = value });
	wrapped.ShouldBe(plain, $"Result<{typeof(T).Name}> success wire bytes");
}

[Fact]
void A_success_written_by_the_wrapped_type_round_trips_through_the_wrapped_type()
{
	var model = TestModel.Create();
	var payload = TestModel.Serialize(model, new Envelope<int> { Value = new Success<int>(42) });
	var back = TestModel.Deserialize<Envelope<int>>(model, payload);
	back.Value.TryGetValue(out Success<int> success).ShouldBeTrue();
	success.Value.ShouldBe(42);
}
```

Update the test-local const to `IllegalWriteMessage = "a failed or default Result<T> is illegal to write"` and update `Writing_any_present_state_of_an_optional_Result_throws` to failure-only states plus a new success-optional byte-oracle case (`Envelope`-style optional fixture vs `PlainResultEnvelope`).

- [ ] **Step 2: Run to verify fail** — success cases throw with the old message; oracle test throws before comparing.
- [ ] **Step 3: Implement.** In `ResultSerializers`: rename/reword the const (XML-doc comment updated to name the two throw states, not "deserialization-only"). In `ResultSerializer<T>`:

```csharp
public void Write(ref ProtoWriter.State state, Result<T> value)
{
	if (!value.TryGetValue(out Success<T> success))
		throw new InvalidOperationException(ResultSerializers.IllegalWriteMessage);
	if (typeof(T) == typeof(DateTimeOffset))
	{
		var raw = success.Value;
		var dto = Unsafe.As<T, DateTimeOffset>(ref raw);
		state.WriteString(dto.ToString("O", CultureInfo.InvariantCulture), null);
		return;
	}
	WriteScalar(ref state, success.Value);
}
```

`WriteScalar` mirrors `ReadScalar` branch-for-branch with the write counterparts: `state.WriteBoolean`, widened `state.WriteInt32` for byte/sbyte/short/ushort, `WriteInt32`/`WriteUInt32`/`WriteInt64`/`WriteUInt64`, `WriteSingle`/`WriteDouble`, `BclHelpers.WriteDecimalString`, `(ushort)` + `WriteUInt16` for char, `WriteString`, `GuidWire.Write`, `BclHelpers.WriteDateOnly`/`WriteTimeOnly`/`WriteTimestamp`/`WriteDuration`. **The byte-oracle test is the arbiter of every helper choice** — if a named helper doesn't exist under that exact name on this protobuf-net version, find the Level300 write counterpart of the same `ReadScalar` branch (the reference model in `AssertSuccessWriteMatchesPlain` proves correctness bit-for-bit; do not hand-wave a passing alternative encoding). Keep the `DateTimeOffset` "O" string form byte-shared with `DateTimeOffsetSerializer.Write` — add a `DateTimeOffset` oracle case asserting `Envelope<DateTimeOffset>` success bytes equal `PlainEnvelope<DateTimeOffset>` bytes (both flow through the two implementations, proving they agree).
Also update the class-level XML doc: the type is no longer "deserialization-only" — it reads and writes; failure/default are the illegal-write states.

- [ ] **Step 4: Run to verify pass** — full `Infrastructure.Web.Grpc.Tests`.
- [ ] **Step 5: Commit**: `feat(grpc): Result<T> success-unwrap on serialize — union never rides the wire`

---

### Task 2: Midgard gRPC — `ResultEnumSerializer<TEnum>` success write

**Files:**
- Modify: `Midgard/src/Infrastructure.Web.Grpc/ResultEnumSerializer.cs`
- Test: `Midgard/tests/Infrastructure.Web.Grpc.Tests/ResultSerializerTests.cs` (the existing enum fixtures `WireStatus`/`WireAccess`/`Envelope<T>`)

**Interfaces:**
- Consumes: `ResultSerializers.IllegalWriteMessage` (Task 1), the existing `ToBits`/`FromBits`/`_definedBits`/`_isFlags` statics.
- Produces: `Write` — success with a defined value → `state.WriteInt64(ToBits(value))` varint; success with an undefined value (or flags leftover bits) → `InvalidOperationException` with message `$"'{success.Value}' is an undefined value of '{typeof(TEnum)}' and is illegal to write."`; failure/default → `IllegalWriteMessage`.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
void Writing_a_success_enum_emits_the_plain_fields_exact_wire_bytes()
{
	var model = TestModel.Create();
	var wrapped = TestModel.Serialize(model, new Envelope<WireStatus> { Value = new Success<WireStatus>(WireStatus.Inactive) });
	var plain = TestModel.Serialize(model, new PlainEnvelope<WireStatus> { Value = WireStatus.Inactive });
	wrapped.ShouldBe(plain);
}

[Fact]
void Writing_an_undefined_enum_success_throws_the_illegal_write_law()
{
	var exception = Should.Throw<InvalidOperationException>(() =>
		TestModel.Serialize(TestModel.Create(), new Envelope<WireStatus> { Value = new Success<WireStatus>((WireStatus)99) }));
	exception.Message.ShouldBe($"'{(WireStatus)99}' is an undefined value of '{typeof(WireStatus)}' and is illegal to write.");
}

[Fact]
void Writing_a_failed_enum_Result_still_throws()
{
	var exception = Should.Throw<InvalidOperationException>(() =>
		TestModel.Serialize(TestModel.Create(), new Envelope<WireStatus> { Value = new Failure(ParseFailure.Malformed, "x", nameof(WireStatus)) }));
	exception.Message.ShouldBe(IllegalWriteMessage);
}
```

Delete `Writing_a_Result_of_an_enum_throws_like_every_other_row` (success no longer throws).

- [ ] **Step 2: Run to verify fail.**
- [ ] **Step 3: Implement** — `Write` per the Produces block; definedness check reuses the same `_isFlags ? (ToBits(v) & ~_definedBits) == 0 : Enum.IsDefined(v)` expression `Read` uses (hoist it into a `static bool IsDefined(TEnum value)` both call). Update the class XML doc (no longer write-always-throws).
- [ ] **Step 4: Run to verify pass** — full `Infrastructure.Web.Grpc.Tests`.
- [ ] **Step 5: Commit**: `feat(grpc): Result<TEnum> success-unwrap — varint out, undefined values illegal to write`

---

### Task 3: Midgard JSON — success-unwrap in the STJ converters

**Files:**
- Modify: `Midgard/src/Infrastructure.Web.Server/Json/ResultJsonConverter.cs` (both converters' `Write`)
- Test: `Midgard/tests/Infrastructure.Web.Server.Tests/Json/ResultJsonConverterTests.cs`

**Interfaces:**
- Consumes: nothing new — `JsonSerializer.Serialize(writer, value, options)` recursion reaches the registered lexical converters, so §7 forms hold by construction.
- Produces: `ResultJsonConverter<T>.Write` — success serializes the unwrapped value; failure/default throw `"a failed or default Result<T> is illegal to write"`. `NullableResultJsonConverter<T>.Write` — null → `WriteNullValue` (unchanged); present success unwraps; present failure/default throws.

- [ ] **Step 1: Write failing tests** — update the test-local `DeserializationOnlyMessage` const to `IllegalWriteMessage` with the new wording; replace the success-throws assertions:

```csharp
[Fact]
void Write_success_emits_the_clean_unwrapped_value()
{
	var options = NorseJsonTestOptions.Create();
	Result<TimeSpan> result = new Success<TimeSpan>(new TimeSpan(1, 2, 3, 4));

	JsonSerializer.Serialize(result, options).ShouldBe("\"P1DT2H3M4S\"");
}

[Fact]
void Write_success_string_round_trips_through_the_wrapped_type()
{
	var options = NorseJsonTestOptions.Create();
	Result<string> result = "Bifrost";

	var json = JsonSerializer.Serialize(result, options);

	json.ShouldBe("\"Bifrost\"");
	JsonSerializer.Deserialize<Result<string>>(json, options).Value
		.ShouldBeOfType<Success<string>>().Value.ShouldBe("Bifrost");
}

[Fact]
void Write_default_result_throws_the_illegal_write_law()
{
	var options = NorseJsonTestOptions.Create();

	var exception = Should.Throw<InvalidOperationException>(() =>
		JsonSerializer.Serialize(default(Result<int>), options));
	exception.Message.ShouldBe(IllegalWriteMessage);
}
```

Keep the failed-required and failed-optional throw tests, retargeted to the new message; keep null-optional writes `null`.

- [ ] **Step 2: Run to verify fail.**
- [ ] **Step 3: Implement** — in `ResultJsonConverter<T>`:

```csharp
public override void Write(Utf8JsonWriter writer, Result<T> value, JsonSerializerOptions options)
{
	if (!value.TryGetValue(out Success<T> success))
		throw new InvalidOperationException("a failed or default Result<T> is illegal to write");
	JsonSerializer.Serialize(writer, success.Value, options);
}
```

Nullable variant mirrors (null branch unchanged, then delegates to the same logic). Rewrite both `Write` XML docs: the request-write path's production consumer remains gRPC-only clients' *absence* — text channels are for strangers (§1.3) — the path is legal everywhere and exercised by the round-trip suites; the throw states are failure and default, per the success-unwrap spec.

- [ ] **Step 4: Run to verify pass** — full `Infrastructure.Web.Server.Tests`.
- [ ] **Step 5: Commit**: `feat(json): Result<T> success-unwrap on serialize`

---

### Task 4: Midgard JSON — the `[DataContract]` opt-in law (§4b)

**Files:**
- Create: `Midgard/src/Infrastructure.Web.Server/Json/OptInContractModifier.cs`
- Modify: `Midgard/src/Infrastructure.Web.Server/Json/MvcBuilderExtensions.cs` (wire the modifier — the `EnumNameRegistry` parameter arrives in Task 6; this task keeps the current signature)
- Test: `Midgard/tests/Infrastructure.Web.Server.Tests/Json/OptInContractModifierTests.cs`

**Interfaces:**
- Produces: `public static class OptInContractModifier { public static void Apply(JsonTypeInfo typeInfo); }` — for `JsonTypeInfoKind.Object` types carrying `[DataContract]`, removes every property whose `AttributeProvider` lacks `[DataMember]`. Non-`[DataContract]` types untouched. Wired in `AddNorseJson` via `options.JsonSerializerOptions.TypeInfoResolver = (options.JsonSerializerOptions.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver()).WithAddedModifier(OptInContractModifier.Apply);` placed before the converter registrations.

- [ ] **Step 1: Write failing tests**

```csharp
[DataContract]
sealed record OptInFixture
{
	[DataMember(Order = 1)] public string Name { get; set; } = "";
	public string Shadow { get; set; } = "";   // undecorated — must not exist to STJ
}

sealed record PlainFixture
{
	public string Name { get; set; } = "";     // no [DataContract] — default STJ behavior holds
}

[Fact]
void A_non_DataMember_property_on_a_DataContract_type_does_not_serialize()
{
	var options = NorseJsonTestOptions.Create();
	var json = JsonSerializer.Serialize(new OptInFixture { Name = "Alice", Shadow = "leak" }, options);

	json.ShouldBe("""{"name":"Alice"}""");
}

[Fact]
void An_incoming_member_naming_a_stripped_property_dies_under_the_unmapped_ratchet()
{
	var options = NorseJsonTestOptions.Create();

	Should.Throw<JsonException>(() =>
		JsonSerializer.Deserialize<OptInFixture>("""{"name":"Alice","shadow":"second door"}""", options));
}

[Fact]
void A_type_without_DataContract_keeps_default_membership()
{
	var options = NorseJsonTestOptions.Create();
	JsonSerializer.Serialize(new PlainFixture { Name = "Alice" }, options).ShouldBe("""{"name":"Alice"}""");
}
```

`NorseJsonTestOptions.Create()` must mirror the full `AddNorseJson` pass — extend the helper to apply the modifier and `UnmappedMemberHandling.Disallow` exactly as the production wiring does (read the helper first; keep it the one mirror of `AddNorseJson`).

- [ ] **Step 2: Run to verify fail** — `Shadow` serializes today; the unmapped test fails because `shadow` binds.
- [ ] **Step 3: Implement** — `Apply` iterates `typeInfo.Properties` in reverse, removing non-`[DataMember]` entries when the declaring type carries `[DataContract]` (`typeInfo.Type.IsDefined(typeof(DataContractAttribute), inherit: false)`; property check via `property.AttributeProvider?.IsDefined(typeof(DataMemberAttribute), inherit: false)`). Wire into `AddNorseJson` per the Produces block, with the §4b comment: three serializers, one membership definition, STJ made to honor the WCF vocabulary it ignores natively.
- [ ] **Step 4: Run to verify pass** — full `Infrastructure.Web.Server.Tests` (existing contract round-trip tests prove `[DataMember]` members still flow).
- [ ] **Step 5: Commit**: `feat(json): [DataContract] opt-in law — non-DataMember members do not exist to STJ`

---

### Task 5: Midgard — `EnumNameTable`, `EnumNameRegistry`, `EnumLexical`

**Files:**
- Create: `Midgard/src/Infrastructure.Web.Server/Xml/EnumNameTable.cs`, `.../Xml/EnumNameRegistry.cs`, `.../Xml/EnumLexical.cs`
- Test: `Midgard/tests/Infrastructure.Web.Server.Tests/Xml/EnumLexicalTests.cs`

**Interfaces:**
- Produces (public — generated host code, JSON converters, and the OpenAPI transformer all consume these; exact shapes later tasks compile against):

```csharp
public sealed class EnumNameTable(Type enumType, string typeName, string[][] names, long[] values)
{
	public Type EnumType { get; } = enumType;
	public string TypeName { get; } = typeName;           // for Failure.ExpectedType and throw messages
	public int Count => values.Length;
	public string Name(int memberIndex, int styleIndex) => names[memberIndex][styleIndex];
	public long Value(int memberIndex) => values[memberIndex];
}

public sealed class EnumNameRegistry
{
	public void Add(EnumNameTable table);                  // throws InvalidOperationException on duplicate EnumType
	public bool TryGet(Type enumType, out EnumNameTable table);
}

public static class EnumLexical
{
	public static string Format<TEnum>(EnumNameTable table, TEnum value, int styleIndex)
		where TEnum : unmanaged, Enum;                     // exact value match → name; undefined → InvalidOperationException
	public static Result<TEnum> Parse<TEnum>(EnumNameTable table, string content, int styleIndex)
		where TEnum : unmanaged, Enum;                     // exact name match → Success; miss → Failure(Malformed, content, table.TypeName)
}
```

- `Format` undefined message: `$"'{value}' is an undefined value of '{table.EnumType}' and is illegal to write."` — byte-identical to the emitter's historical form so downstream assertions don't fork.
- `Parse` on empty content: same miss path — `Failure(ParseFailure.Malformed, content, table.TypeName)` (present-empty is content, and `""` is not a name; spec §8.2 holds).
- Bit conversion between `TEnum` and `long` duplicates the `ToBits`/`FromBits` idiom from `ResultEnumSerializer` (different assembly; note the twinning in a comment on both).

- [ ] **Step 1: Write failing tests** — construct a hand-built table for a fixture enum (`enum TableStatus { Active = 1, Inactive = 2 }`, names `[["active","Active","active","ACTIVE","active"], …]` matching `XmlCaseStyle`'s enum order Camel/Pascal/Snake/Upper/Lower) and assert: `Format(table, TableStatus.Active, (int)XmlCaseStyle.SnakeCase)` returns the snake column; `Format` of `(TableStatus)99` throws the exact undefined message; `Parse` exact-match returns `Success`; wrong-case (`"Active"` at camel index) and off-list and `""` return `Failure` with `Reason == Malformed`, `Input == content`, `ExpectedType == table.TypeName`; registry add/duplicate-throw/tryget.
- [ ] **Step 2: Run to verify fail** (compile failure).
- [ ] **Step 3: Implement** per the signatures. Scan loops are linear over `Count` (tables are small; no dictionary ceremony).
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit**: `feat(xml): EnumNameTable + registry + EnumLexical — one enum mechanism, three consumers`

---

### Task 6: Midgard JSON — enum converters over the registry; `AddNorseJson(EnumNameRegistry)`

**Files:**
- Create: `Midgard/src/Infrastructure.Web.Server/Json/EnumLexicalJsonConverters.cs`
- Modify: `.../Json/ResultJsonConverterFactory.cs` (delete `ThrowIfEnum`; route enums), `.../Json/MvcBuilderExtensions.cs` (signature + wiring)
- Test: `.../Tests/Json/EnumLexicalJsonConverterTests.cs`; update `ResultJsonConverterTests` (delete the named-refusal test + `GapStatus` fixture)

**Interfaces:**
- Consumes: `EnumNameRegistry`/`EnumLexical` (Task 5), `NorseXmlOptions.CaseStyle` (existing), `FailureDetail.Render` (existing).
- Produces:
  - `sealed class EnumLexicalJsonConverterFactory(EnumNameRegistry registry, NorseXmlOptions options) : JsonConverterFactory` — `CanConvert`: `type.IsEnum`. `CreateConverter`: registry miss → `NotSupportedException($"no generated name table for enum '{type.Name}' — an enum outside every facade closure has no text wire law")`; hit → `PlainEnumJsonConverter<TEnum>`.
  - `sealed class PlainEnumJsonConverter<TEnum>(EnumNameTable table, int styleIndex)` — Read: string token → `EnumLexical.Parse`; `Success` → value, `Failure` → `throw new JsonException(FailureDetail.Render(failure))` (the `LexicalScalars.Read` posture); any non-string token (numbers included) → `JsonException` (names-never-numerics). Write: `EnumLexical.Format`.
  - `sealed class ResultEnumJsonConverterFactory(EnumNameRegistry registry, NorseXmlOptions options) : JsonConverterFactory` — `CanConvert`: `Result<TEnum>` / `Result<TEnum>?` where the argument `IsEnum`. Converters: null → `new Failure(ParseFailure.Empty, "", table.TypeName)` for required (renders "required value missing" via `FailureDetail` — assert literally), `null` for optional; string token → `EnumLexical.Parse` captured as data (never throws on content); number/bool/object/array tokens → captured `Failure(Malformed, <token text or kind>, table.TypeName)`; Write: success → `EnumLexical.Format` string, failure/default → the illegal-write message.
  - `MvcBuilderExtensions.AddNorseJson(this IMvcBuilder builder, EnumNameRegistry registry)` — registers the registry singleton, then configures `JsonOptions` with a `NorseXmlOptions` dependency: `builder.Services.AddOptions<JsonOptions>().Configure<NorseXmlOptions>((options, xmlOptions) => { /* modifier + all converters incl. the two enum factories + Disallow */ });` replacing the current `AddJsonOptions` body. `NorseXmlOptions` resolving from DI **is** the state-the-style-once law: `AddNorseXml` registers it; a host composing JSON without XML fails at startup resolving it — loud, documented in the method's XML doc (Futhark has one consumer and it composes both; spec §2.3.5).
- `ResultJsonConverterFactory.CanConvert` must now return **false** for enum-argument `Result<T>` (the enum factory owns them) — ordering in the converter list must not matter.

- [ ] **Step 1: Write failing tests** — with a fixture registry (hand-built `TableStatus` table from Task 5's idiom) and `NorseXmlOptions { CaseStyle = XmlCaseStyle.CamelCase }`:
  - plain enum: `Serialize(TableStatus.Active)` → `"\"active\""`; `Deserialize("\"active\"")` → value; `"\"Active\""` → `JsonException`; `"1"` (number token) → `JsonException`; unregistered enum type → `NotSupportedException` with the named message.
  - `Result<TableStatus>`: `"\"active\""` → `Success`; `"\"Active\""` → `Failure(Malformed)`; `"1"` → `Failure(Malformed)`; `null` → `Failure` rendering exactly `"required value missing"` via `FailureDetail.Render`; `Result<TableStatus>?` null → CLR null; Write success → `"\"active\""`; Write failure → throws the illegal-write message.
- [ ] **Step 2: Run to verify fail.**
- [ ] **Step 3: Implement** per Produces. Delete `ThrowIfEnum` + its test + `GapStatus`.
- [ ] **Step 4: Run to verify pass** — full `Infrastructure.Web.Server.Tests`.
- [ ] **Step 5: Commit**: `feat(json): enum wire law — governed names over the generated registry, both plain and Result-wrapped`

---

### Task 7: Midgard generator — `[DataMember]` filter + NORSE029 flags ban

**Files:**
- Modify: `Midgard/gen/Infrastructure.Web.Server.Xml.Generator/ClosureWalker.cs`, `Diagnostics.cs`
- Test: `Midgard/tests/Infrastructure.Web.Server.Xml.Generator.Tests/` (extend the diagnostics test file; read its harness idiom first)

**Interfaces:**
- Produces: `GetInstanceProperties` additionally requires the property carry `[DataMember]` (metadata name `System.Runtime.Serialization.DataMemberAttribute`) — undecorated members enter no closure, no shape, no diagnostic. New descriptor:

```csharp
public static readonly DiagnosticDescriptor FlagsEnumInClosure = new(
	"NORSE029", "Flags enum in a facade closure",
	"flags don't translate to strangers — model the option set explicitly ('{0}' on '{1}')",
	"Norse.Xml", DiagnosticSeverity.Error, isEnabledByDefault: true);
```

fired wherever the walker classifies an enum-typed member (plain or `Result`-wrapped, either closure) whose enum carries `[Flags]` (`System.FlagsAttribute` by metadata name).

- [ ] **Step 1: Write failing tests** — harness fixtures: (a) a contract with `[DataMember] Result<string> Name` plus an undecorated `public string Shadow` → compiles clean, generated shape's write output contains no `shadow` attribute in any casing, zero diagnostics; (b) a request contract with a `[DataMember] Result<AccessRights>` member where `[Flags] enum AccessRights { None = 0, Read = 1 }` → exactly NORSE029, squiggle on the member symbol; (c) a response contract with a plain `[Flags]` member → NORSE029; (d) the same flags contract with **no** controller exposing it → zero diagnostics (exposure scoping).
- [ ] **Step 2: Run to verify fail.**
- [ ] **Step 3: Implement** both changes.
- [ ] **Step 4: Run to verify pass** — full generator test project, plus re-run the existing exposure-scoping negative explicitly.
- [ ] **Step 5: Commit**: `feat(xml-gen): [DataMember] opt-in membership + NORSE029 flags ban at the facade`

---

### Task 8: Midgard generator — table/registration emission; emitters fold onto `EnumLexical`; Result unwrap-on-write emission

**Files:**
- Modify: `XmlShapeGenerator.cs`, `ReaderEmitter.cs`, `WriterEmitter.cs`, `RegistrationEmitter.cs`
- Test: `.../Xml.Generator.Tests/` — `WriterEmissionTests`, `ReaderEmissionTests`, `RegistrationEmissionTests` updates

**Interfaces:**
- Consumes: `EnumNameTable`/`EnumNameRegistry`/`EnumLexical` (Task 5 — emitted code references them `global::`-qualified), `ShapeModel`'s existing `EnumTableModel`/`EnumValueModel` (the data already exists in the model).
- Produces:
  1. Per reachable enum, one emitted `static readonly global::Norse.Infrastructure.Web.Server.Xml.EnumNameTable` field (names ×5 styles from the existing `WireNames`, values from `EnumValueModel.Value`), plus `internal static class NorseEnumNameRegistration { public static EnumNameRegistry Build() }` in the same `{RootNamespace}.NorseXmlShapes` namespace as `NorseXmlShapeRegistration` (mirror `RegistrationEmitter`'s shape exactly).
  2. `ReaderEmitter`: the per-enum `{Safe}ParseResult`/`{Safe}ParseFlags` helper emission is **deleted**; enum member reads call `EnumLexical.Parse<TEnum>(table, content, styleIndex)` and feed the returned `Result` through the same accumulation path scalar members use. Flags paths are unreachable post-NORSE029 — delete the emission, not just the callers.
  3. `WriterEmitter`: enum writes call `EnumLexical.Format<TEnum>(table, value, styleIndex)`; the emitted per-enum write tables and flags greedy/canonical emission are deleted. Result-wrapped members restore unwrap-on-success: emitted form per member —

```csharp
if (!value.Limit.TryGetValue(out global::Norse.Primitives.Success<decimal> __limit))
	throw new global::System.InvalidOperationException("a failed or default Result<T> is illegal to write");
writer.WriteAttributeString(_limitNames[(int)style], global::Norse.Infrastructure.Web.Server.Xml.XmlLexical.Format(__limit.Value));
```

(optional `Result<T>?` members gate on `HasValue` first and omit when null — the existing optional-omission shape). The truncate-on-unconditional-throw machinery (`WriterEmitter`'s truncation flag and its doc remarks) is deleted.

- [ ] **Step 1: Write failing tests** — (a) registration: generated `NorseEnumNameRegistration.Build()` contains a table for every fixture enum, none for unexposed enums; (b) writer: a request fixture with `Result<decimal>` + `Result<TableStatus>` members at `SnakeCase` emits byte-exact XML for success values (the Task 6-era byte-exact assertions extended to include Result members carrying `Success` — e.g. `<quote_request limit="1234.56" status="inactive" />`), throws the pinned illegal-write message for failure/default members, and throws the undefined-enum message for `(TableStatus)99`; (c) reader: enum attribute parse still yields `Success`/accumulated `Failure` with correct paths (existing reader assertions must stay green with `EnumLexical` underneath — behavior-identical fold); (d) the cached-vs-recomputed incrementality test still reports `Cached`/`Unchanged` (the fold must not capture non-equatable state in the pipeline).
- [ ] **Step 2: Run to verify fail.**
- [ ] **Step 3: Implement** — emission changes only; all algorithm behavior now lives in Task 5's runtime.
- [ ] **Step 4: Run to verify pass** — full generator test project + full `Infrastructure.Web.Server.Tests` (formatter tests exercise generated-shape behavior through the registry).
- [ ] **Step 5: Commit**: `feat(xml-gen): emit enum tables, fold parse/write onto EnumLexical, restore Result unwrap-on-write`

---

### Task 9: Midgard OpenAPI — governed `enum:` lists

**Files:**
- Create: `Midgard/src/Infrastructure.Web.Server/OpenApi/EnumSchemaTransformer.cs`
- Test: `.../Tests/OpenApi/TransformerTests.cs` (extend)

**Interfaces:**
- Consumes: `EnumNameRegistry`, `NorseXmlOptions` (constructor-injected — schema transformers are DI-activated).
- Produces: `sealed class EnumSchemaTransformer(EnumNameRegistry registry, NorseXmlOptions options) : IOpenApiSchemaTransformer` — for any schema whose CLR type is an enum (plain member) it sets `type: string`, clears any numeric enum values, and sets the `enum:` list to the governed names in the host's style, in table order. `ResultSchemaTransformer` (existing) is modified so an unwrapped `Result<TEnum>` yields the same string schema + list (route both through one shared helper in the new file: `internal static void ApplyGovernedList(OpenApiSchema schema, EnumNameTable table, int styleIndex)`). Registry miss inside the transformer → throw (an enum reached the document without a table — the same impossible-by-construction tripwire posture as the formatters).

- [ ] **Step 1: Write failing tests** — via the existing minimal-fixture-app document pipeline: a response contract with a plain `TableStatus` member renders `type: string` with `enum: [active, inactive]` (CamelCase host); a request contract's `Result<TableStatus>` member renders identically; the document still contains the strings `Outcome`/`Result` nowhere (existing guard stays green).
- [ ] **Step 2: Run to verify fail.**
- [ ] **Step 3: Implement**; register `EnumSchemaTransformer` in the test pipeline exactly as the host will (`options.AddSchemaTransformer<EnumSchemaTransformer>()`).
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit**: `feat(openapi): governed enum string lists from the generated tables`

---

### Task 10: Midgard — full realm green

- [ ] **Step 1:** `dotnet test Midgard` — entire realm green, warnings-as-errors clean.
- [ ] **Step 2:** Re-run the Task 7 exposure-scoping negatives explicitly (undecorated-shadow and unexposed-flags fixtures) — the two laws most likely to regress during Task 8's emitter rework.
- [ ] **Step 3:** Commit stragglers; leave the branch local. **No tag, publish, or PR — Buvy runs ship gates.**

---

### Task 11: Yggdrasil — enum row 13, shadow proof, typed-proxy swoop

**Files:**
- Modify: `Yggdrasil/tests/Hosting.Web.Server.Tests/Parity/ParityContracts.cs` (+ `ParityService.cs` validator/handler), `Swoop/TriProtocolSwoopTests.cs`, `Swoop/LexicalCorpus.cs`, `Swoop/WiringTests.cs` (OpenAPI enum assertion)

**Interfaces:**
- Consumes: everything above via `UseProjectReferences=true`; `NorseEnumNameRegistration.Build()` (generated in this test compilation, same namespace as `NorseXmlShapeRegistration`); the implicit `T → Result<T>` conversion (Task 0).
- Produces: the spec's test-doctrine deltas — corpus 13 of 13, opt-in law proven on all three channels, typed proxy replacing the mirror machinery.

- [ ] **Step 1: Contract changes.** `ParityRequest` gains:

```csharp
[DataMember(Order = 15)] public Result<ParityStatus> Status { get; set; }

/// <summary>
/// The §4a binding shadow, proven end-to-end: undecorated, so under the opt-in law it does not
/// exist to protobuf-net, STJ, or the XML closure walker — no NORSE022, no wire presence, no
/// second door. get derives from the union (Failure round-trips its Input); set runs the funnel.
/// </summary>
public string StatusText
{
	get => Status.TryGetValue(out Success<ParityStatus> success) ?
		success.Value.ToString() :
		Status.TryGetValue(out Failure failure) ?
			failure.Input :
			"";
	set => Status = value == nameof(ParityStatus.Active) ?
		new Success<ParityStatus>(ParityStatus.Active) :
		value == nameof(ParityStatus.Inactive) ?
			new Success<ParityStatus>(ParityStatus.Inactive) :
			new Failure(ParseFailure.Malformed, value, nameof(ParityStatus));
}
```

(Enum members are `Result`-wrapped **request-side** now that gRPC and JSON both carry the law; `ParityRequest`'s members become `get; set;` per the ratified mutability rule — update the record's doc comment accordingly.) `ParityRequestValidator` adds `RuleFor(x => x.Status).ResultRequired().OverridePropertyName("parityRequest/@status");`. `EchoParityHandler` echoes `Status = Unwrap(wire.Status)` instead of the hardcoded `ParityStatus.Active`. `JsonBody`/`XmlBody` gain `"status": "active"` / `status="active"`; `ParseParityReportXml` reads the real attribute; `AssertCanonicalReport` asserts `report.Status.ShouldBe(ParityStatus.Active)`.

- [ ] **Step 2: Swoop rework — typed proxy.** Delete `ParityRequestWireFixture`, `ParityTagWireFixture`, and `BuildValidRequestBytes`; replace `EchoRawAsync`'s hand-built method for the success/round-trip tests with the real protobuf-net.Grpc client:

```csharp
public IParityService CreateClient()
{
	var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = App.GetTestServer().CreateHandler() });
	return channel.Intercept(new OutcomeClientInterceptor()).CreateGrpcService<IParityService>();
}
```

(`ProtoBuf.Grpc.Client.GrpcClientFactory` — mirror `Hosting.Web.Client`'s wiring idiom; read it first.) Valid requests construct `ParityRequest` directly via the implicit conversion — `new() { IsActive = true, Count = 42, Amount = 1234.56m, …, Status = ParityStatus.Active, Tags = [new() { Value = "tag-one" }, new() { Value = "tag-two" }] }`. **Keep** a minimal raw-bytes path (the existing marshaller technique, trimmed to one helper) solely for `Required_absent_detail_wording…`'s empty-payload call — an omitting client cannot be authored through a proxy that throws on default; hand-built absence stays the honest fixture, per the spec's deliberate exception. Update the fixture's remarks docs (mirror-contract era is over; say why the one raw path remains).

- [ ] **Step 3: Corpus row 13 + opt-in probes.** `_baseFields` gains `("status", "active", "active")`. New rows: `{ "status", "inactive", "inactive", true }`, `{ "status", "Active", "Active", false }` (wrong case), `{ "status", "99", "99", false }`, `{ "status", "not-a-status", "not-a-status", false }`. Update the corpus header doc (13 of 13 — the enum row is live; delete the response-side-only accounting). Add the opt-in probe to the swoop: a JSON POST whose body includes `"statusText": "Active"` → 400 (unmapped member under Disallow — the shadow is invisible); and assert the XML success response contains no `statusText` attribute.
- [ ] **Step 4: OpenAPI wiring.** `SwoopHostFixture` adds `options.AddSchemaTransformer<EnumSchemaTransformer>()` and `.AddNorseJson(...)` becomes `.AddNorseJson(Norse.Hosting.Web.Server.Tests.NorseXmlShapes.NorseEnumNameRegistration.Build())`. `WiringTests` gains: the live document's parity schemas carry `enum: [active, inactive]` on the status member (both the request and response projections), and still contain `Outcome`/`Result` nowhere.
- [ ] **Step 5: Run to verify** — full `Hosting.Web.Server.Tests` green. Also `dotnet build` the test project and confirm the generator emitted the enum table registration (the exposure law in the real host compilation).
- [ ] **Step 6: Update `ParityContracts` docs** — the `Status` remark's "what still keeps the enum row off ParityRequest" accounting is now history; rewrite it to state the row is live on all three channels and point at both 2026-08-02 specs.
- [ ] **Step 7: Commit** on Yggdrasil `feature/enum-wire-and-success-unwrap`: `feat: enum row live on all three channels — corpus 13/13, typed-proxy swoop, opt-in law proven`

---

## Self-review notes (run before handoff)

- Spec coverage — enum wire law: §2.1 (governed names, no override attribute) → T5/T6/T9; §2.2 (flags ban) → T7; §2.3 (tables/EnumLexical/three consumers/fail-loud/style-once) → T5/T6/T8; §2.4 (JSON posture: exact match, number tokens malformed, null presence) → T6; §2.5 (OpenAPI lists) → T9/T11; §2.6 (no flags client-side) → doctrine, no code; §3 deltas → T7/T8/T11.
- Spec coverage — success-unwrap: §2 (table of write states) → T1/T2/T3/T8; §4 (implicit conversion + interference check) → T0; §4a (shadow, stateless, on-contract) → T11 Step 1; §4b (opt-in on all channels) → T4 (STJ), T7 (walker), protobuf-net native; §5 consequences 1–4 → T1–T3, T8, T11; §5.5 (doc updates) → T11 Step 6 plus the spec pointer notes already stamped 2026-08-02.
- Known plan-locked decisions flagged in the specs: `EnumLexical` residence (`Xml/`, the `XmlLexical` precedent); the style-once seam (`AddNorseJson(EnumNameRegistry)` + `NorseXmlOptions` from DI, JSON-without-XML fails startup loudly); NORSE029; the illegal-write message wording (`"a failed or default Result<T> is illegal to write"`); `ResultRules` wording check (spec plan-detail 6): Task 11's `Required_absent…` test asserting `"required value missing"` end-to-end **is** the verification — `FailureDetail.Render` is type-agnostic, no enum-specific touch expected; if that test fails on wording, halt and flag.
- protobuf-net write-helper names in Task 1 are the mirror of `ReadScalar`'s read helpers; the byte-oracle tests are the arbiter — a wrong name fails compile or fails bytes, never passes silently.
