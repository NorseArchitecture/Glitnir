using System.Net.Http.Json;
using Microsoft.Extensions.AI;

namespace Voyage.Extensions.AI;

/// <summary>
/// Embeds <paramref name="values"/> in batches of at most <see cref="VoyageEmbeddingGeneratorOptions.MaxBatchSize"/>.
/// </summary>
/// <remarks>
/// The query/document asymmetry is pinned at construction via
/// <see cref="VoyageEmbeddingGeneratorOptions.InputType"/> — register one instance per side.
/// An empty input sequence returns an empty result without calling the API.
/// </remarks>
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
		GeneratedEmbeddings<Embedding<float>> result = new();
		long totalTokens = 0;

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

			if (response.Data.Count != batch.Length)
				throw new VoyageApiException(httpResponse.StatusCode,
					$"Response contained {response.Data.Count} embeddings for {batch.Length} inputs — refusing to return a misaligned batch.");

			foreach (var item in response.Data.OrderBy(d => d.Index))
			{
				result.Add(new Embedding<float>(new ReadOnlyMemory<float>(item.Embedding)) { ModelId = response.Model });
			}

			totalTokens += response.Usage.TotalTokens;
		}

		result.Usage = new UsageDetails
		{
			TotalTokenCount = totalTokens,
		};

		return result;
	}

	(string Model, int? OutputDimension) ResolvePerCallOptions(EmbeddingGenerationOptions? options)
	{
		if (options?.AdditionalProperties is { Count: > 0 } extras)
			throw new NotSupportedException(
				$"Unrecognized {nameof(EmbeddingGenerationOptions.AdditionalProperties)}: {string.Join(", ", extras.Keys)}. " +
				$"{nameof(VoyageEmbeddingGenerator)} honors {nameof(EmbeddingGenerationOptions.ModelId)} and {nameof(EmbeddingGenerationOptions.Dimensions)}; " +
				"input_type is pinned at construction and is not a per-call option.");

		return (options?.ModelId ?? _options.Model, options?.Dimensions ?? _options.OutputDimension);
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
