# Futhark — Opinionated XML Serialization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the ratified Futhark spec (`../specs/2026-08-01-opinionated-xml-serialization-design.md`) end to end: the Midgard XML seam + formatters + problem writer, the shape generator executing in the host compilation, the `Result<T>` funnel on all three channels, and the tri-protocol swoop proving parity against the live Yggdrasil host.

**Architecture:** Midgard owns the wire machinery (`Infrastructure.Web.Server` `Xml/`+`Json/` subfolders, `Web.Grpc` surrogates, a new bundled generator); **Asgard declares `GrpcControllerBase`** (`Abstractions.Web.Server/Facade/` — downstream services inherit it, and only-Yggdrasil-depends-on-Midgard would wall off a Midgard residence, ruled 2026-08-02); the generator walks facade-controller closures in the Yggdrasil compilation and emits shapes there; Yggdrasil wires negotiation and hosts the swoop. Svartálfheim gets one surgical prerequisite (lexical-table conformance in the parser stack).

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
- Local dev runs `UseProjectReferences=true` (the two-crossings doctrine); cross-realm package ship gates (PR → CI → tag → publish) come at the end, in dependency order: Svartálfheim → Asgard → Midgard → Yggdrasil.
- Diagnostic IDs: the platform already uses `NORSE0xx`. This plan originally assigned NORSE020–NORSE026, but Task 5's implementer found NORSE020/021 already live in Midgard's sibling `Infrastructure.Web.Server.Generator` (the plan's own "NORSE011 observed" note was stale) — the block shifted to **NORSE022–NORSE028**, reflected below and in Task 5's commit.
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
Asgard/src/Abstractions.Web.Server/Facade/
    GrpcControllerBase.cs                                (Asgard — downstream services inherit it)
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
- **The rendering is the one message source, hoisted:** `public static class FailureDetail { public static string Render(in Failure failure); }` in the same `Xml/` folder — `XmlReadContext.AddScalarFailure` calls it, and Task 4's validation rules call the **same method** so gRPC required-missing wording is byte-identical to the text channels' by construction, never by copied string.

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
- Create: `Midgard/src/Infrastructure.Web.Server/Validation/ResultRules.cs` — the shared FluentValidation rules this task's absent-member semantics depend on (they exist in no other task; this task owns them)
- Test: `Midgard/tests/Infrastructure.Web.Grpc.Tests/ResultSerializerTests.cs`, `Midgard/tests/Infrastructure.Web.Server.Tests/Validation/ResultRulesTests.cs`

**Interfaces:**
- Consumes: the existing custom-serializer registration pattern in `IdentifierSerializers.cs` — **read that file first and mirror its mechanism exactly** (it is the platform's proven protobuf-net 3.x custom-serializer home).
- Produces: registration of `Result<T>` value serializers for the closed taxonomy into the same `RuntimeTypeModel` pass the existing wiring uses. Wire form: the naked `T` (presence-tracked). Serialize failed/default `Result<T>` → throw. **Absent-member semantics land in the validation layer, not the serializer** (protobuf-net gives `default(Result<T>)` for absent members). `ResultRules` provides the FluentValidation extensions the pipeline's `ValidationBehavior` consumes: a required rule that fails on default-state or `Failure`-state `Result<T>`, and an optional rule that fails on `Failure`-state `Result<T>?`. **One-message-source condition (ratified at plan review):** for default-state members the rule obtains its `Failure` by literally calling `Parser.ParseRequired<T>(string.Empty, CultureInfo.InvariantCulture)` and renders it via `FailureDetail.Render` (Task 1) — byte-identical wording to the text channels' required-missing detail, same constant by construction, not a paraphrase. Same observable §9.3 semantics, implemented where gRPC can express them. Verify open-generic surrogate registration first; fall back to the closed-set loop if protobuf-net refuses.

- [ ] **Step 1: Write failing round-trip test** — serialize a `[DataContract]` record with `Result<DateOnly>` (success) and `Result<string>?` (null) members through `RuntimeTypeModel` to a `MemoryStream` and back; assert value equality of unwrapped members and that null stays null. Add a test asserting serialize of a failed `Result<int>` throws `InvalidOperationException`. Add `ResultRules` tests: default-state `Result<int>` fails the required rule with a message asserted **equal to** `FailureDetail.Render(...)` of `Parser.ParseRequired<int>(string.Empty, CultureInfo.InvariantCulture)`'s failure — literal equality, the parity condition itself.
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
- Consumes: `GrpcControllerBase` does not exist until Task 10 — the walker keys on the metadata name `Norse.Abstractions.Web.Server.Facade.GrpcControllerBase` (Asgard residence, ruled 2026-08-02); generator tests stub it.
- Produces: `ShapeModel` (internal to generator): per-contract member list (kind: scalar/complex/collection; wire names ×5 styles via `NameCasing.Apply(XmlCaseStyle, string)`; `Result` wrapping flags; enum tables). Diagnostics, all errors:
  - `NORSE022` raw scalar in request closure ("request scalars wrap in Result<T> or Result<T>?")
  - `NORSE023` `Result<T>` reachable in response closure
  - `NORSE024` type reachable from both request and response closures ("you shared a type across the boundary")
  - `NORSE025` non-sealed / non-object-based / generic contract type
  - `NORSE026` two members of one complex type on a contract, or post-case-transform name collision in any style
  - `NORSE027` taxonomy violation: unsupported scalar, dictionary, scalar collection, nested collection
  - `NORSE028` facade action body-bound type is not `[DataContract]`

Closure derivation (spec §4.1): controllers = classes derived from `GrpcControllerBase`; request closure = body-bound action parameter types (`[FromBody]` explicit, or the lone complex-type parameter under `[ApiController]` inference); response closure = `T` in `Task<ActionResult<T>>`/`ActionResult<T>` returns; closures include all reachable complex types; route/query-bound primitives excluded.

**Incremental pipeline shape (load-bearing — there is no attribute to hang `ForAttributeWithMetadataName` on; the base class is the key, and a naive syntax-provider-plus-semantic-walk re-runs the full closure walk on every keystroke in the host):**
- Syntax predicate: class declarations **with a non-empty base list** — cheap, no semantics.
- Transform: semantic confirmation against the `Norse.Abstractions.Web.Server.Facade.GrpcControllerBase` metadata name; non-matches return null and are filtered before anything expensive runs.
- **`ShapeModel` is a fully equatable, symbol-free value model** — records and value-equatable arrays (`EquatableArray<T>`-style) only; no `ISymbol`, no `Compilation`, no `SyntaxNode` captured anywhere in it. The closure walk produces `ShapeModel` in the transform; the emission stage keys purely on model equality, so an edit that doesn't change the exposed surface hits cache and emits nothing.

- [ ] **Step 1: Write failing diagnostics tests** — one per diagnostic, using the harness: a stub `GrpcControllerBase` + a controller exposing a contract violating exactly one law; assert the diagnostic ID and that the squiggle lands on the offending symbol. Include the negative: the same violating contract with **no** controller touching it produces zero diagnostics (exposure scoping — spec §15). Include the **cached-vs-recomputed test**: run the driver, edit an unrelated syntax tree, run again, and assert via `GeneratorDriverRunResult` tracked-step reasons that the shape pipeline steps report `Cached`/`Unchanged` — incrementality proven, not presumed.
- [ ] **Step 2: Run to verify fail.**
- [ ] **Step 3: Implement discovery + walk + diagnostics** (no emission yet — generator emits nothing when diagnostics fire, and emission itself lands in Task 6). `NameCasing.Apply`: split on Pascal word boundaries; camel/pascal join, snake lower-joins with `_`, upper/lower flatten. Unit-test `NameCasing` directly in the same test project (`"ReadWrite"` → `read_write`/`READWRITE`/`readwrite`/`readWrite`/`ReadWrite`).
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit**: `feat(xml-gen): facade closure discovery + shape-law diagnostics NORSE022-028`

---

### Task 6: Generator — writer emission

**Files:**
- Modify: `.../XmlShapeGenerator.cs` + new `.../WriterEmitter.cs`
- Test: `.../Xml.Generator.Tests/WriterEmissionTests.cs`

**Interfaces:**
- Consumes: `IXmlShape<T>`, `XmlLexical`, `XmlCaseStyle` (Task 2). Emitted classes: `internal sealed {Contract}XmlShape : IXmlShape<{Contract}>` in namespace `{HostRootNamespace}.NorseXmlShapes`, one file per contract, plus the five-style name tables as `static readonly string[]` fields indexed by `(int)style`.
- Produces: canonical writer per spec §6 — declaration-order attributes then child elements; null scalars omitted; `Result` members unwrap success / throw on failure-or-default (`"a failed Result<T> is illegal to write"`); enums via generated name tables, flags exact-match-then-greedy-descending, undefined → throw; collections as N type-named elements; recursion into complex members via their shape classes.

- [ ] **Step 1: Write failing emission tests** — compile a fixture contract set through the harness, instantiate the generated shape via the compilation, and assert output XML strings byte-exact, e.g. a request contract `QuoteRequest` (`Result<decimal> Limit`, `Result<DateOnly>? Effective`, `List<CoverageLine> …`) at `SnakeCase` produces exactly `<quote_request limit="1234.56"><coverage_line code="GL" /></quote_request>` (omitted optional; no declaration in fragment mode — root-level tests assert the declaration too). **Correction (Task 6, verified live against .NET's `XmlWriter`):** self-closing elements always render with a space before `/>` — this is unconfigurable `XmlWriter` behavior, not an emitter choice; every byte-exact assertion in this plan and its downstream tasks (9, 13) uses the space-included form. Add flags-canonical and failed-Result-throws tests.
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
- **The library-controller tripwire (spec §3, ratified 2026-08-02):** `AddNorseXml` also registers a startup validation (`IValidateOnStart`-backed options validator or `IStartupFilter` — pick whichever the platform's ServiceDefaults already idiomatically use; read them first) that enumerates the app's `ControllerFeature` via `ApplicationPartManager`, and for every `GrpcControllerBase` descendant asserts each body-bound parameter type and `ActionResult<T>` payload type has a shape in the registry. Any miss → `InvalidOperationException` naming the controller, the type, and the law: `"facade controllers are host-compilation source — '{Controller}' exposes '{Type}' with no generated shape; controllers shipped in referenced assemblies generate nothing"`. Startup failure, never a runtime 500.

- [ ] **Step 1: Write failing tests** — registration emission (generated `Build()` contains every fixture shape; duplicate contract types impossible by construction), `AddNorseXml` wiring (`MvcOptions` contains both formatter instances; `NorseXmlOptions.CaseStyle` set), and the tripwire: a fixture `GrpcControllerBase` descendant whose action exposes a type absent from the registry fails startup validation with the named error (assert the message contains the controller and type names); the same controller with all shapes registered passes.
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

### Task 10: Asgard + Midgard — `GrpcControllerBase` (Asgard) + `Outcome<T>` fold + RFC 9457 problem writer (Midgard)

Two repos, one task, sequential: the Asgard half first (own `feature/futhark-xml` branch there), then the Midgard problem writer.

**Files:**
- Create: `Asgard/src/Abstractions.Web.Server/Facade/GrpcControllerBase.cs` (namespace `Norse.Abstractions.Web.Server.Facade`; the assembly already carries the server framework reference — read its csproj first and confirm, do not add blindly)
- Create: `Midgard/src/Infrastructure.Web.Server/Xml/ProblemXmlWriter.cs`
- Test: `Asgard/tests/Abstractions.Web.Server.Tests/Facade/GrpcControllerBaseTests.cs` (extend the existing test project; if absent, mirror a sibling Asgard test csproj — one test project per package), `Midgard/tests/Infrastructure.Web.Server.Tests/Xml/ProblemXmlWriterTests.cs`

**Interfaces:**
- Consumes: `Outcome<T>` (`Norse.Abstractions.Contracts`) — **read `Outcome{T}.cs` and the existing `OutcomeServerInterceptor` first** to fold the exact same states the gRPC edge folds; the two folds must agree state-for-state. The fold uses only `ControllerBase` natives (`Ok`/`NotFound`/`Problem`) — **no Midgard reference from Asgard, ever**; problem+xml rendering is the host-registered formatter's job.
- Produces:

```csharp
[ApiController]
[Consumes("application/json", "application/xml")]
[Produces("application/json", "application/xml")]
[RequestSizeLimit(1_048_576)]   // spec §8.4 — the cap travels with the facade, not host config; a formatter cannot enforce body size
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
- Produces: `ResultSchemaTransformer` — `Result<T>`/`Result<T>?` schemas become the underlying scalar schema via a **static closed-taxonomy mapping table** (BCL types cannot carry static abstract interface members; the table is the honest equivalent, one row per §7 type: schema type + format); nullable `Result<T>?` leaves `required`; request schemas `writeOnly`, response schemas `readOnly`. `XmlMetadataTransformer` — stamps scalar properties `NodeType = Attribute` (the resolved `Microsoft.OpenApi` 3.6.0 package's actual vocabulary — the classic `attribute`/`wrapped` boolean pair is internal+obsolete in this version; see the spec's §12 correction), item element names from item types; no `wrapped` signal needed — arrays default to unwrapped in this vocabulary. `UnionLeakGuardTransformer` — document transformer that **fails loudly** (throws) if any schema references `Outcome` or `Result` by name post-transform; the symmetry law's tripwire.

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
- Modify: `Yggdrasil/src/Hosting.Web.Server/Program.cs` — `builder.Services.AddControllers().AddNorseJson().AddNorseXml(XmlCaseStyle.CamelCase, NorseXmlShapeRegistration.Build());` plus **the OpenAPI wiring** — `builder.Services.AddOpenApi(options => { options.AddSchemaTransformer<ResultSchemaTransformer>(); options.AddSchemaTransformer<XmlMetadataTransformer>(); options.AddDocumentTransformer<UnionLeakGuardTransformer>(); });` — plus `app.MapControllers();` and `app.MapOpenApi();` placed with the existing endpoint mappings. Designed-and-tested-but-unwired is the `OutcomeServerInterceptor` sin; this line is where the plan refuses to repeat it while implementing the law that names it.
- Create: `Yggdrasil/tests/Hosting.Web.Server.Tests/` if absent (csproj mirrors an existing Yggdrasil test project — check `Yggdrasil/tests/` first; if the folder has no test projects, copy a Midgard test csproj shape and adjust the `NorseRef`/project references).
- Create in the test (host-compilation) tree: `Parity/ParityContracts.cs` (`[DataContract] ParityRequest` — `Result<T>`-wrapped members covering every §7 scalar row + `List<ParityTag>` where `ParityTag` wraps a `Result<string> Value`; `[DataContract] ParityReport` — plain scalars echoing every value), `Parity/IParityService.cs` + `ParityService.cs` (`[ServiceContract]`, one `ValueTask<Outcome<ParityReport>> EchoAsync(ParityRequest)` through the real mediator pipeline), `Parity/ParityController.cs` (`: GrpcControllerBase`, one POST action).
- Create: `Swoop/TriProtocolSwoopTests.cs`, `Swoop/LexicalCorpus.cs`, `Swoop/WiringTests.cs`.

**Interfaces:** consumes everything above; produces the spec §15 suite:

- [ ] **Step 1: Write the failing swoop tests first** (they fail on wiring, then on behavior, in that order — that's the point):
  - Success parity: one `ParityRequest` via in-proc gRPC client (protobuf-net.Grpc client over `WebApplicationFactory` handler), REST-JSON POST, REST-XML POST → three `ParityReport`s, structurally equal.
  - Failure parity: three malformed scalars → JSON and XML responses carry identical `errors` arrays (paths, details, shape) as problem payloads; gRPC required-absent → the pipeline's validation failure surfaces with detail wording asserted **equal to** the text channels' required-missing detail (`FailureDetail.Render` parity — the Task 4 condition proven end-to-end, not merely "a validation failure surfaced").
  - Lexical corpus: shared accepted/rejected lexeme sets per §7 row asserted identical across both text channels (non-finite spellings in the rejected set).
  - Round-trip spine including required `Result<string>` = `""`.
  - Body cap: an oversized (> 1 MiB) XML body → **413**, asserted against the live host.
  - Wiring tests — spec §10.4 mandates all three remove-the-registration probes, all asserted by hitting the live test host, never by inspecting DI: the XML formatters answer negotiation (and the problem writer negotiates for `Accept: application/problem+xml`); the OpenAPI document fetched from the running host renders the parity contracts **unwrapped** — a `Result<DateOnly>` member appears as `string`/`date`, and the strings `Outcome` and `Result` appear **nowhere** in the document (the fold + both union-unwrap transformers, proven wired).
- [ ] **Step 2: Run to verify fail** (formatters not yet wired in host).
- [ ] **Step 3: Wire `Program.cs`; implement fixture service/controller.**
- [ ] **Step 4: Run to verify pass** — full Yggdrasil suite; also `dotnet build` the host and confirm the generator emitted shapes for exactly the parity contracts (the exposure law working in the real host).
- [ ] **Step 5: Commit** on Yggdrasil `feature/futhark-xml`: `feat: XML negotiation live — tri-protocol swoop green`

---

## Self-review notes (run before handoff)

- Spec coverage: §2→T5–8; §3→T1–8; §4→T5,T10,T13; §5→T5; §6→T2,T6; §7→T0,T2,T3,T13; §8→T1,T7,T9; §9→T3,T4,T6–7; §10→T10,T11,T13; §11→T1,T10; §12→T11; §13 (versioning) is documentation-only — no task, deliberate; §14→T5; §15→every task's tests + T13; §16–17 resolved in-plan (generator name, NORSE block, closed-table schema metadata, absent-member semantics at validation layer).
- Known deviations from spec text, both flagged inline and **both ratified at plan review (2026-08-02)**: OpenAPI schema metadata uses a closed static table instead of static abstract interface members (BCL types cannot implement interfaces — spec intent preserved, mechanism honest); gRPC required-absent semantics live in the shared `ResultRules` validation extensions rather than the deserializer (protobuf-net absent-member reality — observable behavior identical, **message wording byte-identical by construction**: the rule calls `Parser.ParseRequired<T>(string.Empty, …)` and renders via `FailureDetail.Render`, and the swoop asserts the parity end-to-end).
- Plan-review findings folded (2026-08-02): OpenAPI transformers wired into the live host with document-fetch wiring tests (all three §10.4 probes now present); Task 5 pins the incremental pipeline shape (base-list syntax predicate, metadata-name confirmation, symbol-free equatable `ShapeModel`) with a cached-vs-recomputed test; the 1 MiB cap enforces via `[RequestSizeLimit]` on `GrpcControllerBase` with a live-host 413 test.

## Postmortem gap closure (2026-08-02, same-day follow-up)

The Task 13 handoff named five deliberately-deferred gaps. All five verified real against the shipped code; four closed in the same pass (Midgard + Yggdrasil, `feature/futhark-postmortem-gaps`), the fifth reduced to one named open design:

1. **`Result<TEnum>` gRPC wire law — closed.** `ResultEnumSerializer<TEnum>` (Midgard `Infrastructure.Web.Grpc`) reads the native varint; undefined values — and flags leftover bits — funnel to the typed `Failure`, mirroring the text channels' undefined-enum-name accumulable; `Write` throws the shared deserialization-only message. Enums are user-declared (open set), so registration is discovery-driven: `ResultSerializers.Register` hooks `AfterApplyDefaultBehaviour` and registers `Result<TEnum>`/`Result<TEnum>?` members on first sight, same must-run-first contract `IdentifierSerializers` documents.
2. **Bare `DateTimeOffset` gRPC wire law — closed.** `DateTimeOffsetSerializer` (production, registered by `ResultSerializers.Register`) writes/reads the §7 "O" wire string — the same form `ResultSerializer<DateTimeOffset>.Read` consumes, pinned byte-level in `ResultSerializerTests`. The swoop fixture's test-local stopgap serializer is deleted.
3. **Lexical corpus 7 → 12 of 13 §7 rows.** Added char, string (verbatim + present-empty; no rejectable lexeme exists for the row, stated in-corpus), `DateTime`, `DateTimeOffset`, `TimeOnly`. The enum row remains response-side only — blocked on gap 5's remainder, not forgotten.
4. **`[ApiController]` implicit-required double-fire — closed at the law level.** `AddNorseXml` now sets `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes`: required-ness on Futhark contracts is `Result<T>` presence + `ResultRules`, never MVC DataAnnotations. The swoop's `e.Path != "request"` filter is deleted — the failure-parity assertions run unfiltered and stay symmetric.
5. **Registration-order dependence — closed; one remainder promoted to a named open design.** The order-dependent `if (!IsDefined)` test-local `DateTimeOffset` registration is gone (production-owned, unconditional, loud on conflict). The remainder: **`Result<TEnum>` has no JSON request funnel** — `ResultJsonConverter<T>` is `ISpanParsable<T>`-constrained, and §7's case-styled enum name tables live in the generated XML shapes with no JSON-side equivalent; `ResultJsonConverterFactory` now refuses `Result<TEnum>` with a named `NotSupportedException` instead of a bare `MakeGenericType` constraint violation. Designing the JSON channel's enum name mechanism (generator-emitted vs. runtime table vs. something else) is a Forseti question, not a patch. **Ruled same day:** `../specs/2026-08-02-futhark-enum-wire-law-design.md` — generated tables + one `EnumLexical` mechanism, flags banned from the facade closure.
