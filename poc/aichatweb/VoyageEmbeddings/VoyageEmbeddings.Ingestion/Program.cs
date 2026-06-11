using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Voyage.Extensions.AI;
using VoyageEmbeddings.Backend;
using VoyageEmbeddings.Ingestion;
using VoyageEmbeddings.ServiceDefaults;
using DataIngestor = VoyageEmbeddings.Ingestion.Services.DataIngestor;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

var voyageApiKey = builder.Configuration["Voyage:ApiKey"]
	?? throw new InvalidOperationException("Voyage:ApiKey configuration is required.");

// This tier ingests: it registers exactly ONE generator — the document side.
builder.Services.AddVoyageEmbeddingGenerator("voyage-document", new VoyageEmbeddingGeneratorOptions
{
	Model = "voyage-4",
	InputType = VoyageInputType.Document,
	ApiKey = voyageApiKey,
	OutputDimension = IngestedChunk.VectorDimensions,
	MaxBatchSize = 128, // standard tier: batch generously — fewer requests is the bigger ingestion speedup
});
builder.Services.AddSingleton(provider =>
	provider.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>("voyage-document"));
builder.Services.AddSingleton<IEmbeddingGenerator>(provider =>
	provider.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>("voyage-document"));

// Reactive throttling: ingest at full speed until a 429, then honor Retry-After. The free
// tier's proactive 3 RPM cap is retired now that a paid tier lifts the per-minute ceiling.
builder.Services.AddRetryAfterResilience("voyage:voyage-document");

builder.AddMongoDBClient("vectordb", configureClientSettings: settings =>
	settings.DirectConnection = true); // atlas-local is a single-node replica set; discovery fails without DirectConnection
builder.Services.AddMongoVectorStore();
builder.Services.AddSingleton<DataIngestor>();

using var host = builder.Build();

var ingestor = host.Services.GetRequiredService<DataIngestor>();
DirectoryInfo dataDirectory = new(Path.Combine(AppContext.BaseDirectory, "Data"));
await ingestor.IngestDataAsync(dataDirectory, searchPattern: "*.*");
