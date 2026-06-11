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
