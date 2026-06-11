using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using VoyageEmbeddings.Backend;

namespace VoyageEmbeddings.Web.Services;

public class SemanticSearch(
	VectorStoreCollection<Guid, IngestedChunk> vectorCollection, // Guid key: the connector stores it as BSON Binary _id (FINDINGS §Mongo-key)
	IEmbeddingGenerator<string, Embedding<float>> queryEmbeddingGenerator)
{
	public async Task<IReadOnlyList<IngestedChunk>> SearchAsync(string text, string? documentIdFilter, int maxResults)
	{
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
