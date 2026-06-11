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

		await generator.GenerateAsync(["alpha", "beta"], cancellationToken: TestContext.Current.CancellationToken);

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

		await generator.GenerateAsync(["what is covered?"], cancellationToken: TestContext.Current.CancellationToken);

		ParseBody(handler.Requests.Single().Body).GetProperty("input_type").GetString().ShouldBe("query");
	}

	[Fact]
	public async Task Output_dimension_omitted_from_wire_when_not_configured()
	{
		var (generator, handler) = Create(outputDimension: null);
		handler.EnqueueEmbeddings(count: 1, dimensions: 4, totalTokens: 3);

		await generator.GenerateAsync(["alpha"], cancellationToken: TestContext.Current.CancellationToken);

		ParseBody(handler.Requests.Single().Body).TryGetProperty("output_dimension", out _).ShouldBeFalse();
	}

	[Fact]
	public async Task Posts_to_embeddings_relative_to_base_url()
	{
		var (generator, handler) = Create();
		handler.EnqueueEmbeddings(count: 1, dimensions: 4, totalTokens: 3);

		await generator.GenerateAsync(["alpha"], cancellationToken: TestContext.Current.CancellationToken);

		handler.Requests.Single().Request.RequestUri.ShouldBe(new Uri("https://api.voyageai.com/v1/embeddings"));
	}

	[Fact]
	public async Task Embeddings_come_back_in_input_order_with_model_id()
	{
		var (generator, handler) = Create();
		handler.EnqueueEmbeddings(count: 3, dimensions: 4, totalTokens: 12);

		var result = await generator.GenerateAsync(["a", "b", "c"], cancellationToken: TestContext.Current.CancellationToken);

		result.Count.ShouldBe(3);
		result[0].Vector.ToArray().ShouldAllBe(v => v == 0f);
		result[1].Vector.ToArray().ShouldAllBe(v => v == 1f);
		result[2].Vector.ToArray().ShouldAllBe(v => v == 2f);
		result[0].ModelId.ShouldBe("voyage-4");
	}

	[Fact]
	public async Task Out_of_order_response_data_is_reordered_by_index()
	{
		var (generator, handler) = Create();
		// indices deliberately reversed on the wire
		handler.Enqueue(System.Net.HttpStatusCode.OK,
			"""{"object":"list","data":[{"object":"embedding","embedding":[1.0],"index":1},{"object":"embedding","embedding":[0.0],"index":0}],"model":"voyage-4","usage":{"total_tokens":4}}""");

		var result = await generator.GenerateAsync(["a", "b"], cancellationToken: TestContext.Current.CancellationToken);

		result[0].Vector.ToArray().Single().ShouldBe(0f);
		result[1].Vector.ToArray().Single().ShouldBe(1f);
	}

	[Fact]
	public async Task Usage_total_tokens_is_reported_and_input_token_count_left_unset()
	{
		var (generator, handler) = Create();
		handler.EnqueueEmbeddings(count: 1, dimensions: 4, totalTokens: 42);

		var result = await generator.GenerateAsync(["a"], cancellationToken: TestContext.Current.CancellationToken);

		result.Usage.ShouldNotBeNull();
		result.Usage.TotalTokenCount.ShouldBe(42);
		result.Usage.InputTokenCount.ShouldBeNull();
	}

	[Fact]
	public async Task Per_call_dimensions_override_is_honored()
	{
		var (generator, handler) = Create(outputDimension: 1024);
		handler.EnqueueEmbeddings(count: 1, dimensions: 4, totalTokens: 3);

		await generator.GenerateAsync(["a"], new EmbeddingGenerationOptions { Dimensions = 256 }, TestContext.Current.CancellationToken);

		ParseBody(handler.Requests.Single().Body).GetProperty("output_dimension").GetInt32().ShouldBe(256);
	}

	[Fact]
	public async Task Per_call_model_override_is_honored()
	{
		var (generator, handler) = Create();
		handler.EnqueueEmbeddings(count: 1, dimensions: 4, totalTokens: 3);

		await generator.GenerateAsync(["a"], new EmbeddingGenerationOptions { ModelId = "voyage-4-lite" }, TestContext.Current.CancellationToken);

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
			() => generator.GenerateAsync(["a"], options, TestContext.Current.CancellationToken));
		exception.Message.ShouldContain("input_type");
		handler.Requests.ShouldBeEmpty();
	}

	[Fact]
	public async Task Inputs_beyond_max_batch_size_split_into_sequential_requests_preserving_order()
	{
		var (generator, handler) = Create(maxBatchSize: 2);
		handler.EnqueueEmbeddings(count: 2, dimensions: 4, totalTokens: 10);
		handler.EnqueueEmbeddings(count: 2, dimensions: 4, totalTokens: 10);
		handler.EnqueueEmbeddings(count: 1, dimensions: 4, totalTokens: 5);

		var result = await generator.GenerateAsync(["a", "b", "c", "d", "e"], cancellationToken: TestContext.Current.CancellationToken);

		handler.Requests.Count.ShouldBe(3);
		ParseBody(handler.Requests[0].Body).GetProperty("input").GetArrayLength().ShouldBe(2);
		ParseBody(handler.Requests[2].Body).GetProperty("input").EnumerateArray().Single().GetString().ShouldBe("e");
		result.Count.ShouldBe(5);
		result.Usage!.TotalTokenCount.ShouldBe(25);
	}

	[Fact]
	public async Task Response_embedding_count_mismatch_fails_loudly()
	{
		var (generator, handler) = Create();
		handler.EnqueueEmbeddings(count: 1, dimensions: 4, totalTokens: 3); // one embedding for two inputs

		var exception = await Should.ThrowAsync<VoyageApiException>(
			() => generator.GenerateAsync(["a", "b"], cancellationToken: TestContext.Current.CancellationToken));
		exception.Message.ShouldContain("misaligned");
	}

	[Fact]
	public async Task Empty_input_returns_empty_result_without_calling_api()
	{
		var (generator, handler) = Create();

		var result = await generator.GenerateAsync([], cancellationToken: TestContext.Current.CancellationToken);

		result.ShouldBeEmpty();
		handler.Requests.ShouldBeEmpty();
	}

	[Fact]
	public async Task Non_success_response_throws_with_status_and_body()
	{
		var (generator, handler) = Create();
		handler.Enqueue(System.Net.HttpStatusCode.BadRequest,
			"""{"detail":"The max allowed tokens per submitted batch is 320000."}""");

		var exception = await Should.ThrowAsync<VoyageApiException>(
			() => generator.GenerateAsync(["way too much text"], cancellationToken: TestContext.Current.CancellationToken));

		exception.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
		exception.ResponseBody.ShouldContain("320000");
		exception.Message.ShouldContain("400");
	}

}
