using Microsoft.Extensions.DataIngestion;

namespace VoyageEmbeddings.Ingestion.Services;

sealed class DocumentReader(DirectoryInfo rootDirectory) : IngestionDocumentReader
{
	readonly MarkdownReader _markdownReader = new();
	readonly MarkItDownMcpReader _pdfReader = new(mcpServerUri: GetMarkItDownMcpServerUrl());

	public override Task<IngestionDocument> ReadAsync(FileInfo source, string identifier, string? mediaType = null, CancellationToken cancellationToken = default)
	{
		if (Path.IsPathFullyQualified(identifier))
		{
			// Normalize the identifier to its relative path
			identifier = Path.GetRelativePath(rootDirectory.FullName, identifier);
		}

		mediaType = GetCustomMediaType(source) ?? mediaType;
		return base.ReadAsync(source, identifier, mediaType, cancellationToken);
	}

	public override Task<IngestionDocument> ReadAsync(Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default) =>
		mediaType switch
		{
			"application/pdf" => _pdfReader.ReadAsync(source, identifier, mediaType, cancellationToken),
			"text/markdown" => _markdownReader.ReadAsync(source, identifier, mediaType, cancellationToken),
			_ => throw new InvalidOperationException($"Unsupported media type '{mediaType}'"),
		};

	static string? GetCustomMediaType(FileInfo source) =>
		source.Extension switch
		{
			".md" => "text/markdown",
			_ => null
		};

	static Uri GetMarkItDownMcpServerUrl()
	{
		var markItDownMcpUrl = $"{Environment.GetEnvironmentVariable("MARKITDOWN_MCP_URL")}/mcp";
		return new Uri(markItDownMcpUrl);
	}
}
