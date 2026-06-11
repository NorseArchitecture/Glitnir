using System.Net;

namespace Voyage.Extensions.AI;

/// <summary>A non-success response from the Voyage API, carrying the status code and raw error body.</summary>
public sealed class VoyageApiException(HttpStatusCode statusCode, string responseBody)
	: Exception($"Voyage API request failed with {(int)statusCode} {statusCode}: {responseBody}")
{
	public HttpStatusCode StatusCode { get; } = statusCode;
	public string ResponseBody { get; } = responseBody;
}
