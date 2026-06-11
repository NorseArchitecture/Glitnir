using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Voyage.Extensions.AI;

namespace Voyage.Extensions.AI.Tests;

public class VoyageServiceCollectionExtensionsTests
{
	static VoyageEmbeddingGeneratorOptions Options(VoyageInputType inputType) => new()
	{
		Model = "voyage-4",
		InputType = inputType,
		ApiKey = "test-key",
	};

	[Fact]
	public void Query_and_document_generators_coexist_as_keyed_services()
	{
		ServiceCollection services = new();
		services
			.AddVoyageEmbeddingGenerator("voyage-document", Options(VoyageInputType.Document))
			.AddVoyageEmbeddingGenerator("voyage-query", Options(VoyageInputType.Query));

		using ServiceProvider provider = services.BuildServiceProvider();

		var documentGenerator = provider.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>("voyage-document");
		var queryGenerator = provider.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>("voyage-query");

		documentGenerator.ShouldNotBeSameAs(queryGenerator);
	}

	[Fact]
	public void Registration_with_invalid_options_fails_at_resolution()
	{
		ServiceCollection services = new();
		services.AddVoyageEmbeddingGenerator("bad", Options(VoyageInputType.Unspecified));

		using ServiceProvider provider = services.BuildServiceProvider();

		Should.Throw<InvalidOperationException>(() =>
			provider.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>("bad"))
			.Message.ShouldContain("InputType");
	}

	[Fact]
	public void Generator_http_client_carries_base_address_and_bearer_auth()
	{
		ServiceCollection services = new();
		services.AddVoyageEmbeddingGenerator("voyage-document", Options(VoyageInputType.Document));

		using ServiceProvider provider = services.BuildServiceProvider();
		// Force materialization so the named HttpClient is created with our configuration
		_ = provider.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>("voyage-document");

		var factory = provider.GetRequiredService<IHttpClientFactory>();
		HttpClient client = factory.CreateClient("voyage:voyage-document");
		client.BaseAddress.ShouldBe(Options(VoyageInputType.Document).BaseUrl);
		client.DefaultRequestHeaders.Authorization!.Scheme.ShouldBe("Bearer");
		client.DefaultRequestHeaders.Authorization.Parameter.ShouldBe("test-key");
	}

	[Fact]
	public void Duplicate_service_key_registration_fails_loudly()
	{
		ServiceCollection services = new();
		services.AddVoyageEmbeddingGenerator("voyage-document", Options(VoyageInputType.Document));

		Should.Throw<InvalidOperationException>(() =>
			services.AddVoyageEmbeddingGenerator("voyage-document", Options(VoyageInputType.Document)))
			.Message.ShouldContain("voyage-document");
	}
}
