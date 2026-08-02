# Futhark — Opinionated XML Serialization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the ratified Futhark spec (`../specs/2026-08-01-opinionated-xml-serialization-design.md`) end to end: the Midgard XML seam + formatters + problem writer, the shape generator executing in the host compilation, the `Result<T>` funnel on all three channels, and the tri-protocol swoop proving parity against the live Yggdrasil host.

**Architecture:** Midgard owns everything (`Infrastructure.Web.Server` `Xml/`+`Json/`+`Facade/` subfolders, `Web.Grpc` surrogates, a new bundled generator); the generator walks facade-controller closures in the Yggdrasil compilation and emits shapes there; Yggdrasil wires negotiation and hosts the swoop. Svartálfheim gets one surgical prerequisite (lexical-table conformance in the parser stack). Asgard untouched.

**Tech Stack:** .NET 10, Roslyn incremental generators + `Norse.Abstractions.Emit` (`AppendCSharp`), ASP.NET Core MVC formatters (`TextInputFormatter`/`TextOutputFormatter`), `Microsoft.AspNetCore.OpenApi` transformers, System.Text.Json, protobuf-net.Grpc, xUnit v3 + Shouldly on Microsoft.Testing.Platform.

## Global Constraints

- The spec is law: `../specs/2026-08-01-opinionated-xml-serialization-design.md`. Where this plan and the spec disagree, the spec wins — halt and flag, don't improvise.
- Tabs for indentation. `var` for return assignments; explicit types with `new()` for construction. `internal sealed` default; omit default accessibility modifiers. US English everywhere.
- Warnings ratcheted to errors. Suppression law per `house-rules.md` §Analyzers. IDE0005: delete, never suppress.
- Generator emitters call `sb.AppendCSharp(...)` with raw string literals only — never `AppendLine`. Generator output is BOM-free UTF-8 LF.
- `ConfigureAwait(false)` in all `src/` async code; never in tests.
- Tests: xUnit v3 + Shouldly, no accessibility modifiers on test methods, `RandomNumberGenerator` never `System.Random`, one test project per NuGet package (generator test projects mirror the existing `Infrastructure.Web.Server.Generator.Tests` precedent).
- **`src/Directory.Build.props`, `tests/Directory.Build.props`, and `.editorconfig` in every realm are scatter-managed and immutable. Editing any of them is halt-and-ask.** Restate this in every subagent dispatch.
- Realms branch for features (`feature/futhark-xml`); subagents may commit on the local unpushed feature branch, never master, never push. **Bifröst itself is never branched and never touched** except the submodule pointers Buvy manages.
- Local dev runs `UseProjectReferences=true` (the two-crossings doctrine); cross-realm package ship gates (PR → CI → tag → publish) come at the end, in dependency order: Svartálfheim → Midgard → Yggdrasil.
- Diagnostic IDs: the platform already uses `NORSE0xx` (NORSE011 observed). This plan assigns **NORSE020–NORSE026**; the Task 5 implementer must first grep all realms for the highest existing `NORSE0\d\d` and shift the block up if any of 020–026 are taken, updating the later tasks' references in the plan file as they go.
- Doctrine numbers, verbatim from spec §8.4: max depth **32** both directions; request body cap **1 MiB** (`1_048_576`); `DtdProcessing.Prohibit`; `XmlResolver = null`; `MaxCharactersFromEntities = 0`; UTF-8 only.

## File Structure (locked decisions)

```
Svartalfheim/src/Primitives/TimeSpanParser.cs            (modify — ISO 8601 duration lexical space)
Svartalfheim/src/Primitives/RealParser.cs                (modify — reject non-finite lexemes)
Midgard/src/Infrastructure.Web.Server/Xml/
    XmlCaseStyle.cs, NorseXmlOptions.cs, XmlReadContext.cs, XmlReadFailure.cs,
    IXmlShape.cs, XmlShapeRegistry.cs, XmlLexical.cs, NameSuggestion.cs,
    XmlContractInputFormatter.cs, XmlContractOutputFormatter.cs,
    ProblemXmlWriter.cs, MvcBuilderExtensions.cs
Midgard/src/Infrastructure.Web.Server/Json/
    ResultJsonConverter.cs, ResultJsonConverterFactory.cs, LexicalJsonConverters.cs,
    MvcBuilderExtensions.cs
Midgard/src/Infrastructure.Web.Server/Facade/
    GrpcControllerBase.cs
Midgard/src/Infrastructure.Web.Server/OpenApi/
    ResultSchemaTransformer.cs, XmlMetadataTransformer.cs, UnionLeakGuardTransformer.cs
Midgard/src/Infrastructure.Web.Grpc/ResultSerializers.cs
Midgard/gen/Infrastructure.Web.Server.Xml.Generator/     (new generator project, bundled like its sibling)
Midgard/tests/Infrastructure.Web.Server.Tests/{Xml,Json,Facade,OpenApi}/  (new test folders)
Midgard/tests/Infrastructure.Web.Grpc.Tests/ResultSerializerTests.cs
Midgard/tests/Infrastructure.Web.Server.Xml.Generator.Tests/  (new test project)
Yggdrasil/src/Hosting.Web.Server/Program.cs              (modify — wiring)
Yggdrasil/tests/Hosting.Web.Server.Tests/                (new if absent — swoop suite + parity fixture)
```

Execution order: Task 0 → Tasks 1–4 (independent of each other) → Tasks 5–8 (generator, sequential) → Tasks 9–12 → Task 13. gRPC Task 4 only needs Task 0.

---

### Task 0: Svartálfheim — lexical-table conformance in the parser stack

The spec's §7 lexical table requires: `TimeSpan` reads/writes ISO 8601 duration, and non-finite `float`/`double` lexemes are rejected. `double.Parse` invariant accepts `NaN`/`Infinity`, so `RealParser` almost certainly accepts them today; `TimeSpanParser` almost certainly speaks only the invariant constant format. **Verify first, change only what fails.**

**Files:**
- Modify: `Svartalfheim/src/Primitives/TimeSpanParser.cs`
- Modify: `Svartalfheim/src/Primitives/RealParser.cs`
- Test: `Svartalfheim/tests/Primitives.Tests/` (existing project, new/extended test files)

**Interfaces:**
- Consumes: `Parser.ParseRequired<T>(ReadOnlySpan<char>, IFormatProvider)` / `ParseOptional<T>` (existing, unchanged signatures).
- Produces: `Parser.ParseRequired<TimeSpan>("P1DT2H3M4S", CultureInfo.InvariantCulture)` succeeds; `Parser.ParseRequired<double>("NaN"|"INF"|"-INF"|"Infinity"|"-Infinity", ...)` fails with `ParseFailure.Malformed`. These exact behaviors are what Tasks 3, 6, 7, 13 build on.

- [ ] **Step 1: Write the failing tests** (in the existing Primitives test project, following its file naming)

```csharp
[Fact]
void ParseRequired_TimeSpan_accepts_iso8601_duration()
{
	var result = Parser.ParseRequired<TimeSpan>("P1DT2H3M4S", CultureInfo.InvariantCulture);
	result.ShouldBeOfType<Success<TimeSpan>>()
		.Value.ShouldBe(new TimeSpan(1, 2, 3, 4));
}

[Theory]
[InlineData("NaN")]
[InlineData("Infinity")]
[InlineData("-Infinity")]
[InlineData("INF")]
[InlineData("-INF")]
void ParseRequired_double_rejects_non_finite(string lexeme)
{
	var result = Parser.ParseRequired<double>(lexeme, CultureInfo.InvariantCulture);
	result.ShouldBeOfType<Failure>();
}
```

Adjust the success-unwrap idiom to whatever the existing Primitives tests use to assert `Result<T>` cases (pattern-match on `Success<T>`/`Failure` per the union docs) — read two existing test files first and match their idiom exactly.

- [ ] **Step 2: Run to verify which fail** — `dotnet test Svartalfheim/tests/Primitives.Tests`. Expected: ISO-duration test fails (Malformed); non-finite tests fail (currently parse to Success). If any already pass, skip the corresponding implementation.
- [ ] **Step 3: Implement.** `TimeSpanParser`: accept ISO 8601 duration alongside the existing space — detect leading `P`/`-P` and route through `System.Xml.XmlConvert.ToTimeSpan` (wrap its `FormatException` into the normal `Failure` path); keep existing constant-format acceptance (reader lexical space is the parser's full space — additive, not replacing). `RealParser`: after successful `double.TryParse`/`float.TryParse`, reject `double.IsNaN(v) || double.IsInfinity(v)` with `ParseFailure.Malformed` and detail `"non-finite values are not valid boundary data"`.
- [ ] **Step 4: Run full Svartálfheim suite** — `dotnet test Svartalfheim`. Expected: all green (existing non-finite round-trip tests, if any, must be updated to expect rejection — that is a spec-mandated behavior change, note it in the commit body).
- [ ] **Step 5: Commit** on `feature/futhark-xml` in Svartálfheim: `feat: pin lexical table — ISO 8601 TimeSpan durations, reject non-finite reals`

---

### Task 1: Midgard — `XmlCaseStyle`, `XmlReadFailure`, `XmlReadContext` (path grammar + accumulation)

**Files:**
- Create: `Midgard/src/Infrastructure.Web.Server/Xml/XmlCaseStyle.cs`, `.../Xml/XmlReadFailure.cs`, `.../Xml/XmlReadContext.cs`
- Test: `Midgard/tests/Infrastructure.Web.Server.Tests/Xml/XmlReadContextTests.cs`

**Interfaces:**
- Produces (exact, later tasks compile against these):

```csharp
public enum XmlCaseStyle { CamelCase, PascalCase, SnakeCase, UpperCase, LowerCase }

public readonly record struct XmlReadFailure(string Path, string Detail);

public sealed class XmlReadContext
{
	public void PushElement(string wireName);            // root first; nested elements after
	public void PushItem(string wireName, int index);    // collection item, index is 1-based
	public void Pop();                                   // pops the matching Push*
	public string PathTo(string attributeName);          // "Policy/Coverage[2]/@limit"
	public string CurrentPath { get; }                    // "Policy/Coverage[2]"
	public void AddFailure(string path, string detail);
	public void AddScalarFailure(string attributeName, in Failure failure); // formats Failure into detail
	public bool HasFailures { get; }
	public IReadOnlyList<XmlReadFailure> Failures { get; }
}
```

- `AddScalarFailure` detail format, asserted literally: `cannot parse '{failure.Input}' as {failure.ExpectedType}` when `Reason == ParseFailure.Malformed`; `required value missing` when `Reason == ParseFailure.Empty`. (`Failure` is `Norse.Primitives.Failure` — `Input`/`ExpectedType`/`Reason` members exist today.)

- [ ] **Step 1: Write failing tests** — the §11.2 grammar asserted literally:

```csharp
[Fact]
void PathTo_renders_root_collection_index_and_attribute()
{
	var ctx = new XmlReadContext();
	ctx.PushElement("Policy");
	ctx.PushItem("Coverage", 2);
	ctx.PathTo("limit").ShouldBe("Policy/Coverage[2]/@limit");
	ctx.Pop();
	ctx.CurrentPath.ShouldBe("Policy");
}

[Fact]
void AddScalarFailure_formats_malformed_with_input_and_type()
{
	var ctx = new XmlReadContext();
	ctx.PushElement("Policy");
	ctx.AddScalarFailure("limit", new Failure(ParseFailure.Malformed, "x", "Decimal"));
	ctx.Failures.ShouldHaveSingleItem().ShouldBe(
		new XmlReadFailure("Policy/@limit", "cannot parse 'x' as Decimal"));
}
```

- [ ] **Step 2: Run to verify fail** — `dotnet test Midgard/tests/Infrastructure.Web.Server.Tests --filter XmlReadContext`. Expected: compile failure (types don't exist).
- [ ] **Step 3: Implement** — a `List<(string name, int index)>` segment stack (`index == 0` means no `[n]`); `PathTo` string-builds segments joined by `/` with `[{index}]` where index > 0 and `/@{name}` suffix; failures accumulate in a `List<XmlReadFailure>`. `internal sealed` is wrong here — these are consumed by generated code in the *host* compilation, so all three types are `public` (state this in the commit body; it is the deliberate exception, per spec §3).
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit** on Midgard `feature/futhark-xml`: `feat(xml): XmlReadContext path grammar + failure accumulation`

---

### Task 2: Midgard — `IXmlShape`, `XmlShapeRegistry`, `NorseXmlOptions`, `XmlLexical`

**Files:**
- Create: `.../Xml/IXmlShape.cs`, `.../Xml/XmlShapeRegistry.cs`, `.../Xml/NorseXmlOptions.cs`, `.../Xml/XmlLexical.cs`
- Test: `.../Tests/Xml/XmlLexicalTests.cs`, `.../Tests/Xml/XmlShapeRegistryTests.cs`

**Interfaces:**
- Produces (public — generated host code implements/consumes):

```csharp
public interface IXmlShape
{
	Type ContractType { get; }
	string RootName(XmlCaseStyle style);
	void WriteObject(XmlWriter writer, object value, XmlCaseStyle style);
	object? ReadObject(XmlReader reader, XmlCaseStyle style, XmlReadContext context);
}

public interface IXmlShape<T> : IXmlShape
{
	void Write(XmlWriter writer, T value, XmlCaseStyle style);
	T? Read(XmlReader reader, XmlCaseStyle style, XmlReadContext context);
}

public sealed class XmlShapeRegistry
{
	public void Add(IXmlShape shape);                    // throws on duplicate ContractType
	public bool TryGet(Type contractType, out IXmlShape shape);
}

public sealed class NorseXmlOptions { public XmlCaseStyle CaseStyle { get; set; } }

public static class XmlLexical   // canonical emission — §7 table, byte-exact
{
	public static string Format(bool value);             // "true"/"false"
	public static string Format(decimal value);          // invariant, plain
	public static string Format(double value);           // shortest round-trip; throws InvalidOperationException on non-finite
	public static string Format(float value);            //   — message: "non-finite values are illegal to write"
	public static string Format(Guid value);             // "D" lowercase
	public static string Format(DateTime value);         // "O"
	public static string Format(DateTimeOffset value);   // "O"
	public static string Format(DateOnly value);         // "yyyy-MM-dd"
	public static string Format(TimeOnly value);         // "O" (HH:mm:ss.fffffff)
	public static string Format(TimeSpan value);         // XmlConvert.ToString — ISO 8601 duration
	public static string Format(char value);             // single char; throws on XML-illegal control char
	// integral types: invariant ToString — generated code calls value.ToString(CultureInfo.InvariantCulture) directly
}
```

- [ ] **Step 1: Write failing tests** — one `[Theory]` per `XmlLexical` overload asserting the §7 example values byte-exact (`Format(new TimeSpan(1,2,3,4)).ShouldBe("P1DT2H3M4S")`, `Format(new DateOnly(2026,8,1)).ShouldBe("2026-08-01")`, etc.), plus `Format(double.NaN)` and `Format(double.PositiveInfinity)` asserting `Should.Throw<InvalidOperationException>`, plus registry add/duplicate/tryget tests.
- [ ] **Step 2: Run to verify fail** (compile failure).
- [ ] **Step 3: Implement** per the signature comments above. `TimeSpan` via `XmlConvert.ToString(value)`; `double`/`float` guard non-finite before `ToString(CultureInfo.InvariantCulture)` (default shortest-round-trip on .NET 10).
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit**: `feat(xml): shape seam, registry, canonical lexical emission`

---

### Task 3: Midgard — `Result<T>` STJ converters + JSON lexical pinning + strictness ratchet

**Files:**
- Create: `.../Json/ResultJsonConverter.cs` (both generic converters), `.../Json/ResultJsonConverterFactory.cs`, `.../Json/LexicalJsonConverters.cs`, `.../Json/MvcBuilderExtensions.cs`
- Test: `.../Tests/Json/ResultJsonConverterTests.cs`, `.../Tests/Json/LexicalParityTests.cs`

**Interfaces:**
- Consumes: `Parser.ParseRequired<T>`/`ParseOptional<T>`, `XmlLexical` (Task 2).
- Produces: `public static IMvcBuilder AddNorseJson(this IMvcBuilder builder)` — registers the converter factory + lexical converters + `JsonUnmappedMemberHandling.Disallow` on `JsonOptions`. Also `public sealed class ResultJsonConverterFactory : JsonConverterFactory` usable standalone in tests.

Behavior (spec §9.1, presence-aware per §8.2): string token → `ParseRequired`/`ParseOptional`; number/bool tokens → invariant-stringify → same funnel; `null` → `ParseRequired(string.Empty)` for `Result<T>`, `null` for `Result<T>?`; object/array → skip whole, typed failure. `Write`: success → clean unwrapped value using the same lexical forms as `XmlLexical` (test-infrastructure path — say so in a comment, honestly, per spec §1.3); failed/default `Result` → throw `InvalidOperationException("a failed Result<T> is illegal to write")`. `LexicalJsonConverters`: `DateTime`/`DateTimeOffset`/`TimeOnly` pinned to `"O"`, `TimeSpan` to ISO 8601 duration — plain (non-`Result`) response scalars must also emit the §7 table byte-exact, and STJ defaults trim fractional zeros, so these converters exist for plain scalars too.

- [ ] **Step 1: Write failing tests** — round-trip and funnel behavior:

```csharp
[Fact]
void Read_string_token_funnels_to_parser()
{
	var options = NorseJsonTestOptions.Create(); // helper in test file: new JsonSerializerOptions + factory + lexical converters
	var result = JsonSerializer.Deserialize<Result<DateOnly>>("\"2026-08-01\"", options);
	result.ShouldBeOfType<Success<DateOnly>>().Value.ShouldBe(new DateOnly(2026, 8, 1));
}

[Fact]
void Read_null_is_required_missing_for_required_and_null_for_optional()
{
	var options = NorseJsonTestOptions.Create();
	JsonSerializer.Deserialize<Result<int>>("null", options).ShouldBeOfType<Failure>();
	JsonSerializer.Deserialize<Result<int>?>("null", options).ShouldBeNull();
}

[Fact]
void Write_timespan_emits_iso_duration_byte_exact()
{
	var options = NorseJsonTestOptions.Create();
	JsonSerializer.Serialize(new TimeSpan(1, 2, 3, 4), options).ShouldBe("\"P1DT2H3M4S\"");
}
```

- [ ] **Step 2: Run to verify fail.**
- [ ] **Step 3: Implement** per behavior block above. Factory covers `Result<T>` and `Nullable<Result<T>>` for the full closed taxonomy including `string` (`where T : notnull`, not `struct`).
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit**: `feat(json): Result funnel converters, lexical pinning, unmapped-member ratchet`

---

### Task 4: Midgard — `Result<T>` protobuf surrogates (`Web.Grpc`)

**Files:**
- Create: `Midgard/src/Infrastructure.Web.Grpc/ResultSerializers.cs` (beside `IdentifierSerializers.cs`, same idiom)
- Test: `Midgard/tests/Infrastructure.Web.Grpc.Tests/ResultSerializerTests.cs`

**Interfaces:**
- Consumes: the existing custom-serializer registration pattern in `IdentifierSerializers.cs` — **read that file first and mirror its mechanism exactly** (it is the platform's proven protobuf-net 3.x custom-serializer home).
- Produces: registration of `Result<T>` value serializers for the closed taxonomy into the same `RuntimeTypeModel` pass the existing wiring uses. Wire form: the naked `T` (presence-tracked). Serialize failed/default `Result<T>` → throw. **Absent-member semantics land in the validation layer, not the serializer** (protobuf-net gives `default(Result<T>)` for absent members; the shared FluentValidation rules in the pipeline treat default-state `Result<T>` as required-missing — same observable §9.3 semantics, implemented where gRPC can express them). Verify open-generic registration first; fall back to the closed-set loop if protobuf-net refuses.

- [ ] **Step 1: Write failing round-trip test** — serialize a `[DataContract]` record with `Result<DateOnly>` (success) and `Result<string>?` (null) members through `RuntimeTypeModel` to a `MemoryStream` and back; assert value equality of unwrapped members and that null stays null. Add a test asserting serialize of a failed `Result<int>` throws `InvalidOperationException`.
- [ ] **Step 2: Run to verify fail.**
- [ ] **Step 3: Implement** mirroring `IdentifierSerializers.cs`.
- [ ] **Step 4: Run to verify pass** — full `Infrastructure.Web.Grpc.Tests`.
- [ ] **Step 5: Commit**: `feat(grpc): Result<T> surrogates — clean wire, presence-tracked`

---

### Task 5: Midgard — generator project: discovery, closure walk, diagnostics

**Files:**
- Create: `Midgard/gen/Infrastructure.Web.Server.Xml.Generator/Infrastructure.Web.Server.Xml.Generator.csproj` — copy the sibling `Infrastructure.Web.Server.Generator.csproj` shape verbatim (Description rewritten; `NorseRef` to `Abstractions.Emit`; bundled into Infrastructure.Web.Server's package `analyzers/dotnet/cs/`, never standalone). Check `Midgard/gen/Directory.Build.props` before assuming anything extra is needed.
- Create: `.../XmlShapeGenerator.cs` (incremental generator), `.../ClosureWalker.cs`, `.../ShapeModel.cs`, `.../NameCasing.cs`, `.../Diagnostics.cs`
- Create test project: `Midgard/tests/Infrastructure.Web.Server.Xml.Generator.Tests/` — copy the csproj + harness idiom from `Infrastructure.Web.Server.Generator.Tests` (read it first; reuse its compilation-harness helper style).

**Interfaces:**
- Consumes: `GrpcControllerBase` does not exist until Task 10 — the walker keys on **any base class named `GrpcControllerBase`** by metadata name `Norse.Infrastructure.Web.Server.Facade.GrpcControllerBase`; generator tests stub it.
- Produces: `ShapeModel` (internal to generator): per-contract member list (kind: scalar/complex/collection; wire names ×5 styles via `NameCasing.Apply(XmlCaseStyle, string)`; `Result` wrapping flags; enum tables). Diagnostics, all errors:
  - `NORSE020` raw scalar in request closure ("request scalars wrap in Result<T> or Result<T>?")
  - `NORSE021` `Result<T>` reachable in response closure
  - `NORSE022` type reachable from both request and response closures ("you shared a type across the boundary")
  - `NORSE023` non-sealed / non-object-based / generic contract type
  - `NORSE024` two members of one complex type on a contract, or post-case-transform name collision in any style
  - `NORSE025` taxonomy violation: unsupported scalar, dictionary, scalar collection, nested collection
  - `NORSE026` facade action body-bound type is not `[DataContract]`

Closure derivation (spec §4.1): controllers = classes derived from `GrpcControllerBase`; request closure = body-bound action parameter types (`[FromBody]` explicit, or the lone complex-type parameter under `[ApiController]` inference); response closure = `T` in `Task<ActionResult<T>>`/`ActionResult<T>` returns; closures include all reachable complex types; route/query-bound primitives excluded.

- [ ] **Step 1: Write failing diagnostics tests** — one per diagnostic, using the harness: a stub `GrpcControllerBase` + a controller exposing a contract violating exactly one law; assert the diagnostic ID and that the squiggle lands on the offending symbol. Include the negative: the same violating contract with **no** controller touching it produces zero diagnostics (exposure scoping — spec §15).
- [ ] **Step 2: Run to verify fail.**
- [ ] **Step 3: Implement discovery + walk + diagnostics** (no emission yet — generator emits nothing when diagnostics fire, and emission itself lands in Task 6). `NameCasing.Apply`: split on Pascal word boundaries; camel/pascal join, snake lower-joins with `_`, upper/lower flatten. Unit-test `NameCasing` directly in the same test project (`"ReadWrite"` → `read_write`/`READWRITE`/`readwrite`/`readWrite`/`ReadWrite`).
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit**: `feat(xml-gen): facade closure discovery + shape-law diagnostics NORSE020-026`

---

### Task 6: Generator — writer emission

**Files:**
- Modify: `.../XmlShapeGenerator.cs` + new `.../WriterEmitter.cs`
- Test: `.../Xml.Generator.Tests/WriterEmissionTests.cs`

**Interfaces:**
- Consumes: `IXmlShape<T>`, `XmlLexical`, `XmlCaseStyle` (Task 2). Emitted classes: `internal sealed {Contract}XmlShape : IXmlShape<{Contract}>` in namespace `{HostRootNamespace}.NorseXmlShapes`, one file per contract, plus the five-style name tables as `static readonly string[]` fields indexed by `(int)style`.
- Produces: canonical writer per spec §6 — declaration-order attributes then child elements; null scalars omitted; `Result` members unwrap success / throw on failure-or-default (`"a failed Result<T> is illegal to write"`); enums via generated name tables, flags exact-match-then-greedy-descending, undefined → throw; collections as N type-named elements; recursion into complex members via their shape classes.

- [ ] **Step 1: Write failing emission tests** — compile a fixture contract set through the harness, instantiate the generated shape via the compilation, and assert output XML strings byte-exact, e.g. a request contract `QuoteRequest` (`Result<decimal> Limit`, `Result<DateOnly>? Effective`, `List<CoverageLine> …`) at `SnakeCase` produces exactly `<quote_request limit="1234.56"><coverage_line code="GL"/></quote_request>` (omitted optional; no declaration in fragment mode — root-level tests assert the declaration too). Add flags-canonical and failed-Result-throws tests.
- [ ] **Step 2: Run to verify fail.**
- [ ] **Step 3: Implement `WriterEmitter`** — `AppendCSharp` raw-string blocks per member kind; `XmlWriterSettings` handled by the formatter (Task 9), the shape writes into a supplied `XmlWriter`.
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit**: `feat(xml-gen): canonical writer emission`

---

### Task 7: Generator — reader emission

**Files:**
- Modify: `.../XmlShapeGenerator.cs` + new `.../ReaderEmitter.cs`; Create: `Midgard/src/Infrastructure.Web.Server/Xml/NameSuggestion.cs` (Levenshtein ≤ 2 nearest-name helper, public static, unit-tested in Web.Server.Tests)
- Test: `.../Xml.Generator.Tests/ReaderEmissionTests.cs`

**Interfaces:**
- Consumes: `XmlReadContext` (Task 1), `Parser.ParseRequired/ParseOptional`, `NameSuggestion.Nearest(string candidate, IEnumerable<string> known)`.
- Produces: generated `Read` per spec §8 — presence-aware funnel (absent required → `ParseRequired(string.Empty)`; present-empty parses `""`); unknown attribute/element accumulated with suggestion (`unknown attribute — did you mean 'birthDate'?` when within distance 2); duplicate singleton element, text content, undefined enum name, duplicate flags token accumulated; dispatch order-insensitive; collection items grouped in document order; every failure carries the Task 1 path grammar.

- [ ] **Step 1: Write failing reader tests** — feed generated readers XML fragments and assert: happy-path round-trip (write→read→structural equality including required `Result<string>` carrying `""`); the `birthday`-for-`birthDate` case yields exactly two failures (`…/@birthday: unknown attribute — did you mean 'birthDate'?` and `…/@birthDate: required value missing`); three bad scalars yield three accumulated failures; element text content and duplicate singleton accumulate with correct paths.
- [ ] **Step 2: Run to verify fail.**
- [ ] **Step 3: Implement `ReaderEmitter`.**
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit**: `feat(xml-gen): presence-aware accumulating reader emission`

---

### Task 8: Generator — registration emission + `AddNorseXml`

**Files:**
- Modify: generator (new `RegistrationEmitter.cs` — emits `internal static class NorseXmlShapeRegistration { public static XmlShapeRegistry Build() }` listing every generated shape)
- Create: `Midgard/src/Infrastructure.Web.Server/Xml/MvcBuilderExtensions.cs`
- Test: `.../Xml.Generator.Tests/RegistrationEmissionTests.cs`, `.../Web.Server.Tests/Xml/AddNorseXmlTests.cs`

**Interfaces:**
- Produces: `public static IMvcBuilder AddNorseXml(this IMvcBuilder builder, XmlCaseStyle caseStyle, XmlShapeRegistry registry)` — registers `NorseXmlOptions`, the registry singleton, and inserts `XmlContractInputFormatter`/`XmlContractOutputFormatter` (Task 9 — this task registers by type; Task 9 makes them real; order the two tasks as written and let this task's formatter classes start as minimal shells that Task 9 fills, each shell throwing `NotSupportedException` from `ReadRequestBodyAsync`/`WriteResponseBodyAsync` so nothing silently half-works). The host calls `AddNorseXml(style, NorseXmlShapeRegistration.Build())`.

- [ ] **Step 1: Write failing tests** — registration emission (generated `Build()` contains every fixture shape; duplicate contract types impossible by construction) and `AddNorseXml` wiring (`MvcOptions` contains both formatter instances; `NorseXmlOptions.CaseStyle` set).
- [ ] **Step 2: Run to verify fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit**: `feat(xml): generated registration + AddNorseXml composition seam`

---

### Task 9: Midgard — the formatter pair (security law lives here)

**Files:**
- Fill: `.../Xml/XmlContractInputFormatter.cs`, `.../Xml/XmlContractOutputFormatter.cs`
- Test: `.../Web.Server.Tests/Xml/InputFormatterTests.cs`, `OutputFormatterTests.cs`, `SecurityCorpusTests.cs`

**Interfaces:**
- Consumes: registry, options, shapes, `XmlReadContext`.
- Produces: `TextInputFormatter` (`application/xml`, `text/xml`; UTF-8 only): builds the non-negotiable `XmlReaderSettings` **inline in code** — `DtdProcessing.Prohibit`, `XmlResolver = null`, `MaxCharactersFromEntities = 0`, depth guard 32, `IgnoreComments = true`, `IgnoreWhitespace = true`, `IgnoreProcessingInstructions = false` (PI encountered → session-fatal), `Async = true`. Session-fatal failures → single `ModelState` entry keyed by path-so-far or `$` for pre-root; accumulable failures → one `ModelState` entry per `XmlReadFailure` (key = `Path`, message = `Detail`); root-name mismatch accumulated. Returns `InputFormatterResult.Failure` when `HasFailures`. `TextOutputFormatter`: canonical `XmlWriterSettings` (declaration on, UTF-8 no BOM, no indent, `Async = true`), writes via registry shape; type not in registry → `InvalidOperationException` (loud refusal, spec §2).

- [ ] **Step 1: Write failing tests** — happy read/write through a hand-rolled `IXmlShape<T>` stub (formatters must not depend on generated code for their own tests); the **security corpus**: DOCTYPE payload, billion-laughs internal entity, external entity, parameter entity, 33-deep nesting bomb, UTF-16 BOM payload, PI payload — each asserted session-fatal (single ModelState error, request never reaches the shape), never resolved.
- [ ] **Step 2: Run to verify fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit**: `feat(xml): formatter pair — strict reader, canonical writer, XXE dead by construction`

---

### Task 10: Midgard — `GrpcControllerBase` + `Outcome<T>` fold + RFC 9457 problem writer

**Files:**
- Create: `.../Facade/GrpcControllerBase.cs`, `.../Xml/ProblemXmlWriter.cs`
- Test: `.../Web.Server.Tests/Facade/GrpcControllerBaseTests.cs`, `.../Web.Server.Tests/Xml/ProblemXmlWriterTests.cs`

**Interfaces:**
- Consumes: `Outcome<T>` (`Norse.Abstractions.Contracts`) — **read `Outcome{T}.cs` and the existing `OutcomeServerInterceptor` first** to fold the exact same states the gRPC edge folds; the two folds must agree state-for-state.
- Produces:

```csharp
[ApiController]
[Consumes("application/json", "application/xml")]
[Produces("application/json", "application/xml")]
public abstract class GrpcControllerBase : ControllerBase
{
	protected async Task<ActionResult<TResponse>> FoldAsync<TResponse>(ValueTask<Outcome<TResponse>> operation);
	// success → Ok(payload); not-found state → NotFound(); failure states → problem details per spec §11
}
```

  `ProblemXmlWriter`: hand-written emitter for `application/problem+xml` — RFC 9457 XML format, `urn:ietf:rfc:7807` namespace, elements not attributes, arrays as `<i>`, extension members as child elements; the `errors` extension renders `[{path, detail}]` entries. Plus MVC wiring so `ModelState` 400s and `FoldAsync` failures negotiate to problem+xml for XML clients and problem+json (with the **identical** `errors` array shape — `[{path, detail}]`, not the `ValidationProblemDetails` dictionary) for JSON clients.

- [ ] **Step 1: Write failing tests** — fold each `Outcome` state to its status; problem writer output asserted byte-exact for a fixture problem (type/title/status/detail + two `errors` entries); JSON problem payload asserted to carry the identical `errors` array shape.
- [ ] **Step 2: Run to verify fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit**: `feat(facade): GrpcControllerBase Outcome fold + RFC 9457 problem+xml writer`

---

### Task 11: Midgard — OpenAPI transformers

**Files:**
- Create: `.../OpenApi/ResultSchemaTransformer.cs`, `.../OpenApi/XmlMetadataTransformer.cs`, `.../OpenApi/UnionLeakGuardTransformer.cs`
- Test: `.../Web.Server.Tests/OpenApi/TransformerTests.cs`

**Interfaces:**
- Consumes: `Microsoft.AspNetCore.OpenApi` `IOpenApiSchemaTransformer`/document transformer seams (native pipeline — never Swashbuckle).
- Produces: `ResultSchemaTransformer` — `Result<T>`/`Result<T>?` schemas become the underlying scalar schema via a **static closed-taxonomy mapping table** (BCL types cannot carry static abstract interface members; the table is the honest equivalent, one row per §7 type: schema type + format); nullable `Result<T>?` leaves `required`; request schemas `writeOnly`, response schemas `readOnly`. `XmlMetadataTransformer` — stamps `xml: {attribute: true}` on scalar properties, item element names from item types, `wrapped: false`. `UnionLeakGuardTransformer` — document transformer that **fails loudly** (throws) if any schema references `Outcome` or `Result` by name post-transform; the symmetry law's tripwire.

- [ ] **Step 1: Write failing tests** — build an OpenAPI document from a minimal fixture app (controller + contracts) via the native pipeline; assert `Result<DateOnly>` renders `string`/`date`; assert optional leaves `required`; assert scalar property carries `xml.attribute: true`; assert guard throws when a raw `Outcome<T>` is smuggled into a signature-less schema fixture.
- [ ] **Step 2: Run to verify fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit**: `feat(openapi): union unwrap + xml metadata + leak tripwire`

---

### Task 12: Midgard — full-suite green + realm ship prep

- [ ] **Step 1:** `dotnet test Midgard` — entire realm green, warnings-as-errors clean.
- [ ] **Step 2:** Re-run the Task 5 exposure-scoping negative test explicitly (violating-but-unexposed compiles clean) — it is the law most likely to regress during Tasks 6–8.
- [ ] **Step 3:** Commit any stragglers; leave the branch local. **Do not tag, publish, or PR — Buvy runs ship gates.**

---

### Task 13: Yggdrasil — wiring, parity fixture, the tri-protocol swoop

**Files:**
- Modify: `Yggdrasil/src/Hosting.Web.Server/Program.cs` — `builder.Services.AddControllers().AddNorseJson().AddNorseXml(XmlCaseStyle.CamelCase, NorseXmlShapeRegistration.Build());` plus `app.MapControllers();` placed with the existing endpoint mappings.
- Create: `Yggdrasil/tests/Hosting.Web.Server.Tests/` if absent (csproj mirrors an existing Yggdrasil test project — check `Yggdrasil/tests/` first; if the folder has no test projects, copy a Midgard test csproj shape and adjust the `NorseRef`/project references).
- Create in the test (host-compilation) tree: `Parity/ParityContracts.cs` (`[DataContract] ParityRequest` — `Result<T>`-wrapped members covering every §7 scalar row + `List<ParityTag>` where `ParityTag` wraps a `Result<string> Value`; `[DataContract] ParityReport` — plain scalars echoing every value), `Parity/IParityService.cs` + `ParityService.cs` (`[ServiceContract]`, one `ValueTask<Outcome<ParityReport>> EchoAsync(ParityRequest)` through the real mediator pipeline), `Parity/ParityController.cs` (`: GrpcControllerBase`, one POST action).
- Create: `Swoop/TriProtocolSwoopTests.cs`, `Swoop/LexicalCorpus.cs`, `Swoop/WiringTests.cs`.

**Interfaces:** consumes everything above; produces the spec §15 suite:

- [ ] **Step 1: Write the failing swoop tests first** (they fail on wiring, then on behavior, in that order — that's the point):
  - Success parity: one `ParityRequest` via in-proc gRPC client (protobuf-net.Grpc client over `WebApplicationFactory` handler), REST-JSON POST, REST-XML POST → three `ParityReport`s, structurally equal.
  - Failure parity: three malformed scalars → JSON and XML responses carry identical `errors` arrays (paths, details, shape) as problem payloads; gRPC required-absent → the pipeline's validation failure surfaces per the existing `Outcome` error path.
  - Lexical corpus: shared accepted/rejected lexeme sets per §7 row asserted identical across both text channels (non-finite spellings in the rejected set).
  - Round-trip spine including required `Result<string>` = `""`.
  - Wiring tests: remove-the-registration probes — a test asserting `MvcOptions` contains the XML formatters, one asserting the problem writer negotiates for `Accept: application/problem+xml`... asserted by hitting the live test host, not by inspecting DI.
- [ ] **Step 2: Run to verify fail** (formatters not yet wired in host).
- [ ] **Step 3: Wire `Program.cs`; implement fixture service/controller.**
- [ ] **Step 4: Run to verify pass** — full Yggdrasil suite; also `dotnet build` the host and confirm the generator emitted shapes for exactly the parity contracts (the exposure law working in the real host).
- [ ] **Step 5: Commit** on Yggdrasil `feature/futhark-xml`: `feat: XML negotiation live — tri-protocol swoop green`

---

## Self-review notes (run before handoff)

- Spec coverage: §2→T5–8; §3→T1–8; §4→T5,T10,T13; §5→T5; §6→T2,T6; §7→T0,T2,T3,T13; §8→T1,T7,T9; §9→T3,T4,T6–7; §10→T10,T11,T13; §11→T1,T10; §12→T11; §13 (versioning) is documentation-only — no task, deliberate; §14→T5; §15→every task's tests + T13; §16–17 resolved in-plan (generator name, NORSE block, closed-table schema metadata, absent-member semantics at validation layer).
- Known deviations from spec text, both flagged inline: OpenAPI schema metadata uses a closed static table instead of static abstract interface members (BCL types cannot implement interfaces — spec intent preserved, mechanism honest); gRPC required-absent semantics live in the shared validation rules rather than the deserializer (protobuf-net absent-member reality — observable behavior identical). If Forseti objects to either, they surface at first review, not after 13 tasks.
