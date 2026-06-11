using Microsoft.Extensions.VectorData;
using MongoDB.Bson.Serialization.Attributes;

namespace VoyageEmbeddings.Backend;

public class IngestedChunk
{
	public const int VectorDimensions = 1024; // voyage-4 Matryoshka dimension, pinned explicitly (decision D4)
	public const string VectorDistanceFunction = DistanceFunction.CosineSimilarity;
	public const string CollectionName = "data-voyageembeddings-chunks";

	// maps to _id. The SK Mongo connector + DataIngestion writer generate Guid keys and persist
	// them as BSON Binary (UUID) — proven by the live store (20 docs, _id : Binary). The MS Learn
	// doc's "string keys only" claim is stale/wrong for connector 1.74.0-preview; a string Key
	// ingests fine but throws on read ("Cannot deserialize a 'String' from BsonType 'Binary'").
	// Guid is the correct, evidence-backed key type. (FINDINGS §Mongo-key)
	[VectorStoreKey]
	public required Guid Key { get; set; }

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
