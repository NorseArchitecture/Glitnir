namespace Glitnir.Probe;

/// <summary>Inverse canary: IDE0046 bait that must always compile clean — proves the silent tier stays silent.</summary>
/// <remarks>Deliberately public: under omit_if_default + the ratchet, a bare (private) member with
/// no callers is an IDE0051 build error — dead-code detection working as designed (proven live, 2026-06-06).
/// Public is the escalation that declares this surface, not dead code.</remarks>
public static class JudgmentCitizen
{
	/// <summary>An if/else return IDE0046 would collapse to a conditional; the judgment tier leaves it alone.</summary>
	public static string Describe(bool formal)
	{
		if (formal)
		{
			return "Citizen";
		}

		return "baw";
	}
}
