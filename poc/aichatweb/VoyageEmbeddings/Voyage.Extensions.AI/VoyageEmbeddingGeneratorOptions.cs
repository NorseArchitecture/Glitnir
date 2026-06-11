namespace Voyage.Extensions.AI;

/// <summary>
/// Options for <see cref="VoyageEmbeddingGenerator"/>. <see cref="Model"/>, <see cref="InputType"/>,
/// and <see cref="ApiKey"/> are required — embedding identity and query/document asymmetry are never defaulted.
/// </summary>
public sealed record VoyageEmbeddingGeneratorOptions
{
	/// <summary>Embedding model id (e.g. <c>voyage-4</c>). Required: model version is index identity.</summary>
	public required string Model { get; init; }

	/// <summary>Which side of the query/document asymmetry this generator serves. Required.</summary>
	public required VoyageInputType InputType { get; init; }

	/// <summary>Voyage API key. Required.</summary>
	public required string ApiKey { get; init; }

	/// <summary>
	/// API base URL. Defaults to the Voyage API; point at <c>https://ai.mongodb.com/v1/</c>
	/// for the Atlas-fronted endpoint (same wire shape).
	/// </summary>
	public Uri BaseUrl { get; init; } = new("https://api.voyageai.com/v1/"); // trailing slash required for relative Uri joining

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

	/// <summary>Throws if any required value is missing or out of range.</summary>
	/// <remarks>Called at registration — fail at boot, not first request.</remarks>
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
		if (OutputDimension is < 1)
			throw new InvalidOperationException($"{nameof(VoyageEmbeddingGeneratorOptions)}.{nameof(OutputDimension)} must be a positive dimension when specified.");
	}
}
