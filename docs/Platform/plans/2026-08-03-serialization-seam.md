# The Serialization Seam — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Pairs with superpowers:test-driven-development on every task.

**Goal:** Ship the format-agnostic serialization seam — `ISerializer`/`NamingStrategy`/`ISerializerProvider` in Asgard's `Abstractions.Backend`, the System.Text.Json machinery in a new Midgard `Infrastructure.Serialization` project, composition at Yggdrasil's root — and restore Himinbjörg's personal-data download on it, lawfully.

**Architecture:** Spec: `../specs/2026-08-03-serialization-seam-design.md` — read it before any task; every ruling is settled (format-agnostic contract with JSON default via `ContentType`; `Abstractions.Backend` placement is a permanently closed door client-side; egress machinery out of scope). The contract surface is pure BCL — no STJ type appears outside Midgard; NORSE070 is the enforcement.

**Tech Stack:** .NET 11 preview / C# 15, System.Text.Json (Midgard only), xUnit v3 + Shouldly on MTP.

## Global Constraints

- Read `../../house-rules.md` in full before implementing any task (tabs, `sealed`, target-typed `new()`, collection expressions, expression bodies, hoisted usings, XML docs in src, `ConfigureAwait(false)` in src, DIM formatting, fluent DI chains).
- **Branching:** Asgard `feature/serialization-seam`; Midgard `feature/serialization-seam`; Yggdrasil `feature/serialization-composition` off `master`; Himinbjörg `feature/serialization-download-restore` off **`feature/wire-format-remediation`** (it restores atop the excision — if `master` already contains the excision at execution time, branch off `master` instead and note it). Commits local and unpushed; implementers commit their own work on these branches; Buvy pushes/PRs at ship gates. Never branch or commit Bifröst.
- **Commit policy:** subagents commit only files they authored, named explicitly — never `git add -A`.
- **Hands-off files:** every `Directory.Build.props`/`Directory.Build.targets` and `config/*` in every realm — Ginnungagap scatter; halt and ask if a change seems needed there.
- **Namespace = folder, always** (ruled 2026-08-03; IDE0130 is an error, never suppressed): contracts live in `src/Abstractions.Backend/Serialization/` → `namespace Norse.Abstractions.Backend.Serialization;`.
- **Enum law:** `Unspecified = 0` sentinel, explicit values on every member.
- **Prior art is not cited by path in code or docs** — the shapes below are complete; no external repo access is needed or permitted.
- **Transitive-first:** add a `<NorseRef>`/`<PackageReference>` only after verifying the dependency does not already flow; note the check in the report.
- Test naming sentence-shaped; test classes `public sealed`; methods bare; Shouldly/global usings — never per-file.
- MTP filter syntax only (`dotnet test tests/X -- --filter-class "*.YTests"`); never `dotnet test` a zero-test project.
- Touched realms' suites green before each commit; builds run under the law (workspace mode attaches the analyzer — a NORSE07x is a build error, and in Midgard's new project STJ is legal by jurisdiction).

## File Structure

```
Asgard/
  src/Abstractions.Backend/Serialization/ISerializer.cs          (new)
  src/Abstractions.Backend/Serialization/NamingStrategy.cs       (new)
  src/Abstractions.Backend/Serialization/ISerializerProvider.cs  (new)
  tests/Abstractions.Backend.Tests/Serialization/SerializerContractTests.cs (new)
  CLAUDE.md / README.md                                          (modify — Backend gains the seam)
Midgard/
  src/Infrastructure.Serialization/Infrastructure.Serialization.csproj  (new project)
  src/Infrastructure.Serialization/SystemTextJsonSerializer.cs   (new)
  src/Infrastructure.Serialization/SerializerProvider.cs         (new)
  src/Infrastructure.Serialization/ServiceCollectionExtensions.cs (new)
  tests/Infrastructure.Serialization.Tests/                      (new project)
  Midgard.slnx                                                   (modify)
  CLAUDE.md / README.md                                          (modify)
Yggdrasil/
  src/Hosting.Web.Server/Program.cs                              (modify — .AddNorseSerialization())
  Directory.Packages.props                                       (modify — pin Norse.Infrastructure.Serialization)
Himinbjorg/
  src/Identity.Web.Server/IdentityComponentsEndpointRouteBuilderExtensions.cs (modify — restore on the seam)
  src/Identity.Web.Server/Components/Pages/Manage/PersonalData.razor          (modify — restore the form)
  src/Identity.Web.Server/Identity.Web.Server.csproj             (modify only if Abstractions.Backend doesn't flow transitively)
```

---

## Phase A — Asgard (`feature/serialization-seam`)

### Task 1: The contracts

**Files:**
- Create: `src/Abstractions.Backend/Serialization/ISerializer.cs`, `.../NamingStrategy.cs`, `.../ISerializerProvider.cs`
- Test: `tests/Abstractions.Backend.Tests/Serialization/SerializerContractTests.cs`
- Modify: `CLAUDE.md`/`README.md` (one line each: Backend now carries the serialization seam; match existing doc voice)

**Interfaces (Produces):** exactly the three types below — Tasks 2–4 consume these signatures verbatim.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace Norse.Abstractions.Backend.Tests.Serialization;

public sealed class SerializerContractTests
{
	// DIM-default probe: the contract's defaults are law (spec §1) — a format that overrides
	// neither is JSON-shaped and async-capable by declaration.
	sealed class BareSerializer : ISerializer
	{
		public T? Deserialize<T>(byte[] bytes) => default;
		public T? Deserialize<T>(Stream stream) => default;
		public T? Deserialize<T>(string payload) => default;
		public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default) => default;
		public void Serialize<T>(Stream stream, T obj, bool serializeNulls = false) { }
		public string Serialize<T>(T obj, bool serializeNulls = false, bool prettyPrint = false) => "";
		public Task SerializeAsync<T>(Stream stream, T obj, bool serializeNulls = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
		public byte[] SerializeToUtf8Bytes<T>(T obj, bool serializeNulls = false) => [];
	}

	[Fact]
	void Content_type_defaults_to_json_and_async_defaults_to_supported()
	{
		ISerializer serializer = new BareSerializer();
		serializer.ContentType.ShouldBe("application/json");
		serializer.HasAsyncSupport.ShouldBeTrue();
	}

	[Theory]
	[InlineData(NamingStrategy.Unspecified, 0)]
	[InlineData(NamingStrategy.CamelCase, 1)]
	[InlineData(NamingStrategy.PascalCase, 2)]
	[InlineData(NamingStrategy.SnakeCase, 3)]
	[InlineData(NamingStrategy.KebabCase, 4)]
	void Naming_strategy_values_are_explicit_and_zero_is_the_sentinel(NamingStrategy strategy, int value) =>
		((int)strategy).ShouldBe(value);
}
```

(`using Norse.Abstractions.Backend.Serialization;` hoisted — sibling namespace, not an ancestor.)

- [ ] **Step 2: Run to verify failure** — `dotnet test tests/Abstractions.Backend.Tests -- --filter-class "*.SerializerContractTests"`; expected: compile error, types missing.

- [ ] **Step 3: Implement**

`src/Abstractions.Backend/Serialization/NamingStrategy.cs`:

```csharp
namespace Norse.Abstractions.Backend.Serialization;

/// <summary>
/// The property-naming convention an <see cref="ISerializer"/> applies when writing and reading
/// payloads. Conventions are format-agnostic — a JSON serializer maps them to its naming policies;
/// any other format maps them however that format spells casing.
/// </summary>
public enum NamingStrategy
{
	/// <summary>Sentinel CLR default — never a valid strategy; a caller always names its convention.</summary>
	Unspecified = 0,
	/// <summary>Property names are written in camelCase (e.g. <c>myProperty</c>).</summary>
	CamelCase = 1,
	/// <summary>Property names are written in PascalCase (e.g. <c>MyProperty</c>).</summary>
	PascalCase = 2,
	/// <summary>Property names are written in snake_case (e.g. <c>my_property</c>).</summary>
	SnakeCase = 3,
	/// <summary>Property names are written in kebab-case (e.g. <c>my-property</c>).</summary>
	KebabCase = 4
}
```

`src/Abstractions.Backend/Serialization/ISerializer.cs`:

```csharp
using System.Net.Mime;

namespace Norse.Abstractions.Backend.Serialization;

/// <summary>
/// Format-agnostic payload serialization: objects to and from bytes, streams, and strings. The
/// surface is deliberately pure BCL — no serializer machinery type ever crosses it, so realms
/// declare intent here while the encoding executes behind the wire border (NORSE070). The default
/// case is JSON, said by <see cref="ContentType"/>'s default — but nothing constrains an
/// implementation to it: any format that can honor this surface registers through DI and drops in.
/// </summary>
public interface ISerializer
{
	/// <summary>The MIME content type this serializer produces and consumes. Defaults to <c>application/json</c>.</summary>
	string ContentType =>
		MediaTypeNames.Application.Json;

	/// <summary>Whether <see cref="DeserializeAsync{T}"/> is genuinely asynchronous. Defaults to <see langword="true"/>.</summary>
	bool HasAsyncSupport =>
		true;

	/// <summary>Deserializes a <typeparamref name="T"/> from a raw byte payload.</summary>
	T? Deserialize<T>(byte[] bytes);

	/// <summary>Deserializes a <typeparamref name="T"/> from a stream.</summary>
	T? Deserialize<T>(Stream stream);

	/// <summary>Deserializes a <typeparamref name="T"/> from a string payload.</summary>
	T? Deserialize<T>(string payload);

	/// <summary>Asynchronously deserializes a <typeparamref name="T"/> from a stream.</summary>
	ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default);

	/// <summary>Serializes <paramref name="obj"/> to <paramref name="stream"/>.</summary>
	/// <param name="stream">The destination stream.</param>
	/// <param name="obj">The object to serialize.</param>
	/// <param name="serializeNulls">When <see langword="true"/>, null properties are written.</param>
	void Serialize<T>(Stream stream, T obj, bool serializeNulls = false);

	/// <summary>Serializes <paramref name="obj"/> to a string.</summary>
	/// <param name="obj">The object to serialize.</param>
	/// <param name="serializeNulls">When <see langword="true"/>, null properties are written.</param>
	/// <param name="prettyPrint">When <see langword="true"/>, the output is human-formatted.</param>
	string Serialize<T>(T obj, bool serializeNulls = false, bool prettyPrint = false);

	/// <summary>Asynchronously serializes <paramref name="obj"/> to <paramref name="stream"/>.</summary>
	Task SerializeAsync<T>(Stream stream, T obj, bool serializeNulls = false, CancellationToken cancellationToken = default);

	/// <summary>Serializes <paramref name="obj"/> directly to UTF-8 bytes.</summary>
	byte[] SerializeToUtf8Bytes<T>(T obj, bool serializeNulls = false);
}
```

`src/Abstractions.Backend/Serialization/ISerializerProvider.cs`:

```csharp
namespace Norse.Abstractions.Backend.Serialization;

/// <summary>
/// Hands out the registered default-format <see cref="ISerializer"/> for a naming convention.
/// A future format joins by its own DI registration and a composition-root choice — never by
/// widening this contract. <see cref="NamingStrategy.Unspecified"/> is the smuggled sentinel:
/// implementations throw on it.
/// </summary>
public interface ISerializerProvider
{
	/// <summary>Gets the serializer configured for <paramref name="key"/>.</summary>
	ISerializer this[NamingStrategy key] { get; }
}
```

- [ ] **Step 4: Run tests to verify they pass** — same filter, then full `dotnet test tests/Abstractions.Backend.Tests`.

- [ ] **Step 5: Docs + commit**

```bash
git checkout -b feature/serialization-seam
git add src/Abstractions.Backend/Serialization/ISerializer.cs src/Abstractions.Backend/Serialization/NamingStrategy.cs src/Abstractions.Backend/Serialization/ISerializerProvider.cs tests/Abstractions.Backend.Tests/Serialization/SerializerContractTests.cs CLAUDE.md README.md
git commit -m "feat: the serialization seam — ISerializer, NamingStrategy, ISerializerProvider"
```

**SHIP GATE (human): Asgard** — PR, CI, tag, publish.

---

## Phase B — Midgard (`feature/serialization-seam`)

### Task 2: The machinery

**Files:**
- Create: `src/Infrastructure.Serialization/Infrastructure.Serialization.csproj`, `SystemTextJsonSerializer.cs`, `SerializerProvider.cs`, `ServiceCollectionExtensions.cs`
- Create: `tests/Infrastructure.Serialization.Tests/Infrastructure.Serialization.Tests.csproj`, `SystemTextJsonSerializerTests.cs`, `SerializerProviderTests.cs`
- Modify: `Midgard.slnx` (`dotnet sln Midgard.slnx add src/Infrastructure.Serialization tests/Infrastructure.Serialization.Tests`), `CLAUDE.md`/`README.md` (new project, one line each)

**Interfaces:**
- Consumes: Task 1's three contracts via `<NorseRef Include="Abstractions.Backend" />`.
- Produces: `AddNorseSerialization(this IServiceCollection)` — the only public member; both implementation classes internal sealed.

csproj (match a sibling like an existing small src project's shape; one PropertyGroup/ItemGroup, alphabetical):

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Infrastructure.Serialization: the System.Text.Json machinery behind Asgard's format-agnostic serialization seam (ISerializer/ISerializerProvider, naming-strategy keyed). Realms inject the contracts from Norse.Abstractions.Backend; the encoding lives here, inside the wire border, per NORSE070.</Description>
	</PropertyGroup>
	<ItemGroup>
		<NorseRef Include="Abstractions.Backend">
			<Repo>Asgard</Repo>
		</NorseRef>
	</ItemGroup>
</Project>
```

(Verify the `<NorseRef>` metadata shape against an existing Midgard csproj that NorseRefs Asgard and mirror it exactly. `Microsoft.Extensions.DependencyInjection.Abstractions`: transitive-first — add a direct `Version="11.*-*"` reference only if the build proves it doesn't flow.)

- [ ] **Step 1: Write the failing tests**

```csharp
namespace Norse.Infrastructure.Serialization.Tests;

public sealed class SystemTextJsonSerializerTests
{
	sealed record Payload
	{
		public required string FirstName { get; init; }
		public string? MiddleName { get; init; }
		public required int Age { get; init; }
	}

	static readonly ISerializerProvider _provider = BuildProvider();

	static ISerializerProvider BuildProvider()
	{
		ServiceCollection services = new();
		services.AddNorseSerialization();
		return services.BuildServiceProvider().GetRequiredService<ISerializerProvider>();
	}

	[Theory]
	[InlineData(NamingStrategy.CamelCase, "firstName")]
	[InlineData(NamingStrategy.PascalCase, "FirstName")]
	[InlineData(NamingStrategy.SnakeCase, "first_name")]
	[InlineData(NamingStrategy.KebabCase, "first-name")]
	void Serializes_property_names_per_strategy(NamingStrategy strategy, string expectedName)
	{
		var json = _provider[strategy].Serialize(new Payload { FirstName = "Buvy", Age = 40 });
		json.ShouldContain($"\"{expectedName}\"");
	}

	[Theory]
	[InlineData(NamingStrategy.CamelCase)]
	[InlineData(NamingStrategy.PascalCase)]
	[InlineData(NamingStrategy.SnakeCase)]
	[InlineData(NamingStrategy.KebabCase)]
	void Round_trips_through_string_bytes_and_stream_per_strategy(NamingStrategy strategy)
	{
		var serializer = _provider[strategy];
		Payload original = new() { FirstName = "Buvy", MiddleName = "B", Age = 40 };

		serializer.Deserialize<Payload>(serializer.Serialize(original)).ShouldBe(original);
		serializer.Deserialize<Payload>(serializer.SerializeToUtf8Bytes(original)).ShouldBe(original);

		using MemoryStream stream = new();
		serializer.Serialize(stream, original);
		stream.Position = 0;
		serializer.Deserialize<Payload>(stream).ShouldBe(original);
	}

	[Fact]
	async Task Async_round_trip_works_and_the_contract_defaults_hold()
	{
		var serializer = _provider[NamingStrategy.CamelCase];
		Payload original = new() { FirstName = "Buvy", Age = 40 };

		using MemoryStream stream = new();
		await serializer.SerializeAsync(stream, original, cancellationToken: TestContext.Current.CancellationToken);
		stream.Position = 0;
		(await serializer.DeserializeAsync<Payload>(stream, TestContext.Current.CancellationToken)).ShouldBe(original);

		serializer.ContentType.ShouldBe("application/json");
		serializer.HasAsyncSupport.ShouldBeTrue();
	}

	[Fact]
	void Omits_nulls_by_default_and_writes_them_on_request()
	{
		var serializer = _provider[NamingStrategy.CamelCase];
		Payload payload = new() { FirstName = "Buvy", Age = 40 };
		serializer.Serialize(payload).ShouldNotContain("middleName");
		serializer.Serialize(payload, serializeNulls: true).ShouldContain("\"middleName\":null");
	}

	[Fact]
	void Pretty_print_indents_and_default_is_compact()
	{
		var serializer = _provider[NamingStrategy.CamelCase];
		Payload payload = new() { FirstName = "Buvy", Age = 40 };
		serializer.Serialize(payload).ShouldNotContain("\n");
		serializer.Serialize(payload, prettyPrint: true).ShouldContain("\n");
	}

	[Fact]
	void Dictionary_keys_are_data_and_pass_through_unrewritten()
	{
		// The seam serializes shapes, not data: property names follow the strategy, dictionary
		// KEYS are values and are never case-rewritten (the personal-data download depends on it).
		var json = _provider[NamingStrategy.CamelCase]
			.Serialize(new Dictionary<string, string> { ["Authenticator Key"] = "x" });
		json.ShouldContain("\"Authenticator Key\"");
	}
}

public sealed class SerializerProviderTests
{
	[Fact]
	void Provider_caches_one_serializer_per_strategy()
	{
		ServiceCollection services = new();
		services.AddNorseSerialization();
		var provider = services.BuildServiceProvider().GetRequiredService<ISerializerProvider>();
		provider[NamingStrategy.CamelCase].ShouldBeSameAs(provider[NamingStrategy.CamelCase]);
		provider[NamingStrategy.SnakeCase].ShouldNotBeSameAs(provider[NamingStrategy.CamelCase]);
	}

	[Fact]
	void Unspecified_is_the_smuggled_sentinel_and_throws()
	{
		ServiceCollection services = new();
		services.AddNorseSerialization();
		var provider = services.BuildServiceProvider().GetRequiredService<ISerializerProvider>();
		Should.Throw<ArgumentOutOfRangeException>(() => provider[NamingStrategy.Unspecified]);
	}
}
```

(Hoisted: `using Microsoft.Extensions.DependencyInjection;` + `using Norse.Abstractions.Backend.Serialization;`. Test csproj mirrors a sibling Midgard test project + `<ProjectReference>` to the new src project.)

- [ ] **Step 2: Run to verify failure** (create both csproj + slnx wiring first so the failure is missing types).

- [ ] **Step 3: Implement**

`SystemTextJsonSerializer.cs`:

```csharp
using System.Text.Json;
using Norse.Abstractions.Backend.Serialization;

namespace Norse.Infrastructure.Serialization;

/// <summary>
/// The JSON arm of the seam: one instance per <see cref="NamingStrategy"/>, four cached
/// <see cref="JsonSerializerOptions"/> variants (nulls × pretty) — options are never minted per
/// call. Property names follow the strategy; dictionary keys are data and pass through unrewritten.
/// </summary>
sealed class SystemTextJsonSerializer : ISerializer
{
	readonly JsonSerializerOptions
		_compact,
		_compactWithNulls,
		_pretty,
		_prettyWithNulls;

	public SystemTextJsonSerializer(NamingStrategy strategy)
	{
		var policy = strategy switch
		{
			NamingStrategy.CamelCase => JsonNamingPolicy.CamelCase,
			NamingStrategy.PascalCase => null,
			NamingStrategy.SnakeCase => JsonNamingPolicy.SnakeCaseLower,
			NamingStrategy.KebabCase => JsonNamingPolicy.KebabCaseLower,
			_ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "A serializer always names its convention.")
		};
		_compact = Build(policy, serializeNulls: false, prettyPrint: false);
		_compactWithNulls = Build(policy, serializeNulls: true, prettyPrint: false);
		_pretty = Build(policy, serializeNulls: false, prettyPrint: true);
		_prettyWithNulls = Build(policy, serializeNulls: true, prettyPrint: true);
	}

	public T? Deserialize<T>(byte[] bytes) =>
		JsonSerializer.Deserialize<T>(bytes, _compact);

	public T? Deserialize<T>(Stream stream) =>
		JsonSerializer.Deserialize<T>(stream, _compact);

	public T? Deserialize<T>(string payload) =>
		JsonSerializer.Deserialize<T>(payload, _compact);

	public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default) =>
		JsonSerializer.DeserializeAsync<T>(stream, _compact, cancellationToken);

	public void Serialize<T>(Stream stream, T obj, bool serializeNulls = false) =>
		JsonSerializer.Serialize(stream, obj, Options(serializeNulls, prettyPrint: false));

	public string Serialize<T>(T obj, bool serializeNulls = false, bool prettyPrint = false) =>
		JsonSerializer.Serialize(obj, Options(serializeNulls, prettyPrint));

	public Task SerializeAsync<T>(Stream stream, T obj, bool serializeNulls = false, CancellationToken cancellationToken = default) =>
		JsonSerializer.SerializeAsync(stream, obj, Options(serializeNulls, prettyPrint: false), cancellationToken);

	public byte[] SerializeToUtf8Bytes<T>(T obj, bool serializeNulls = false) =>
		JsonSerializer.SerializeToUtf8Bytes(obj, Options(serializeNulls, prettyPrint: false));

	JsonSerializerOptions Options(bool serializeNulls, bool prettyPrint) =>
		serializeNulls ?
			prettyPrint ?
				_prettyWithNulls :
				_compactWithNulls :
			prettyPrint ?
				_pretty :
				_compact;

	static JsonSerializerOptions Build(JsonNamingPolicy? policy, bool serializeNulls, bool prettyPrint) =>
		new()
		{
			PropertyNamingPolicy = policy,
			DefaultIgnoreCondition = serializeNulls ?
				System.Text.Json.Serialization.JsonIgnoreCondition.Never :
				System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
			WriteIndented = prettyPrint
		};
}
```

(House-rules note for the implementer: hoist `using System.Text.Json.Serialization;` and drop the qualification in `Build` — shown qualified here only to make the symbol's origin explicit in the plan.)

`SerializerProvider.cs`:

```csharp
using System.Collections.Concurrent;
using Norse.Abstractions.Backend.Serialization;

namespace Norse.Infrastructure.Serialization;

/// <summary>Lazy-mints and caches one <see cref="SystemTextJsonSerializer"/> per strategy.</summary>
sealed class SerializerProvider : ISerializerProvider
{
	readonly ConcurrentDictionary<NamingStrategy, ISerializer> _serializers = new();

	public ISerializer this[NamingStrategy key] =>
		_serializers.GetOrAdd(key, static k => new SystemTextJsonSerializer(k));
}
```

`ServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Backend.Serialization;

namespace Norse.Infrastructure.Serialization;

/// <summary>Composition-root wiring for the serialization seam.</summary>
public static class ServiceCollectionExtensions
{
	/// <summary>Registers the JSON-backed <see cref="ISerializerProvider"/> as a singleton.</summary>
	public static IServiceCollection AddNorseSerialization(this IServiceCollection services) =>
		services.AddSingleton<ISerializerProvider, SerializerProvider>();
}
```

- [ ] **Step 4: Run tests** — full new test project, then `dotnet build Midgard.slnx && dotnet test Midgard.slnx` zero warnings.

- [ ] **Step 5: Docs + commit**

```bash
git checkout -b feature/serialization-seam
git add src/Infrastructure.Serialization tests/Infrastructure.Serialization.Tests Midgard.slnx CLAUDE.md README.md
git commit -m "feat: Infrastructure.Serialization — the STJ machinery behind the seam"
```

**SHIP GATE (human): Midgard** — PR, CI, tag, publish `Norse.Infrastructure.Serialization`.

---

## Phase C — Yggdrasil (`feature/serialization-composition`)

### Task 3: Compose at the tree

**Files:**
- Modify: `src/Hosting.Web.Server/Program.cs` — add `.AddNorseSerialization()` to the existing fluent chain, adjacent to the other Midgard registrations (`.AddNorsePipeline()`/`.AddNorseCodeFirstGrpc()` block), with a matching trailing comment in the file's existing style; add the `<NorseRef Include="Infrastructure.Serialization"><Repo>Midgard</Repo></NorseRef>` to `Hosting.Web.Server.csproj` mirroring its existing NorseRef entries.
- Modify: `Directory.Packages.props` — `PackageVersion` for `Norse.Infrastructure.Serialization` following the file's existing version-variable convention (Task 8 precedent: `$(MidgardVersion)`-style if that is what siblings use — read the file, mirror it).

- [ ] **Step 1: Wire, then verify** — `dotnet build Yggdrasil/src/Hosting.Web.Server` from Bifröst (law attached) → green; from the realm root `dotnet test Yggdrasil.slnx` → green modulo any pre-existing known failures (name them from the output if hit; do not fix unrelated breakage).
- [ ] **Step 2: Commit**

```bash
git checkout master && git checkout -b feature/serialization-composition
git add src/Hosting.Web.Server/Program.cs src/Hosting.Web.Server/Hosting.Web.Server.csproj Directory.Packages.props
git commit -m "feat: compose the serialization seam at the tree"
```

**SHIP GATE (human): Yggdrasil** — PR, CI (after Midgard's package is live for CI mode).

---

## Phase D — Himinbjörg (`feature/serialization-download-restore`)

### Task 4: The download returns, lawfully

**Files:**
- Modify: `src/Identity.Web.Server/IdentityComponentsEndpointRouteBuilderExtensions.cs`
- Modify: `src/Identity.Web.Server/Components/Pages/Manage/PersonalData.razor`
- Modify: `src/Identity.Web.Server/Identity.Web.Server.csproj` — only if `Abstractions.Backend` does not flow transitively (check `dotnet list src/Identity.Web.Server package --include-transitive | grep Backend` or the workspace build error; if needed, add `<NorseRef Include="Abstractions.Backend"><Repo>Asgard</Repo></NorseRef>` mirroring the file's entries).

**Branch:** off `feature/wire-format-remediation` (contains the excision); if `master` already contains the excision, off `master` — say which in the report.

- [ ] **Step 1: Restore the endpoint on the seam.** Reinstate, in the same position the excision removed them (git history is the reference: `git show 2b60c26:src/Identity.Web.Server/IdentityComponentsEndpointRouteBuilderExtensions.cs`): the `loggerFactory`/`downloadLogger` locals, the `MapPost("/DownloadPersonalData", ...)` block, and the `LogUserPersonalDataRequested` `[LoggerMessage]` method — with exactly these deltas from the original:
  - `using Norse.Abstractions.Backend.Serialization;` hoisted; **no** `using System.Text.Json;`.
  - The unused `[FromServices] AuthenticationStateProvider authenticationStateProvider` parameter from the original does NOT return (it was dead; its using stays gone).
  - The lambda takes `[FromServices] ISerializerProvider serializerProvider`; the serialization lines become:

```csharp
				var serializer = serializerProvider[NamingStrategy.CamelCase];
				var fileBytes = serializer.SerializeToUtf8Bytes(personalData);

				context.Response.Headers.TryAdd("Content-Disposition", "attachment; filename=PersonalData.json");
				return TypedResults.File(fileBytes, contentType: serializer.ContentType, fileDownloadName: "PersonalData.json");
```

- [ ] **Step 2: Restore the page.** From the same git reference for `PersonalData.razor`: reinstate the download form and the original "download or delete" lead wording; remove the interim static notice added by the excision.
- [ ] **Step 3: Verify under the law** — from Bifröst: `dotnet build Himinbjorg/src/Identity.Web.Server` → green, zero NORSE07x; from the realm root: `dotnet test Himinbjorg.slnx` → green (71+ tests). Paste output.
- [ ] **Step 4: Commit**

```bash
git checkout feature/wire-format-remediation && git checkout -b feature/serialization-download-restore
git add src/Identity.Web.Server/IdentityComponentsEndpointRouteBuilderExtensions.cs src/Identity.Web.Server/Components/Pages/Manage/PersonalData.razor
git commit -m "feat: restore personal-data download on the serialization seam — NORSE070 clean"
```

(Include the csproj in the `git add` only if Step 1's transitive check required the NorseRef.)

**SHIP GATE (human): Himinbjörg** — merges after `feature/wire-format-remediation` in the train.

---

## Self-Review Notes (performed at authoring)

1. **Spec coverage:** §1 contracts → Task 1 (verbatim shapes, DIM defaults, enum sentinel); §2 machinery → Task 2 (placement resolved: new `Infrastructure.Serialization` — recon confirmed no existing Midgard backend-shared project; cached options; provider caching; `AddNorseSerialization`); §3 restoration → Task 4 (exact deltas from the excised original, dead parameter not resurrected); §4 exclusions honored (no HttpClient egress, no client-side anything, no extra formats); §5 verification → per-task law builds + suites.
2. **Type consistency:** `ISerializer`/`ISerializerProvider`/`NamingStrategy` identical across Tasks 1/2/4; `AddNorseSerialization` identical across Tasks 2/3.
3. **Known judgment encodings:** dictionary-keys-pass-through pinned by test (Task 2) because the download's data keys must not be case-rewritten; `Deserialize` uses the compact options (naming policy governs reads identically regardless of null/indent variants).
