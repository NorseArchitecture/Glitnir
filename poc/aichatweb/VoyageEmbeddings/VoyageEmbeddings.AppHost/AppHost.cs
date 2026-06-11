var builder = DistributedApplication.CreateBuilder(args);

var mongo = builder.AddMongoDB("mongo")
	.WithImage("mongodb/mongodb-atlas-local")
	.WithImageTag("8.0") // confirmed: docker manifest inspect mongodb/mongodb-atlas-local:8.0 → exists (Landmine A: tag verified)
	.WithDataVolume()
	.WithLifetime(ContainerLifetime.Persistent);
// Landmine B (fired live 2026-06-07): Aspire generates an "admin" user + password and bakes them
// into the connection string, but injects them as MONGO_INITDB_ROOT_* — the names the PLAIN mongo
// image reads. atlas-local reads MONGODB_INITDB_ROOT_* instead, so it never creates the user and
// every client auth fails with SCRAM-SHA-256. Forward the SAME generated credentials under
// atlas-local's env-var names so the server creates the exact user the connection string expects.
// (Requires a fresh data volume — atlas-local seeds the root user only on first init.) FINDINGS §4.
mongo.WithEnvironment(context =>
{
	context.EnvironmentVariables["MONGODB_INITDB_ROOT_USERNAME"] = "admin"; // Aspire's default username when none supplied
	context.EnvironmentVariables["MONGODB_INITDB_ROOT_PASSWORD"] = mongo.Resource.PasswordParameter!;
});
// Connection name MUST equal AddMongoDBClient("vectordb") in Web + Ingestion.
var vectorDb = mongo.AddDatabase("vectordb");

// Aspire's built-in MongoDB health check can't reach atlas-local's single-node replica set:
// it builds a MongoClient with no directConnection=true, so SDAM server discovery hangs and
// WaitFor never passes — the container is Up but the resource never goes Healthy
// (dotnet/aspire#7995, #6811). Remove the health-check annotations so dependents gate on the
// container being Running; clients connect with DirectConnection=true (set in each Program.cs)
// and work the moment the node accepts connections. (FINDINGS §4)
foreach (var resource in new IResource[] { mongo.Resource, vectorDb.Resource })
{
	foreach (var healthCheck in resource.Annotations.OfType<HealthCheckAnnotation>().ToArray())
	{
		resource.Annotations.Remove(healthCheck);
	}
}
// Runtime watch (Landmine B): atlas-local reads MONGODB_INITDB_ROOT_{USERNAME,PASSWORD}; the plain
// mongo image (Aspire's default) uses MONGO_INITDB_ROOT_*. If clients fail to authenticate after this,
// the generated credentials never reached the server — forward them under the atlas-local names.

var markitdown = builder.AddContainer("markitdown", "mcp/markitdown")
	.WithImageTag("latest")
	.WithArgs("--http", "--host", "0.0.0.0", "--port", "3001")
	.WithHttpEndpoint(targetPort: 3001, name: "http");

var anthropicApiKey = builder.AddParameter("anthropic-api-key", secret: true);
var voyageApiKey = builder.AddParameter("voyage-api-key", secret: true);

var ingestion = builder.AddProject<Projects.VoyageEmbeddings_Ingestion>("ingestion");
ingestion
	.WithReference(vectorDb)
	.WaitFor(vectorDb)
	.WithEnvironment("MARKITDOWN_MCP_URL", markitdown.GetEndpoint("http"))
	.WithEnvironment("Voyage__ApiKey", voyageApiKey)
	.WaitFor(markitdown);

var webApp = builder.AddProject<Projects.VoyageEmbeddings_Web>("aichatweb-app");
webApp
	.WithReference(vectorDb)
	.WaitFor(vectorDb)
	.WaitForCompletion(ingestion);
webApp
	.WithEnvironment("ANTHROPIC_API_KEY", anthropicApiKey);
webApp
	.WithEnvironment("Voyage__ApiKey", voyageApiKey);

await builder.Build().RunAsync();
