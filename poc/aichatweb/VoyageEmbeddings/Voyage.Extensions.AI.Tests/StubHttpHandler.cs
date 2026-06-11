using System.Net;
using System.Text;

namespace Voyage.Extensions.AI.Tests;

/// <summary>Captures outgoing requests and replays canned responses, in order.</summary>
sealed class StubHttpHandler : HttpMessageHandler
{
	readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

	public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

	public void Enqueue(HttpStatusCode status, string body) =>
		_responses.Enqueue((status, body));

	/// <summary>Canned success body for <paramref name="count"/> embeddings of <paramref name="dimensions"/> dims, values = index.</summary>
	public void EnqueueEmbeddings(int count, int dimensions, int totalTokens)
	{
		var data = string.Join(",", Enumerable.Range(0, count).Select(i =>
			"{\"object\":\"embedding\",\"embedding\":[" + string.Join(",", Enumerable.Repeat($"{i}.0", dimensions)) + "],\"index\":" + i + "}"));
		Enqueue(HttpStatusCode.OK,
			"{\"object\":\"list\",\"data\":[" + data + "],\"model\":\"voyage-4\",\"usage\":{\"total_tokens\":" + totalTokens + "}}");
	}

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
		Requests.Add((request, body));
		if (_responses.Count == 0)
			throw new InvalidOperationException("StubHttpHandler: no response enqueued for this request.");
		var (status, responseBody) = _responses.Dequeue();
		return new HttpResponseMessage(status)
		{
			Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
		};
	}
}
