using System.Net.Http.Headers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Voyage.Extensions.AI;

public static class VoyageServiceCollectionExtensions
{
	/// <summary>
	/// Registers a keyed <see cref="VoyageEmbeddingGenerator"/>. Register one per
	/// <see cref="VoyageInputType"/> side (e.g. keys <c>"voyage-document"</c> and <c>"voyage-query"</c>) —
	/// the query/document asymmetry is per-instance by design.
	/// </summary>
	public static IServiceCollection AddVoyageEmbeddingGenerator(
		this IServiceCollection services,
		string serviceKey,
		VoyageEmbeddingGeneratorOptions options)
	{
		// Duplicate registration is a configuration error — fail loudly rather than
		// silently last-wins (Add) or silently first-wins (TryAdd).
		if (services.Any(d => d.IsKeyedService && Equals(d.ServiceKey, serviceKey)
			&& d.ServiceType == typeof(IEmbeddingGenerator<string, Embedding<float>>)))
			throw new InvalidOperationException($"A Voyage embedding generator is already registered for service key '{serviceKey}'.");

		var httpClientName = $"voyage:{serviceKey}";
		services.AddHttpClient(httpClientName, http =>
		{
			http.BaseAddress = options.BaseUrl;
			http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
		});

		services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(serviceKey, (provider, _) =>
		{
			IHttpClientFactory factory = provider.GetRequiredService<IHttpClientFactory>();
			return new VoyageEmbeddingGenerator(factory.CreateClient(httpClientName), options);
		});

		return services;
	}
}
