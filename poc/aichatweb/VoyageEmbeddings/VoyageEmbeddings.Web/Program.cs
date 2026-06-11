using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Voyage.Extensions.AI;
using VoyageEmbeddings.Backend;
using VoyageEmbeddings.ServiceDefaults;
using VoyageEmbeddings.Web.Components;
using VoyageEmbeddings.Web.Services;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services
	.AddRazorComponents()
	.AddInteractiveServerComponents();

AnthropicClient anthropicClient = new(); // reads ANTHROPIC_API_KEY (injected by the AppHost)
var chatModel = builder.Configuration["Anthropic:Model"]
	?? throw new InvalidOperationException("Anthropic:Model configuration is required — no default chat model.");
builder.Services.AddChatClient(anthropicClient.AsIChatClient(chatModel))
	.UseFunctionInvocation()
	.UseOpenTelemetry(configure: c =>
		c.EnableSensitiveData = builder.Environment.IsDevelopment());

var voyageApiKey = builder.Configuration["Voyage:ApiKey"]
	?? throw new InvalidOperationException("Voyage:ApiKey configuration is required.");

// This tier serves queries: it registers exactly ONE generator — the query side.
// The collection's model builder still demands an ambient IEmbeddingGenerator because
// IngestedChunk.Vector is a string property; this tier has NO upsert path, so the
// query-flavored ambient generator can never silently embed corpus content.
builder.Services.AddVoyageEmbeddingGenerator("voyage-query", new()
{
	Model = "voyage-4",
	InputType = VoyageInputType.Query,
	ApiKey = voyageApiKey,
	OutputDimension = IngestedChunk.VectorDimensions,
	MaxBatchSize = 128, // standard tier: batch generously (well under Voyage's 1000-input / 320K-token caps)
});
builder.Services.AddSingleton(provider =>
	provider.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>("voyage-query"));
builder.Services.AddSingleton<IEmbeddingGenerator>(provider =>
	provider.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>("voyage-query"));

// Reactive throttling: full speed until a 429, then honor Retry-After. No proactive cap
// now that a paid tier lifts the per-minute ceiling.
builder.Services.AddRetryAfterResilience("voyage:voyage-query");

builder.AddMongoDBClient("vectordb", configureClientSettings: settings =>
	settings.DirectConnection = true); // atlas-local is a single-node replica set; discovery fails without DirectConnection

builder.Services.AddMongoVectorStore();
// AddMongoCollection<TRecord> is single-arity and registers a string-keyed collection, but the
// store holds Guid keys (BSON Binary _id) so SemanticSearch needs VectorStoreCollection<Guid,_>.
// Register it explicitly off the VectorStore with the correct key type. (FINDINGS §Mongo-key)
builder.Services.AddSingleton(sp =>
	sp.GetRequiredService<VectorStore>().GetCollection<Guid, IngestedChunk>(IngestedChunk.CollectionName));
builder.Services.AddSingleton<SemanticSearch>();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/Error", createScopeForErrors: true);
	// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
	app.UseHsts();
}

app
	.UseHttpsRedirection()
	.UseAntiforgery()
	.UseStaticFiles();

app.MapRazorComponents<App>()
	.AddInteractiveServerRenderMode();

await app.RunAsync();
