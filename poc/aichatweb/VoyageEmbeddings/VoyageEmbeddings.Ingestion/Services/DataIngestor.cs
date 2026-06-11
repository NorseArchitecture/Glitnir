using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using Microsoft.ML.Tokenizers;
using VoyageEmbeddings.Backend;

namespace VoyageEmbeddings.Ingestion.Services;

public partial class DataIngestor(
	ILogger<DataIngestor> logger,
	ILoggerFactory loggerFactory,
	VectorStore vectorStore,
	IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
{
	public async Task IngestDataAsync(DirectoryInfo directory, string searchPattern)
	{
		using VectorStoreWriter<string> writer = new(vectorStore, dimensionCount: IngestedChunk.VectorDimensions, new()
		{
			CollectionName = IngestedChunk.CollectionName,
			DistanceFunction = IngestedChunk.VectorDistanceFunction,
			// Incremental: unchanged documents are not re-embedded — every boot re-running full ingestion would burn the free-tier token budget for nothing.
			IncrementalIngestion = true,
		});

		using IngestionPipeline<string> pipeline = new(
			reader: new DocumentReader(directory),
			// Tokenizer mismatch, accepted for the POC: cl100k/gpt-4o token counts approximate
			// Voyage's tokenizer. Only chunk sizing depends on it, not correctness. (FINDINGS #6)
			chunker: new SemanticSimilarityChunker(embeddingGenerator, new(TiktokenTokenizer.CreateForModel("gpt-4o"))),
			writer: writer,
			loggerFactory: loggerFactory);

		await foreach (var result in pipeline.ProcessAsync(directory, searchPattern))
		{
			LogCompletedProcessing(result.DocumentId, result.Succeeded);
		}
	}

	[LoggerMessage(LogLevel.Information, "Completed processing '{id}'. Succeeded: '{succeeded}'.")]
	partial void LogCompletedProcessing(string id, bool succeeded);
}
