namespace Voyage.Extensions.AI;

/// <summary>
/// Voyage AI <c>input_type</c>. Queries and documents are embedded asymmetrically;
/// declaring which side a generator serves is mandatory — there is no default.
/// </summary>
public enum VoyageInputType
{
	/// <summary>Sentinel. Never valid for a request; construction-time validation rejects it.</summary>
	Unspecified = 0,
	/// <summary>Embed search queries (<c>input_type: "query"</c>).</summary>
	Query = 1,
	/// <summary>Embed corpus documents (<c>input_type: "document"</c>).</summary>
	Document = 2,
}
