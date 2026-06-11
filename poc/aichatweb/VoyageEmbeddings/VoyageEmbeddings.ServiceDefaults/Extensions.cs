using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Polly;

namespace VoyageEmbeddings.ServiceDefaults;

// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
	const string HealthEndpointPath = "/health";
	const string AlivenessEndpointPath = "/alive";

	extension<TBuilder>(TBuilder builder) where TBuilder : IHostApplicationBuilder
	{
		public TBuilder AddServiceDefaults()
		{
			builder.ConfigureOpenTelemetry();

			builder.AddDefaultHealthChecks();

			builder.Services.AddServiceDiscovery();

			builder.Services.ConfigureHttpClientDefaults(http =>
			{
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is experimental
				http.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

				// Turn on resilience by default
				http.AddStandardResilienceHandler();

				// Turn on service discovery by default
				http.AddServiceDiscovery();
			});

			// Uncomment the following to restrict the allowed schemes for service discovery.
			// builder.Services.Configure<ServiceDiscoveryOptions>(options =>
			// {
			//     options.AllowedSchemes = ["https"];
			// });

			return builder;
		}

		public TBuilder ConfigureOpenTelemetry()
		{
			builder.Logging.AddOpenTelemetry(logging =>
			{
				logging.IncludeFormattedMessage = true;
				logging.IncludeScopes = true;
			});

			builder.Services.AddOpenTelemetry()
				.WithMetrics(metrics =>
				{
					metrics.AddAspNetCoreInstrumentation()
						.AddHttpClientInstrumentation()
						.AddRuntimeInstrumentation()
						.AddMeter("Experimental.Microsoft.Extensions.AI");
				})
				.WithTracing(tracing =>
				{
					tracing.AddSource(builder.Environment.ApplicationName)
						.AddAspNetCoreInstrumentation(tracing =>
							// Exclude health check requests from tracing
							tracing.Filter = context =>
								!context.Request.Path.StartsWithSegments(HealthEndpointPath)
								&& !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
						)
						// Uncomment the following line to enable gRPC instrumentation (requires the OpenTelemetry.Instrumentation.GrpcNetClient package)
						//.AddGrpcClientInstrumentation()
						.AddHttpClientInstrumentation()
						.AddSource("Experimental.Microsoft.Extensions.AI")
						.AddSource("Experimental.Microsoft.Extensions.DataIngestion");
				});

			builder.AddOpenTelemetryExporters();

			return builder;
		}

		TBuilder AddOpenTelemetryExporters()
		{
			var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

			if (useOtlpExporter)
			{
				builder.Services.AddOpenTelemetry().UseOtlpExporter();
			}

			// Uncomment the following lines to enable the Azure Monitor exporter (requires the Azure.Monitor.OpenTelemetry.AspNetCore package)
			//if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
			//{
			//    builder.Services.AddOpenTelemetry()
			//       .UseAzureMonitor();
			//}

			return builder;
		}

		public TBuilder AddDefaultHealthChecks()
		{
			builder.Services.AddHealthChecks()
				// Add a default liveness check to ensure app is responsive
				.AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

			return builder;
		}
	}

	/// <summary>
	/// Reactive throttling for named HttpClients hitting a rate-limited API: send at full
	/// speed, and only back off when the server pushes back with a 429, waiting the exact
	/// duration its Retry-After header dictates before resuming. No proactive pacing.
	/// </summary>
	/// <remarks>
	/// Replaces the standard resilience pipeline on these clients: its 30-second
	/// TotalRequestTimeout is too tight once a 429's Retry-After wait plus exponential
	/// backoff stack across attempts. The proactive sliding-window limiter this method
	/// used to install (for Voyage's 3 RPM free tier) is gone now that a paid tier lifts
	/// the per-minute ceiling — its design is preserved in poc/aichatweb/FINDINGS.md §0
	/// for the next time a partner forces proactive pacing.
	/// </remarks>
	public static IServiceCollection AddRetryAfterResilience(
		this IServiceCollection services, params string[] httpClientNames)
	{
		foreach (var clientName in httpClientNames)
		{
			IHttpClientBuilder clientBuilder = services.AddHttpClient(clientName);
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is experimental
			clientBuilder.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
			clientBuilder.AddResilienceHandler("retry-after", resilience =>
			{
				// Outermost: total ceiling generous enough for a few stacked Retry-After waits.
				resilience.AddTimeout(TimeSpan.FromMinutes(2));
				resilience.AddRetry(new HttpRetryStrategyOptions
				{
					MaxRetryAttempts = 5,
					Delay = TimeSpan.FromSeconds(2),
					BackoffType = DelayBackoffType.Exponential,
					UseJitter = true,
					ShouldRetryAfterHeader = true, // 429 → honor the server's Retry-After, then resume
				});
				// Innermost: per-attempt timeout for the actual wire call.
				resilience.AddTimeout(TimeSpan.FromSeconds(60));
			});
		}

		return services;
	}

	public static WebApplication MapDefaultEndpoints(this WebApplication app)
	{
		// Adding health checks endpoints to applications in non-development environments has security implications.
		// See https://aka.ms/dotnet/aspire/healthchecks for details before enabling these endpoints in non-development environments.
		if (app.Environment.IsDevelopment())
		{
			// All health checks must pass for app to be considered ready to accept traffic after starting
			app.MapHealthChecks(HealthEndpointPath);

			// Only health checks tagged with the "live" tag must pass for app to be considered alive
			app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
			{
				Predicate = r => r.Tags.Contains("live")
			});
		}

		return app;
	}
}
