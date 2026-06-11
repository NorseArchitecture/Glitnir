# aichatweb POC Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Execute the three staged swaps in `poc/aichatweb/VoyageEmbeddings` — Ollama chat → Anthropic SDK, Ollama embeddings → new `Voyage.Extensions.AI` adapter, Qdrant → MongoDB atlas-local — per `docs/superpowers/specs/2026-06-07-aichatweb-poc-refactor-design.md`.

**Architecture:** All three swaps happen behind Microsoft.Extensions.AI / Microsoft.Extensions.VectorData abstractions already in the template. A new classlib `Voyage.Extensions.AI` (the OSS seed) implements `IEmbeddingGenerator<string, Embedding<float>>` over Voyage's REST API with constructor-pinned `input_type`. Three stage gates; the app runs after each.

**Tech Stack:** .NET 10, Aspire 13, Microsoft.Extensions.AI 10.6.0, `Anthropic` NuGet (beta), `Microsoft.SemanticKernel.Connectors.MongoDB` (preview), xUnit v3 + Shouldly on Microsoft.Testing.Platform.

---

## ⚠️ Process Rules (override anything below that conflicts)

1. **NO `git commit` — ever.** Glitnir law: stage with `git add` and report; the human commits. Every "commit" step in this plan is a **stage-and-report** step.
2. **Tabs for indentation** in all C# (root `.editorconfig` posture). `var` for return assignments; explicit type + target-typed `new()` for construction. Accessibility modifiers omitted when default (`class Foo`, not `internal class Foo`); `sealed` on concrete types.
3. Working directory for all `dotnet` commands: `poc\aichatweb\VoyageEmbeddings` unless stated.
4. Package versions: install latest stable (or latest prerelease where the plan says prerelease) at execution time and **pin the exact version** the restore resolves — pre-GA posture is ride-the-current-train, pin-exact.

## Verified Facts (researched 2026-06-07 — do NOT re-research, do NOT contradict)

| Fact | Detail |
|---|---|
| Anthropic C# SDK | Package `Anthropic` (v10+, beta). `AnthropicClient client = new();` reads `ANTHROPIC_API_KEY` env var. `client.AsIChatClient("claude-opus-4-8")` → `IChatClient` (namespace `Microsoft.Extensions.AI` via the SDK's M.E.AI.Abstractions dependency). Compose with `.UseFunctionInvocation()`. SDK auto-retries 2× internally. |
| Voyage embeddings API | `POST {base}/embeddings` (base default `https://api.voyageai.com/v1`). Request: `input` (string[] ≤ **1000** items), `model`, `input_type` (`"query"`/`"document"`/null), `truncation` (API default **true** — we send **false** unless opted in), `output_dimension` (256/512/1024/2048), `output_dtype` (default `float`). voyage-4 token cap: 320K/request. Response: `{ "data": [{"embedding": [floats], "index": n}], "model": "...", "usage": {"total_tokens": n} }`. Auth: `Authorization: Bearer {key}`. |
| SK MongoDB MEVD connector | Package `Microsoft.SemanticKernel.Connectors.MongoDB` (**prerelease**). Key property type: **string only** (maps to `_id`). `[VectorStoreData(StorageName=...)]` **not supported** — use `[BsonElement("...")]`. DI: `AddMongoVectorStore()` (+ collection registration; verify exact generic overload against the installed version — same MEVD train as the Qdrant connector 1.67.1-preview already in the csproj). Requires registered `IMongoDatabase`. |
| Aspire MongoDB | Hosting: `Aspire.Hosting.MongoDB` — `AddMongoDB(name)`, `.AddDatabase(name)`, `.WithDataVolume()`, `.WithLifetime()`, `.WithImage()/.WithImageTag()`. Client: `Aspire.MongoDB.Driver.v2` — `builder.AddMongoDBClient(connectionName, configureClientSettings: ...)` registers `IMongoClient` + `IMongoDatabase` (database registered when the connection name is an `AddDatabase` resource). |
| atlas-local gotchas | (a) Image `mongodb/mongodb-atlas-local` reads `MONGODB_INITDB_ROOT_USERNAME`/`MONGODB_INITDB_ROOT_PASSWORD`; the plain `mongo` image (Aspire's default) uses `MONGO_INITDB_ROOT_*`. If Aspire injects credentials under the wrong names, the container ignores them and auth fails → prefer the credential-free `AddMongoDB(name)` shape; if the resolved connection string still carries credentials, map them with `.WithEnvironment("MONGODB_INITDB_ROOT_USERNAME", userParam)` etc. (b) atlas-local self-configures a single-node replica set → client must set `DirectConnection = true` (or `?directConnection=true`) or driver discovery fails from the host network. (c) Search indexes build **asynchronously** — ingest-then-immediate-search can silently return empty. |

## File Structure (end state)

```
poc/aichatweb/VoyageEmbeddings/
├─ Voyage.Extensions.AI/                      [NEW — the OSS seed]
│  ├─ Voyage.Extensions.AI.csproj
│  ├─ VoyageInputType.cs                      enum (0 = sentinel, per platform enum law)
│  ├─ VoyageEmbeddingGeneratorOptions.cs      options + Validate()
│  ├─ VoyageEmbeddingGenerator.cs             IEmbeddingGenerator<string, Embedding<float>>
│  ├─ VoyageApiException.cs                   typed error surface
│  ├─ VoyageJsonContext.cs                    STJ source-gen context + wire shapes
│  └─ VoyageServiceCollectionExtensions.cs    AddVoyageEmbeddingGenerator(key, configure)
├─ Voyage.Extensions.AI.Tests/                [NEW]
│  ├─ Voyage.Extensions.AI.Tests.csproj
│  ├─ StubHttpHandler.cs
│  ├─ VoyageEmbeddingGeneratorOptionsTests.cs
│  ├─ VoyageEmbeddingGeneratorTests.cs
│  ├─ VoyageServiceCollectionExtensionsTests.cs
│  └─ VoyageLiveSmokeTests.cs                 skipped unless VOYAGE_API_KEY set
├─ VoyageEmbeddings.AppHost/AppHost.cs        [MODIFY per stage]
├─ VoyageEmbeddings.AppHost/*.csproj          [MODIFY per stage]
├─ VoyageEmbeddings.Web/Program.cs            [MODIFY per stage]
├─ VoyageEmbeddings.Web/*.csproj              [MODIFY per stage]
├─ VoyageEmbeddings.Web/Services/IngestedChunk.cs        [MODIFY ② dims, ③ key/attrs]
├─ VoyageEmbeddings.Web/Services/SemanticSearch.cs       [MODIFY ② explicit query embed]
├─ VoyageEmbeddings.Web/Services/OllamaResilienceHandlerExtensions.cs  [DELETE ②]
└─ poc/aichatweb/FINDINGS.md                  [NEW — Task 11]
```

`Chat.razor`, `DataIngestor.cs`, `DocumentReader.cs` are expected to need **zero edits** (DataIngestor gets one comment). Any forced edit there is a finding — record it, don't silently absorb it.

---

## Task 1: Scaffold `Voyage.Extensions.AI` + test project

**Files:**
- Create: `Voyage.Extensions.AI/Voyage.Extensions.AI.csproj`
- Create: `Voyage.Extensions.AI.Tests/Voyage.Extensions.AI.Tests.csproj`
- Modify: `VoyageEmbeddings.slnx` (via `dotnet sln add`)

- [ ] **Step 1: Create the classlib project file**

`Voyage.Extensions.AI/Voyage.Extensions.AI.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Voyage.Extensions.AI</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.AI.Abstractions" Version="10.6.0" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Voyage.Extensions.AI.Tests" />
  </ItemGroup>

</Project>
```

(If restore reports a newer 10.x for either package, take it and pin what resolves.)

- [ ] **Step 2: Create the test project file**

`Voyage.Extensions.AI.Tests/Voyage.Extensions.AI.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <OutputType>Exe</OutputType>
    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
    <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit.v3" Version="3.1.0" />
    <PackageReference Include="Shouldly" Version="4.3.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Voyage.Extensions.AI\Voyage.Extensions.AI.csproj" />
  </ItemGroup>

</Project>
```

(Same rule: take latest stable xunit.v3/Shouldly at execution time, pin what resolves.)

- [ ] **Step 3: Add both projects to the solution**

```powershell
dotnet sln VoyageEmbeddings.slnx add Voyage.Extensions.AI/Voyage.Extensions.AI.csproj Voyage.Extensions.AI.Tests/Voyage.Extensions.AI.Tests.csproj
```

- [ ] **Step 4: Verify it builds**

Run: `dotnet build Voyage.Extensions.AI.Tests/Voyage.Extensions.AI.Tests.csproj`
Expected: Build succeeded (empty projects).

- [ ] **Step 5: Stage**

```powershell
git add poc/aichatweb/VoyageEmbeddings/Voyage.Extensions.AI poc/aichatweb/VoyageEmbeddings/Voyage.Extensions.AI.Tests poc/aichatweb/VoyageEmbeddings/VoyageEmbeddings.slnx
```

---

## Task 2: `VoyageInputType` + options with hard-fail validation

**Files:**
- Create: `Voyage.Extensions.AI/VoyageInputType.cs`
- Create: `Voyage.Extensions.AI/VoyageEmbeddingGeneratorOptions.cs`
- Test: `Voyage.Extensions.AI.Tests/VoyageEmbeddingGeneratorOptionsTests.cs`

- [ ] **Step 1: Write the failing tests**

`VoyageEmbeddingGeneratorOptionsTests.cs`:

```csharp
using Shouldly;
using Voyage.Extensions.AI;

namespace Voyage.Extensions.AI.Tests;

public class VoyageEmbeddingGeneratorOptionsTests
{
	static VoyageEmbeddingGeneratorOptions Valid() => new()
	{
		Model = "voyage-4",
		InputType = VoyageInputType.Document,
		ApiKey = "test-key",
	};

	[Fact]
	public void Validate_passes_for_valid_options() =>
		Should.NotThrow(() => Valid().Validate());

	[Fact]
	public void Validate_throws_when_model_missing()
	{
		VoyageEmbeddingGeneratorOptions options = Valid() with { Model = null };
		Should.Throw<InvalidOperationException>(() => options.Validate())
			.Message.ShouldContain("Model");
	}

	[Fact]
	public void Validate_throws_when_input_type_unspecified()
	{
		VoyageEmbeddingGeneratorOptions options = Valid() with { InputType = VoyageInputType.Unspecified };
		Should.Throw<InvalidOperationException>(() => options.Validate())
			.Message.ShouldContain("InputType");
	}

	[Fact]
	public void Validate_throws_when_api_key_missing()
	{
		VoyageEmbeddingGeneratorOptions options = Valid() with { ApiKey = "" };
		Should.Throw<InvalidOperationException>(() => options.Validate())
			.Message.ShouldContain("ApiKey");
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1001)]
	public void Validate_throws_when_max_batch_size_out_of_range(int size)
	{
		VoyageEmbeddingGeneratorOptions options = Valid() with { MaxBatchSize = size };
		Should.Throw<InvalidOperationException>(() => options.Validate())
			.Message.ShouldContain("MaxBatchSize");
	}

	[Fact]
	public void Truncation_defaults_to_false() =>
		Valid().Truncation.ShouldBeFalse();

	[Fact]
	public void BaseUrl_defaults_to_voyage_api() =>
		Valid().BaseUrl.ShouldBe(new Uri("https://api.voyageai.com/v1/"));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Voyage.Extensions.AI.Tests/Voyage.Extensions.AI.Tests.csproj`
Expected: compile FAILURE — types not defined.

- [ ] **Step 3: Implement the enum and options**

`VoyageInputType.cs`:

```csharp
namespace Voyage.Extensions.AI;

/// <summary>
/// Voyage AI <c>input_type</c>. Queries and documents are embedded asymmetrically;
/// declaring which side a generator serves is mandatory — there is no default.
/// </summary>
public enum VoyageInputType
{
	/// <summary>Sentinel. Never valid for a request; construction-time validation rejects it.</summary>
	Unspecified = 0,
	/// <summary>Embed search queries (<c>input_type: "query"</c>).</summary>
	Query = 1,
	/// <summary>Embed corpus documents (<c>input_type: "document"</c>).</summary>
	Document = 2,
}
```

`VoyageEmbeddingGeneratorOptions.cs`:

```csharp
namespace Voyage.Extensions.AI;

/// <summary>
/// Options for <see cref="VoyageEmbeddingGenerator"/>. <see cref="Model"/>, <see cref="InputType"/>,
/// and <see cref="ApiKey"/> are required — embedding identity and query/document asymmetry are never defaulted.
/// </summary>
public sealed record VoyageEmbeddingGeneratorOptions
{
	/// <summary>Embedding model id (e.g. <c>voyage-4</c>). Required: model version is index identity.</summary>
	public string? Model { get; init; }

	/// <summary>Which side of the query/document asymmetry this generator serves. Required.</summary>
	public VoyageInputType InputType { get; init; }

	/// <summary>Voyage API key. Required.</summary>
	public string? ApiKey { get; init; }

	/// <summary>
	/// API base URL. Defaults to the Voyage API; point at <c>https://ai.mongodb.com/v1/</c>
	/// for the Atlas-fronted endpoint (same wire shape).
	/// </summary>
	public Uri BaseUrl { get; init; } = new("https://api.voyageai.com/v1/");

	/// <summary>Matryoshka output dimension (256/512/1024/2048, model-dependent). Null = model native.</summary>
	public int? OutputDimension { get; init; }

	/// <summary>
	/// Whether the API may silently truncate over-length inputs. Defaults to <c>false</c>
	/// (deliberately inverting the API default of <c>true</c>): an embedding of half a document
	/// claiming to represent the whole is a silent fallback. Opt in explicitly if truncation is acceptable.
	/// </summary>
	public bool Truncation { get; init; }

	/// <summary>Max inputs per request. Voyage caps at 1000; lower it to trade fewer tokens per request.</summary>
	public int MaxBatchSize { get; init; } = 1000;

	/// <summary>Throws if any required value is missing or out of range. Called at registration — fail at boot, not first request.</summary>
	public void Validate()
	{
		if (string.IsNullOrWhiteSpace(Model))
			throw new InvalidOperationException($"{nameof(VoyageEmbeddingGeneratorOptions)}.{nameof(Model)} is required. Model version is index identity; it is never defaulted.");
		if (InputType is VoyageInputType.Unspecified)
			throw new InvalidOperationException($"{nameof(VoyageEmbeddingGeneratorOptions)}.{nameof(InputType)} is required. Declare {nameof(VoyageInputType.Query)} or {nameof(VoyageInputType.Document)} — the asymmetry is mandatory.");
		if (string.IsNullOrWhiteSpace(ApiKey))
			throw new InvalidOperationException($"{nameof(VoyageEmbeddingGeneratorOptions)}.{nameof(ApiKey)} is required.");
		if (MaxBatchSize is < 1 or > 1000)
			throw new InvalidOperationException($"{nameof(VoyageEmbeddingGeneratorOptions)}.{nameof(MaxBatchSize)} must be between 1 and 1000 (Voyage per-request cap).");
	}
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Voyage.Extensions.AI.Tests/Voyage.Extensions.AI.Tests.csproj`
Expected: all PASS.

- [ ] **Step 5: Stage**

```powershell
git add poc/aichatweb/VoyageEmbeddings/Voyage.Extensions.AI poc/aichatweb/VoyageEmbeddings/Voyage.Extensions.AI.Tests
```

---

## Task 3: Wire shapes + request shaping

**Files:**
- Create: `Voyage.Extensions.AI/VoyageJsonContext.cs`
- Create: `Voyage.Extensions.AI/VoyageApiException.cs`
- Create: `Voyage.Extensions.AI/VoyageEmbeddingGenerator.cs`
- Create: `Voyage.Extensions.AI.Tests/StubHttpHandler.cs`
- Test: `Voyage.Extensions.AI.Tests/VoyageEmbeddingGeneratorTests.cs`

- [ ] **Step 1: Write the stub handler**

`StubHttpHandler.cs`:

```csharp
using System.Net;
using System.Text;

namespace Voyage.Extensions.AI.Tests;

/// <summary>Captures outgoing requests and replays canned responses, in order.</summary>
sealed class StubHttpHandler : HttpMessageHandler
{
	readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

	public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

	public void Enqueue(HttpStatusCode status, string body) =>
		_responses.Enqueue((status, body));

	/// <summary>Canned success body for <paramref name="count"/> embeddings of <paramref name="dimensions"/> dims, values = index.</summary>
	public void EnqueueEmbeddings(int count, int dimensions, int totalTokens)
	{
		var data = string.Join(",", Enumerable.Range(0, count).Select(i =>
			$$"""{"object":"embedding","embedding":[{{string.Join(",", Enumerable.Repeat($"{i}.0", dimensions))}}],"index":{{i}}}"""));
		Enqueue(HttpStatusCode.OK,
			$$"""{"object":"list","data":[{{data}}],"model":"voyage-4","usage":{"total_tokens":{{totalTokens}}}}""");
	}

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
		Requests.Add((request, body));
		if (_responses.Count == 0)
			throw new InvalidOperationException("StubHttpHandler: no response enqueued for this request.");
		var (status, responseBody) = _responses.Dequeue();
		return new HttpResponseMessage(status)
		{
			Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
		};
	}
}
```

- [ ] **Step 2: Write the failing request-shaping tests**

`VoyageEmbeddingGeneratorTests.cs` (initial set — later tasks append to this file):

```csharp
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Shouldly;
using Voyage.Extensions.AI;

namespace Voyage.Extensions.AI.Tests;

public class VoyageEmbeddingGeneratorTests
{
	static (VoyageEmbeddingGenerator Generator, StubHttpHandler Handler) Create(
		VoyageInputType inputType = VoyageInputType.Document,
		int? outputDimension = 1024,
		bool truncation = false,
		int maxBatchSize = 1000)
	{
		StubHttpHandler handler = new();
		HttpClient http = new(handler) { BaseAddress = new Uri("https://api.voyageai.com/v1/") };
		http.DefaultRequestHeaders.Authorization = new("Bearer", "test-key");
		VoyageEmbeddingGeneratorOptions options = new()
		{
			Model = "voyage-4",
			InputType = inputType,
			ApiKey = "test-key",
			OutputDimension = outputDimension,
			Truncation = truncation,
			MaxBatchSize = maxBatchSize,
		};
		return (new VoyageEmbeddingGenerator(http, options), handler);
	}

	static JsonElement ParseBody(string body) =>
		JsonDocument.Parse(body).RootElement;

	[Fact]
	public async Task Request_carries_model_inputs_input_type_truncation_and_dimension()
	{
		var (generator, handler) = Create();
		handler.EnqueueEmbeddings(count: 2, dimensions: 4, totalTokens: 10);

		await generator.GenerateAsync(["alpha", "beta"]);

		var body = ParseBody(handler.Requests.Single().Body);
		body.GetProperty("model").GetString().ShouldBe("voyage-4");
		body.GetProperty("input").EnumerateArray().Select(e => e.GetString()).ShouldBe(["alpha", "beta"]);
		body.GetProperty("input_type").GetString().ShouldBe("document");
		body.GetProperty("truncation").GetBoolean().ShouldBeFalse();
		body.GetProperty("output_dimension").GetInt32().ShouldBe(1024);
	}

	[Fact]
	public async Task Query_generator_sends_query_input_type()
	{
		var (generator, handler) = Create(inputType: VoyageInputType.Query);
		handler.EnqueueEmbeddings(count: 1, dimensions: 4, totalTokens: 3);

		await generator.GenerateAsync(["what is covered?"]);

		ParseBody(handler.Requests.Single().Body).GetProperty("input_type").GetString().ShouldBe("query");
	}

	[Fact]
	public async Task Output_dimension_omitted_from_wire_when_not_configured()
	{
		var (generator, handler) = Create(outputDimension: null);
		handler.EnqueueEmbeddings(count: 1, dimensions: 4, totalTokens: 3);

		await generator.GenerateAsync(["alpha"]);

		ParseBody(handler.Requests.Single().Body).TryGetProperty("output_dimension", out _).ShouldBeFalse();
	}

	[Fact]
	public async Task Posts_to_embeddings_relative_to_base_url()
	{
		var (generator, handler) = Create();
		handler.EnqueueEmbeddings(count: 1, dimensions: 4, totalTokens: 3);

		await generator.GenerateAsync(["alpha"]);

		handler.Requests.Single().Request.RequestUri.ShouldBe(new Uri("https://api.voyageai.com/v1/embeddings"));
	}
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test Voyage.Extensions.AI.Tests/Voyage.Extensions.AI.Tests.csproj`
Expected: compile FAILURE — `VoyageEmbeddingGenerator` not defined.

- [ ] **Step 4: Implement wire shapes, exception, and the generator (request side + minimal response mapping)**

`VoyageJsonContext.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Voyage.Extensions.AI;

sealed record VoyageEmbeddingRequest
{
	[JsonPropertyName("input")]
	public required IReadOnlyList<string> Input { get; init; }

	[JsonPropertyName("model")]
	public required string Model { get; init; }

	[JsonPropertyName("input_type")]
	public required string InputType { get; init; }

	[JsonPropertyName("truncation")]
	public required bool Truncation { get; init; }

	[JsonPropertyName("output_dimension")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? OutputDimension { get; init; }
}

sealed record VoyageEmbeddingResponse
{
	[JsonPropertyName("data")]
	public required IReadOnlyList<VoyageEmbeddingData> Data { get; init; }

	[JsonPropertyName("model")]
	public required string Model { get; init; }

	[JsonPropertyName("usage")]
	public required VoyageUsage Usage { get; init; }
}

sealed record VoyageEmbeddingData
{
	[JsonPropertyName("embedding")]
	public required float[] Embedding { get; init; }

	[JsonPropertyName("index")]
	public required int Index { get; init; }
}

sealed record VoyageUsage
{
	[JsonPropertyName("total_tokens")]
	public required long TotalTokens { get; init; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(VoyageEmbeddingRequest))]
[JsonSerializable(typeof(VoyageEmbeddingResponse))]
partial class VoyageJsonContext : JsonSerializerContext;
```

`VoyageApiException.cs`:

```csharp
using System.Net;

namespace Voyage.Extensions.AI;

/// <summary>A non-success response from the Voyage API, carrying the status code and raw error body.</summary>
public sealed class VoyageApiException(HttpStatusCode statusCode, string responseBody)
	: Exception($"Voyage API request failed with {(int)statusCode} {statusCode}: {responseBody}")
{
	public HttpStatusCode StatusCode { get; } = statusCode;
	public string ResponseBody { get; } = responseBody;
}
```

`VoyageEmbeddingGenerator.cs`:

```csharp
using System.Net.Http.Json;
using Microsoft.Extensions.AI;

namespace Voyage.Extensions.AI;

/// <summary>
/// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> over the Voyage AI embeddings API.
/// The query/document asymmetry is pinned at construction via
/// <see cref="VoyageEmbeddingGeneratorOptions.InputType"/> — register one instance per side.
/// </summary>
public sealed class VoyageEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
	readonly HttpClient _httpClient;
	readonly VoyageEmbeddingGeneratorOptions _options;
	readonly string _inputType;
	readonly EmbeddingGeneratorMetadata _metadata;

	public VoyageEmbeddingGenerator(HttpClient httpClient, VoyageEmbeddingGeneratorOptions options)
	{
		options.Validate();
		_httpClient = httpClient;
		_options = options;
		_inputType = options.InputType switch
		{
			VoyageInputType.Query => "query",
			VoyageInputType.Document => "document",
			_ => throw new InvalidOperationException($"Unreachable: {nameof(options.InputType)} was validated."),
		};
		_metadata = new EmbeddingGeneratorMetadata("voyageai", options.BaseUrl, options.Model, options.OutputDimension);
	}

	public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
		IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
	{
		var (model, outputDimension) = ResolvePerCallOptions(options);
		List<string> inputs = [.. values];
		GeneratedEmbeddings<Embedding<float>> result = new() { Usage = new UsageDetails { InputTokenCount = 0, TotalTokenCount = 0 } };

		foreach (var batch in inputs.Chunk(_options.MaxBatchSize))
		{
			VoyageEmbeddingRequest request = new()
			{
				Input = batch,
				Model = model,
				InputType = _inputType,
				Truncation = _options.Truncation,
				OutputDimension = outputDimension,
			};

			using var httpResponse = await _httpClient.PostAsJsonAsync(
				"embeddings", request, VoyageJsonContext.Default.VoyageEmbeddingRequest, cancellationToken);

			if (!httpResponse.IsSuccessStatusCode)
			{
				var errorBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
				throw new VoyageApiException(httpResponse.StatusCode, errorBody);
			}

			var response = await httpResponse.Content.ReadFromJsonAsync(
				VoyageJsonContext.Default.VoyageEmbeddingResponse, cancellationToken)
				?? throw new VoyageApiException(httpResponse.StatusCode, "Response body was empty.");

			foreach (var item in response.Data.OrderBy(d => d.Index))
			{
				result.Add(new Embedding<float>(item.Embedding) { ModelId = response.Model });
			}

			result.Usage.InputTokenCount += response.Usage.TotalTokens;
			result.Usage.TotalTokenCount += response.Usage.TotalTokens;
		}

		return result;
	}

	(string Model, int? OutputDimension) ResolvePerCallOptions(EmbeddingGenerationOptions? options)
	{
		if (options?.AdditionalProperties is { Count: > 0 } extras)
			throw new NotSupportedException(
				$"Unrecognized {nameof(EmbeddingGenerationOptions.AdditionalProperties)}: {string.Join(", ", extras.Keys)}. " +
				$"{nameof(VoyageEmbeddingGenerator)} honors {nameof(EmbeddingGenerationOptions.ModelId)} and {nameof(EmbeddingGenerationOptions.Dimensions)}; " +
				"input_type is pinned at construction and is not a per-call option.");

		return (options?.ModelId ?? _options.Model!, options?.Dimensions ?? _options.OutputDimension);
	}

	public object? GetService(Type serviceType, object? serviceKey = null) =>
		serviceKey is null && serviceType.IsInstanceOfType(_metadata) ? _metadata
		: serviceKey is null && serviceType.IsInstanceOfType(this) ? this
		: null;

	public void Dispose()
	{
		// HttpClient lifetime is owned by IHttpClientFactory (or the caller); nothing to dispose.
	}
}
```

Note: if `EmbeddingGeneratorMetadata`'s constructor signature differs in the installed Microsoft.Extensions.AI.Abstractions version, match the actual signature (provider name `"voyageai"`, the base URI, default model id, default dimensions) — do not invent parameters.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Voyage.Extensions.AI.Tests/Voyage.Extensions.AI.Tests.csproj`
Expected: all PASS.

- [ ] **Step 6: Stage**

```powershell
git add poc/aichatweb/VoyageEmbeddings/Voyage.Extensions.AI poc/aichatweb/VoyageEmbeddings/Voyage.Extensions.AI.Tests
```

---

## Task 4: Response mapping, usage, per-call honor-or-throw

**Files:**
- Test: `Voyage.Extensions.AI.Tests/VoyageEmbeddingGeneratorTests.cs` (append)

The implementation already landed in Task 3; this task pins the behavior with tests so regressions can't slip in silently.

- [ ] **Step 1: Append the failing/behavioral tests**

Append to `VoyageEmbeddingGeneratorTests.cs`:

```csharp
	[Fact]
	public async Task Embeddings_come_back_in_input_order_with_model_id()
	{
		var (generator, handler) = Create();
		handler.EnqueueEmbeddings(count: 3, dimensions: 4, totalTokens: 12);

		var result = await generator.GenerateAsync(["a", "b", "c"]);

		result.Count.ShouldBe(3);
		// Stub encodes the input index as every vector component.
		result[0].Vector.ToArray().ShouldAllBe(v => v == 0f);
		result[1].Vector.ToArray().ShouldAllBe(v => v == 1f);
		result[2].Vector.ToArray().ShouldAllBe(v => v == 2f);
		result[0].ModelId.ShouldBe("voyage-4");
	}

	[Fact]
	public async Task Usage_total_tokens_is_reported()
	{
		var (generator, handler) = Create();
		handler.EnqueueEmbeddings(count: 1, dimensions: 4, totalTokens: 42);

		var result = await generator.GenerateAsync(["a"]);

		result.Usage.ShouldNotBeNull();
		result.Usage.TotalTokenCount.ShouldBe(42);
	}

	[Fact]
	public async Task Per_call_dimensions_override_is_honored()
	{
		var (generator, handler) = Create(outputDimension: 1024);
		handler.EnqueueEmbeddings(count: 1, dimensions: 4, totalTokens: 3);

		await generator.GenerateAsync(["a"], new EmbeddingGenerationOptions { Dimensions = 256 });

		ParseBody(handler.Requests.Single().Body).GetProperty("output_dimension").GetInt32().ShouldBe(256);
	}

	[Fact]
	public async Task Per_call_model_override_is_honored()
	{
		var (generator, handler) = Create();
		handler.EnqueueEmbeddings(count: 1, dimensions: 4, totalTokens: 3);

		await generator.GenerateAsync(["a"], new EmbeddingGenerationOptions { ModelId = "voyage-4-lite" });

		ParseBody(handler.Requests.Single().Body).GetProperty("model").GetString().ShouldBe("voyage-4-lite");
	}

	[Fact]
	public async Task Unknown_additional_properties_throw_not_supported()
	{
		var (generator, handler) = Create();
		handler.EnqueueEmbeddings(count: 1, dimensions: 4, totalTokens: 3);

		EmbeddingGenerationOptions options = new()
		{
			AdditionalProperties = new() { ["input_type"] = "query" },
		};

		var exception = await Should.ThrowAsync<NotSupportedException>(
			() => generator.GenerateAsync(["a"], options));
		exception.Message.ShouldContain("input_type");
		handler.Requests.ShouldBeEmpty(); // failed loudly before any wire traffic
	}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test Voyage.Extensions.AI.Tests/Voyage.Extensions.AI.Tests.csproj`
Expected: all PASS (behavior landed in Task 3). If any fail, fix `VoyageEmbeddingGenerator` — the tests are the contract.

- [ ] **Step 3: Stage**

```powershell
git add poc/aichatweb/VoyageEmbeddings/Voyage.Extensions.AI.Tests
```

---

## Task 5: Batching + error surfacing

**Files:**
- Test: `Voyage.Extensions.AI.Tests/VoyageEmbeddingGeneratorTests.cs` (append)

- [ ] **Step 1: Append the tests**

```csharp
	[Fact]
	public async Task Inputs_beyond_max_batch_size_split_into_sequential_requests_preserving_order()
	{
		var (generator, handler) = Create(maxBatchSize: 2);
		handler.EnqueueEmbeddings(count: 2, dimensions: 4, totalTokens: 10);
		handler.EnqueueEmbeddings(count: 2, dimensions: 4, totalTokens: 10);
		handler.EnqueueEmbeddings(count: 1, dimensions: 4, totalTokens: 5);

		var result = await generator.GenerateAsync(["a", "b", "c", "d", "e"]);

		handler.Requests.Count.ShouldBe(3);
		ParseBody(handler.Requests[0].Body).GetProperty("input").GetArrayLength().ShouldBe(2);
		ParseBody(handler.Requests[2].Body).GetProperty("input").EnumerateArray().Single().GetString().ShouldBe("e");
		result.Count.ShouldBe(5);
		result.Usage!.TotalTokenCount.ShouldBe(25); // summed across batches
	}

	[Fact]
	public async Task Non_success_response_throws_with_status_and_body()
	{
		var (generator, handler) = Create();
		handler.Enqueue(System.Net.HttpStatusCode.BadRequest,
			"""{"detail":"The max allowed tokens per submitted batch is 320000."}""");

		var exception = await Should.ThrowAsync<VoyageApiException>(
			() => generator.GenerateAsync(["way too much text"]));

		exception.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
		exception.ResponseBody.ShouldContain("320000");
		exception.Message.ShouldContain("400");
	}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test Voyage.Extensions.AI.Tests/Voyage.Extensions.AI.Tests.csproj`
Expected: all PASS (behavior landed in Task 3; fix the generator if not).

- [ ] **Step 3: Stage**

```powershell
git add poc/aichatweb/VoyageEmbeddings/Voyage.Extensions.AI.Tests
```

---

## Task 6: DI registration extension — keyed pair must coexist

**Files:**
- Create: `Voyage.Extensions.AI/VoyageServiceCollectionExtensions.cs`
- Test: `Voyage.Extensions.AI.Tests/VoyageServiceCollectionExtensionsTests.cs`

- [ ] **Step 1: Write the failing tests**

`VoyageServiceCollectionExtensionsTests.cs`:

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Voyage.Extensions.AI;

namespace Voyage.Extensions.AI.Tests;

public class VoyageServiceCollectionExtensionsTests
{
	[Fact]
	public void Query_and_document_generators_coexist_as_keyed_services()
	{
		ServiceCollection services = new();
		services
			.AddVoyageEmbeddingGenerator("voyage-document", o =>
				o with { Model = "voyage-4", InputType = VoyageInputType.Document, ApiKey = "k" })
			.AddVoyageEmbeddingGenerator("voyage-query", o =>
				o with { Model = "voyage-4", InputType = VoyageInputType.Query, ApiKey = "k" });

		using var provider = services.BuildServiceProvider();

		var documentGenerator = provider.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>("voyage-document");
		var queryGenerator = provider.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>("voyage-query");

		documentGenerator.ShouldNotBeSameAs(queryGenerator); // the gravity9 TryAddSingleton failure, inverted
	}

	[Fact]
	public void Registration_with_invalid_options_fails_at_resolution()
	{
		ServiceCollection services = new();
		services.AddVoyageEmbeddingGenerator("bad", o =>
			o with { Model = "voyage-4", InputType = VoyageInputType.Unspecified, ApiKey = "k" });

		using var provider = services.BuildServiceProvider();

		Should.Throw<InvalidOperationException>(() =>
			provider.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>("bad"));
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Voyage.Extensions.AI.Tests/Voyage.Extensions.AI.Tests.csproj`
Expected: compile FAILURE — extension not defined.

- [ ] **Step 3: Implement the extension**

`VoyageServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Voyage.Extensions.AI;

public static class VoyageServiceCollectionExtensions
{
	/// <summary>
	/// Registers a keyed <see cref="VoyageEmbeddingGenerator"/>. Register one per
	/// <see cref="VoyageInputType"/> side (e.g. keys <c>"voyage-document"</c> and <c>"voyage-query"</c>) —
	/// the query/document asymmetry is per-instance by design.
	/// </summary>
	public static IServiceCollection AddVoyageEmbeddingGenerator(
		this IServiceCollection services,
		string serviceKey,
		Func<VoyageEmbeddingGeneratorOptions, VoyageEmbeddingGeneratorOptions> configure)
	{
		VoyageEmbeddingGeneratorOptions options = configure(new VoyageEmbeddingGeneratorOptions());

		var httpClientName = $"voyage:{serviceKey}";
		services.AddHttpClient(httpClientName, http =>
		{
			http.BaseAddress = options.BaseUrl;
			http.DefaultRequestHeaders.Authorization = new("Bearer", options.ApiKey);
		});

		services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(serviceKey, (provider, _) =>
		{
			var factory = provider.GetRequiredService<IHttpClientFactory>();
			return new VoyageEmbeddingGenerator(factory.CreateClient(httpClientName), options);
		});

		return services;
	}
}
```

Note: `options.Validate()` runs inside the generator constructor, so an invalid registration fails the first time the keyed service is resolved. The host's startup wiring (Program.cs registering the MEVD default alias, Task 9) resolves it at boot — satisfying fail-at-startup. Setting `Authorization` before validation would NRE on a null key only at resolution time, which is the same failure point — acceptable.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Voyage.Extensions.AI.Tests/Voyage.Extensions.AI.Tests.csproj`
Expected: all PASS.

- [ ] **Step 5: Stage**

```powershell
git add poc/aichatweb/VoyageEmbeddings/Voyage.Extensions.AI poc/aichatweb/VoyageEmbeddings/Voyage.Extensions.AI.Tests
```

---

## Task 7: Live smoke test (skipped without API key)

**Files:**
- Test: `Voyage.Extensions.AI.Tests/VoyageLiveSmokeTests.cs`

- [ ] **Step 1: Write the gated test**

```csharp
using System.Numerics.Tensors;
using Microsoft.Extensions.AI;
using Shouldly;
using Voyage.Extensions.AI;

namespace Voyage.Extensions.AI.Tests;

public class VoyageLiveSmokeTests
{
	static string? ApiKey => Environment.GetEnvironmentVariable("VOYAGE_API_KEY");

	static VoyageEmbeddingGenerator Create(VoyageInputType inputType)
	{
		VoyageEmbeddingGeneratorOptions options = new()
		{
			Model = "voyage-4",
			InputType = inputType,
			ApiKey = ApiKey,
			OutputDimension = 1024,
		};
		HttpClient http = new() { BaseAddress = options.BaseUrl };
		http.DefaultRequestHeaders.Authorization = new("Bearer", options.ApiKey);
		return new VoyageEmbeddingGenerator(http, options);
	}

	[Fact(Skip = "Requires VOYAGE_API_KEY environment variable.", SkipUnless = nameof(HasApiKey))]
	public async Task Query_and_document_embeddings_are_sane()
	{
		var documents = await Create(VoyageInputType.Document).GenerateAsync(
			["The policy covers water damage from burst pipes.", "Bananas are an excellent source of potassium."]);
		var query = await Create(VoyageInputType.Query).GenerateAsync(["does my policy cover a burst pipe?"]);

		documents.Count.ShouldBe(2);
		documents[0].Vector.Length.ShouldBe(1024);
		query[0].Vector.Length.ShouldBe(1024);

		var relevant = TensorPrimitives.CosineSimilarity(query[0].Vector.Span, documents[0].Vector.Span);
		var irrelevant = TensorPrimitives.CosineSimilarity(query[0].Vector.Span, documents[1].Vector.Span);
		relevant.ShouldBeGreaterThan(irrelevant);
	}

	public static bool HasApiKey => !string.IsNullOrEmpty(ApiKey);
}
```

(xUnit v3 supports `SkipUnless` on `[Fact]`; if the installed version's attribute surface differs, use `Assert.Skip` guard at the top of the test instead — same effect, still loud about why.)

- [ ] **Step 2: Run without key, then with key if available**

Run: `dotnet test Voyage.Extensions.AI.Tests/Voyage.Extensions.AI.Tests.csproj`
Expected: smoke test SKIPPED, everything else PASS. If the operator provides `VOYAGE_API_KEY` for the session: re-run, expected PASS.

- [ ] **Step 3: Stage**

```powershell
git add poc/aichatweb/VoyageEmbeddings/Voyage.Extensions.AI.Tests
```

---

## Task 8: Stage ① — Chat → Anthropic SDK

**Files:**
- Modify: `VoyageEmbeddings.Web/VoyageEmbeddings.Web.csproj`
- Modify: `VoyageEmbeddings.Web/Program.cs:12-19`
- Modify: `VoyageEmbeddings.Web/appsettings.json`
- Modify: `VoyageEmbeddings.AppHost/AppHost.cs`

- [ ] **Step 1: Package changes (Web)**

```powershell
dotnet remove VoyageEmbeddings.Web/VoyageEmbeddings.Web.csproj package CommunityToolkit.Aspire.OllamaSharp
dotnet add VoyageEmbeddings.Web/VoyageEmbeddings.Web.csproj package Anthropic
```

Keep `OllamaSharp` for now — the embeddings path still uses it until stage ②. **Verify after removal:** `AddOllamaApiClient` for embeddings comes from `CommunityToolkit.Aspire.OllamaSharp`; if removing it breaks the embeddings registration, keep both packages through stage ① and remove both in stage ② instead — note which way it went.

- [ ] **Step 2: Replace the chat registration in `Program.cs`**

Replace lines 12–16 (the `builder.AddOllamaApiClient("chat")…` block) with:

```csharp
AnthropicClient anthropicClient = new(); // reads ANTHROPIC_API_KEY (injected by the AppHost)
var chatModel = builder.Configuration["Anthropic:Model"]
	?? throw new InvalidOperationException("Anthropic:Model configuration is required — no default chat model.");
builder.Services.AddChatClient(anthropicClient.AsIChatClient(chatModel))
	.UseFunctionInvocation()
	.UseOpenTelemetry(configure: c =>
		c.EnableSensitiveData = builder.Environment.IsDevelopment());
```

Add `using Anthropic;` to the top of the file. Leave the `AddOllamaApiClient("embeddings")` block (lines 17–19) untouched. Leave `AddOllamaResilienceHandler()` untouched (Ollama embeddings still need it).

- [ ] **Step 3: Add the model to `appsettings.json`**

Add a top-level section:

```json
"Anthropic": {
	"Model": "claude-opus-4-8"
}
```

- [ ] **Step 4: AppHost — drop the chat model, add the API-key parameter**

In `AppHost.cs`: delete the `var chat = ollama.AddModel("chat", "llama3.2");` line and the `.WithReference(chat)` / `.WaitFor(chat)` calls. Add:

```csharp
var anthropicApiKey = builder.AddParameter("anthropic-api-key", secret: true);
```

and on the web app:

```csharp
webApp.WithEnvironment("ANTHROPIC_API_KEY", anthropicApiKey);
```

- [ ] **Step 5: Set the secret (operator step — report it, don't fake it)**

```powershell
dotnet user-secrets set Parameters:anthropic-api-key "<key>" --project VoyageEmbeddings.AppHost
```

The orchestrator/human supplies the key. If unavailable, report the blocker — do not stub.

- [ ] **Step 6: Build + run + verify (stage ① gate)**

Run: `dotnet build`, then `dotnet run --project VoyageEmbeddings.AppHost` and exercise the chat UI: ask "What's in the emergency survival kit?" Expected: streamed answer, `Search` tool invoked, `<citation>` rendered. Embeddings/Qdrant behavior unchanged.

- [ ] **Step 7: Stage and report for human commit**

```powershell
git add poc/aichatweb/VoyageEmbeddings
git status --short
```

Report the diff summary; the human commits stage ①.

---

## Task 9: Stage ② — Embeddings → Voyage

**Files:**
- Modify: `VoyageEmbeddings.Web/VoyageEmbeddings.Web.csproj` (remove Ollama packages, add project ref)
- Modify: `VoyageEmbeddings.Web/Program.cs`
- Modify: `VoyageEmbeddings.Web/Services/IngestedChunk.cs:8`
- Modify: `VoyageEmbeddings.Web/Services/SemanticSearch.cs`
- Modify: `VoyageEmbeddings.Web/Services/Ingestion/DataIngestor.cs:26` (comment only)
- Delete: `VoyageEmbeddings.Web/Services/OllamaResilienceHandlerExtensions.cs`
- Modify: `VoyageEmbeddings.AppHost/AppHost.cs` + `.csproj`

- [ ] **Step 1: Package and reference changes (Web)**

```powershell
dotnet remove VoyageEmbeddings.Web/VoyageEmbeddings.Web.csproj package OllamaSharp
dotnet add VoyageEmbeddings.Web/VoyageEmbeddings.Web.csproj reference Voyage.Extensions.AI/Voyage.Extensions.AI.csproj
```

(Also remove `CommunityToolkit.Aspire.OllamaSharp` now if Task 8 Step 1 deferred it.)

- [ ] **Step 2: Replace the embeddings registration in `Program.cs`**

Delete the `builder.AddOllamaApiClient("embeddings").AddEmbeddingGenerator();` block and the `builder.Services.AddOllamaResilienceHandler();` line (and its explanatory comment). Add, after the chat registration:

```csharp
var voyageApiKey = builder.Configuration["Voyage:ApiKey"]
	?? throw new InvalidOperationException("Voyage:ApiKey configuration is required.");
builder.Services
	.AddVoyageEmbeddingGenerator("voyage-document", o => o with
	{
		Model = "voyage-4",
		InputType = VoyageInputType.Document,
		ApiKey = voyageApiKey,
		OutputDimension = IngestedChunk.VectorDimensions,
	})
	.AddVoyageEmbeddingGenerator("voyage-query", o => o with
	{
		Model = "voyage-4",
		InputType = VoyageInputType.Query,
		ApiKey = voyageApiKey,
		OutputDimension = IngestedChunk.VectorDimensions,
	});
// Default (non-keyed) generator = the DOCUMENT instance: it serves every corpus-side consumer
// (MEVD auto-embed on upsert, the semantic chunker). Query-side consumers must ask for
// "voyage-query" explicitly — the asymmetry is opt-in by key, never ambient.
builder.Services.AddSingleton(provider =>
	provider.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>("voyage-document"));
```

Add `using Voyage.Extensions.AI;` at the top. Note: the non-keyed alias resolves the keyed document generator **at startup**, which runs `Validate()` — boot-time failure for bad config, as the spec requires.

- [ ] **Step 3: Flip dimensions in `IngestedChunk.cs`**

Replace line 8:

```csharp
	public const int VectorDimensions = 1024; // voyage-4 Matryoshka dimension, pinned explicitly (decision D4)
```

- [ ] **Step 4: Switch `SemanticSearch` to explicit query embedding**

Replace the full class body of `SemanticSearch.cs`:

```csharp
using VoyageEmbeddings.Web.Services.Ingestion;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace VoyageEmbeddings.Web.Services;

public class SemanticSearch(
	VectorStoreCollection<Guid, IngestedChunk> vectorCollection,
	[FromKeyedServices("voyage-query")] IEmbeddingGenerator<string, Embedding<float>> queryEmbeddingGenerator,
	[FromKeyedServices("ingestion_directory")] DirectoryInfo ingestionDirectory,
	DataIngestor dataIngestor)
{
	Task? _ingestionTask;

	public async Task LoadDocumentsAsync() =>
		await (_ingestionTask ??= dataIngestor.IngestDataAsync(ingestionDirectory, searchPattern: "*.*"));

	public async Task<IReadOnlyList<IngestedChunk>> SearchAsync(string text, string? documentIdFilter, int maxResults)
	{
		// Ensure documents have been loaded before searching
		await LoadDocumentsAsync();

		// Queries embed asymmetrically from documents (Voyage input_type), so the query is
		// embedded explicitly here — MEVD's SearchAsync(string) overload cannot express that.
		var queryEmbedding = (await queryEmbeddingGenerator.GenerateAsync([text]))[0];

		var nearest = vectorCollection.SearchAsync(queryEmbedding.Vector, maxResults, new VectorSearchOptions<IngestedChunk>
		{
			Filter = documentIdFilter is { Length: > 0 } ? record => record.DocumentId == documentIdFilter : null,
		});

		return await nearest.Select(result => result.Record).ToListAsync();
	}
}
```

(If the installed MEVD version's vector-search overload takes `ReadOnlyMemory<float>` rather than accepting `Embedding<float>.Vector` directly, pass `queryEmbedding.Vector` — it is `ReadOnlyMemory<float>`. Verify against the compile error, not guesswork.)

- [ ] **Step 5: Annotate the chunker tokenizer mismatch in `DataIngestor.cs`**

Above line 26 (`chunker: new SemanticSimilarityChunker(...)`), add:

```csharp
			// Tokenizer mismatch, accepted for the POC: cl100k/gpt-4o token counts approximate
			// Voyage's tokenizer. Only chunk sizing depends on it, not correctness. (FINDINGS #6)
```

- [ ] **Step 6: AppHost — Ollama exits, Voyage key enters**

In `AppHost.cs`: delete the `AddOllama`/`AddModel` lines and the embeddings `.WithReference(embeddings)` / `.WaitFor(embeddings)` calls. Add:

```csharp
var voyageApiKey = builder.AddParameter("voyage-api-key", secret: true);
```

and on the web app:

```csharp
webApp.WithEnvironment("Voyage__ApiKey", voyageApiKey);
```

Then:

```powershell
dotnet remove VoyageEmbeddings.AppHost/VoyageEmbeddings.AppHost.csproj package CommunityToolkit.Aspire.Hosting.Ollama
dotnet user-secrets set Parameters:voyage-api-key "<key>" --project VoyageEmbeddings.AppHost
```

(Key supplied by the operator; report if blocked.)

- [ ] **Step 7: Reset the Qdrant volume (dimension change 384 → 1024)**

```powershell
docker volume ls --format "{{.Name}}" | Select-String "vectordb"
docker volume rm <matched-volume-name>
```

The persistent Qdrant volume holds a 384-dim collection; `IncrementalIngestion = false` recreates collection content but a stale collection schema can still conflict. Removing the volume is the unambiguous reset. (Stop the container first if it's running.)

- [ ] **Step 8: Run all tests + run app + verify (stage ② gate)**

Run: `dotnet test` (adapter suite green), then `dotnet run --project VoyageEmbeddings.AppHost`. Exercise chat: ingestion runs against the live Voyage API (two sample docs — pennies), search returns relevant chunks, citations render. Watch the dashboard traces: embedding calls should show `voyage` HTTP traffic, zero Ollama resources present.

- [ ] **Step 9: Stage and report for human commit**

```powershell
git add -A poc/aichatweb/VoyageEmbeddings
git status --short
```

---

## Task 10: Stage ③ — Qdrant → Mongo atlas-local

**Files:**
- Modify: `VoyageEmbeddings.AppHost/AppHost.cs` + `.csproj`
- Modify: `VoyageEmbeddings.Web/VoyageEmbeddings.Web.csproj`
- Modify: `VoyageEmbeddings.Web/Program.cs`
- Modify: `VoyageEmbeddings.Web/Services/IngestedChunk.cs`
- Modify: `VoyageEmbeddings.Web/Services/SemanticSearch.cs` (key type only)

- [ ] **Step 1: AppHost — swap the store resource**

```powershell
dotnet remove VoyageEmbeddings.AppHost/VoyageEmbeddings.AppHost.csproj package Aspire.Hosting.Qdrant
dotnet add VoyageEmbeddings.AppHost/VoyageEmbeddings.AppHost.csproj package Aspire.Hosting.MongoDB
```

In `AppHost.cs`, replace the Qdrant block with:

```csharp
var mongo = builder.AddMongoDB("mongo")
	.WithImage("mongodb/mongodb-atlas-local")
	.WithImageTag("8.0")
	.WithDataVolume()
	.WithLifetime(ContainerLifetime.Persistent);
var vectorDb = mongo.AddDatabase("vectordb");
```

and keep `webApp.WithReference(vectorDb).WaitFor(vectorDb);` (same resource name `vectordb`, so the Web connection name is stable).

**Contingencies (Verified Facts table, atlas-local row):**
- Confirm tag `8.0` exists for `mongodb/mongodb-atlas-local` (`docker manifest inspect mongodb/mongodb-atlas-local:8.0`); if not, use the newest available major tag and pin it.
- If `AddMongoDB` injects credentials (check the resolved connection string in the Aspire dashboard): atlas-local ignores `MONGO_INITDB_ROOT_*`. Either use a credential-free overload if one exists, or forward the same parameters as `MONGODB_INITDB_ROOT_USERNAME`/`MONGODB_INITDB_ROOT_PASSWORD` via `.WithEnvironment(...)`. Record which path was needed (FINDINGS #4).

- [ ] **Step 2: Web — swap packages**

```powershell
dotnet remove VoyageEmbeddings.Web/VoyageEmbeddings.Web.csproj package Aspire.Qdrant.Client
dotnet remove VoyageEmbeddings.Web/VoyageEmbeddings.Web.csproj package Microsoft.SemanticKernel.Connectors.Qdrant
dotnet add VoyageEmbeddings.Web/VoyageEmbeddings.Web.csproj package Aspire.MongoDB.Driver.v2
dotnet add VoyageEmbeddings.Web/VoyageEmbeddings.Web.csproj package Microsoft.SemanticKernel.Connectors.MongoDB --prerelease
```

- [ ] **Step 3: Web — swap registrations in `Program.cs`**

Replace `builder.AddQdrantClient("vectordb");` and the `.AddQdrantVectorStore().AddQdrantCollection<Guid, IngestedChunk>(...)` calls with:

```csharp
builder.AddMongoDBClient("vectordb", configureClientSettings: settings =>
	settings.DirectConnection = true); // atlas-local is a single-node replica set; discovery fails without this
builder.Services
	.AddMongoVectorStore()
	.AddMongoCollection<IngestedChunk>(IngestedChunk.CollectionName);
```

**Verify the collection-registration generic signature against the installed connector version** (some versions take `<TKey, TRecord>`, some `<TRecord>` with string keys implied — string is the only supported key type either way). Match what compiles; do not force it.

- [ ] **Step 4: `IngestedChunk` — string key + Bson element names**

Replace the full file:

```csharp
using MongoDB.Bson.Serialization.Attributes;
using Microsoft.Extensions.VectorData;

namespace VoyageEmbeddings.Web.Services;

public class IngestedChunk
{
	public const int VectorDimensions = 1024; // voyage-4 Matryoshka dimension, pinned explicitly (decision D4)
	public const string VectorDistanceFunction = DistanceFunction.CosineSimilarity;
	public const string CollectionName = "data-voyageembeddings-chunks";

	[VectorStoreKey] // maps to _id; Mongo connector supports string keys only
	public required string Key { get; set; }

	[VectorStoreData]
	[BsonElement("documentid")]
	public required string DocumentId { get; set; }

	[VectorStoreData]
	[BsonElement("content")]
	public required string Text { get; set; }

	[VectorStoreData]
	[BsonElement("context")]
	public string? Context { get; set; }

	[VectorStoreVector(VectorDimensions, DistanceFunction = VectorDistanceFunction)]
	[BsonElement("embedding")]
	public string? Vector =>
		Text;
}
```

(`StorageName`/`JsonPropertyName` were the Qdrant-era mapping; the Mongo connector uses `BsonElement`. The element names must match what `VectorStoreWriter` writes — after first ingestion, inspect one document in the collection and align if they differ. Record the actual field names in FINDINGS #3.)

- [ ] **Step 5: `SemanticSearch` — key type follows the connector**

Change the injected collection type:

```csharp
	VectorStoreCollection<string, IngestedChunk> vectorCollection,
```

This is a forced consumer change (typed key leaks through the MEVD abstraction) — record it as part of FINDINGS #3, it's a genuine data point against "registrations-only" reversibility.

- [ ] **Step 6: Run + verify (stage ③ gate)**

Run: `dotnet run --project VoyageEmbeddings.AppHost`. First run pulls the atlas-local image (large — note pull time and image size for FINDINGS #4). Exercise chat end-to-end.

**Expected friction to observe deliberately (do not paper over):**
- Index readiness: if the first search after ingestion returns zero results without error, retry after a few seconds and document the behavior + whatever readiness signal the connector exposes (FINDINGS #4 / spec §7).
- If auth or replica-set connection errors appear, apply the Step 1/Step 3 contingencies and record which one fired.

- [ ] **Step 7: Run full test suite**

Run: `dotnet test`
Expected: all PASS (adapter suite unaffected by store swap — that's the point).

- [ ] **Step 8: Stage and report for human commit**

```powershell
git add -A poc/aichatweb/VoyageEmbeddings
git status --short
```

---

## Task 11: FINDINGS.md

**Files:**
- Create: `poc/aichatweb/FINDINGS.md`

- [ ] **Step 1: Write the findings document**

Structure it per spec §9's docket — one section per item, written from what actually happened (not from this plan's predictions):

```markdown
# aichatweb POC — Findings (2026-06-XX)

Feeds back into docs/superpowers/specs/2026-06-07-vector-embeddings-decision-inputs.md §5.

## 1. Anthropic SDK MEAI adapter
[streaming fidelity, function invocation behavior, ConversationId/statelessness, anything A2 would have been needed for]

## 2. input_type asymmetry
[did constructor-pinned + keyed pair hold; the SemanticSearch manual-embed seam; default-alias-equals-document ergonomics]

## 3. MEVD reversibility (Qdrant → Mongo)
[exact list of changed files/lines; the Guid→string key leak; StorageName→BsonElement; actual field names written by VectorStoreWriter]

## 4. atlas-local muck tax
[image size, pull+start time, credential env-var handling, DirectConnection requirement, search-index readiness behavior]

## 5. Voyage API behaviors
[batching observed, truncation=false failures if any, usage accuracy, dimension pass-through]

## 6. Chunker tokenizer mismatch
[observed effect on chunk quality, if any]

## 7. Adapter OSS-readiness gaps
[what remains between POC state and publishable: token-aware chunking?, dtypes, README/license, package id]
```

Every `[bracket]` is filled with observed reality before this task completes — an unfilled bracket means the task is not done.

- [ ] **Step 2: Stage**

```powershell
git add poc/aichatweb/FINDINGS.md
git status --short
```

---

## Self-Review (completed at plan time)

- **Spec coverage:** D1→Task 10 (atlas-local), D2→Tasks 1–7 (full-bar adapter + own project), D3→Tasks 2/6/9 (pinned + keyed pair, throw on per-call input_type), D4→Tasks 2/9 (1024 explicit), D5→Task 8, D6→Tasks 8/9/10 stage gates, D7→Task 1 (project name), D8→Tasks 2/3 (truncation false), D9→Task 8 (config model), D10→Tasks 8/9 (Aspire secret params); spec §5 chunking→Task 5, usage→Task 4, §7 error handling→Tasks 2/5/6 + Task 10 Step 6, §8 testing→Tasks 2–7 + stage gates, §9 findings→Task 11.
- **Placeholders:** none — every code step has full code; operator-supplied secrets are explicitly operator steps; FINDINGS brackets are deliberate fill-at-execution slots with a loud completion rule.
- **Type consistency:** `VoyageEmbeddingGeneratorOptions` is a `record` with `init` setters — tests and DI extension both use `with` expressions consistently; `IngestedChunk.VectorDimensions` referenced by Task 9 Program.cs matches the Task 9/10 constant; keyed names `"voyage-document"`/`"voyage-query"` consistent across Tasks 6/9 and `SemanticSearch`.
