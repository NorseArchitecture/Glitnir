using System.Numerics.Tensors;
using Microsoft.Extensions.AI;
using Shouldly;
using Voyage.Extensions.AI;

namespace Voyage.Extensions.AI.Tests;

public class VoyageLiveSmokeTests
{
	static string? ApiKey => Environment.GetEnvironmentVariable("VOYAGE_API_KEY");

	public static bool HasApiKey => !string.IsNullOrEmpty(ApiKey);

	static VoyageEmbeddingGenerator Create(VoyageInputType inputType)
	{
		VoyageEmbeddingGeneratorOptions options = new()
		{
			Model = "voyage-4",
			InputType = inputType,
			ApiKey = ApiKey!,
			OutputDimension = 1024,
		};
		HttpClient http = new() { BaseAddress = options.BaseUrl };
		http.DefaultRequestHeaders.Authorization = new("Bearer", options.ApiKey);
		return new VoyageEmbeddingGenerator(http, options);
	}

	[Fact(SkipUnless = nameof(HasApiKey), Skip = "Requires VOYAGE_API_KEY environment variable.")]
	public async Task Query_and_document_embeddings_are_sane()
	{
		var documents = await Create(VoyageInputType.Document).GenerateAsync(
			["The policy covers water damage from burst pipes.", "Bananas are an excellent source of potassium."],
			cancellationToken: TestContext.Current.CancellationToken);
		var query = await Create(VoyageInputType.Query).GenerateAsync(
			["does my policy cover a burst pipe?"],
			cancellationToken: TestContext.Current.CancellationToken);

		documents.Count.ShouldBe(2);
		documents[0].Vector.Length.ShouldBe(1024);
		documents[1].Vector.Length.ShouldBe(1024);
		query[0].Vector.Length.ShouldBe(1024);

		var relevant = TensorPrimitives.CosineSimilarity(query[0].Vector.Span, documents[0].Vector.Span);
		var irrelevant = TensorPrimitives.CosineSimilarity(query[0].Vector.Span, documents[1].Vector.Span);
		relevant.ShouldBeGreaterThan(irrelevant);
	}
}
