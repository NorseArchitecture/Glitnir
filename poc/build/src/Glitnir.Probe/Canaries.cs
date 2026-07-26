#if CANARY
namespace Glitnir.Probe;

/// <summary>One member per rule the law must catch. Compiled only when EnableCanaries=true.</summary>
public static class CanaryNest
{
	/// <summary>CA5394 — insecure randomness (Security, latest-All-only).</summary>
	public static int InsecureRandom() => new Random().Next();

	/// <summary>CA2007 — await without ConfigureAwait (Reliability, latest-All-only).</summary>
	public static async Task<int> MissingConfigureAwait()
	{
		await Task.Delay(1);
		return 1;
	}

	/// <summary>CA2201 — reserved exception type (Usage, latest-All-only).</summary>
	public static void ReservedException() => throw new Exception("canary");

	/// <summary>CA2200 — rethrow destroys the stack (Usage, latest-Recommended baseline).</summary>
	public static void RethrowWrong()
	{
		try
		{
			throw new InvalidOperationException("canary");
		}
		catch (InvalidOperationException ex)
		{
			throw ex;
		}
	}

	/// <summary>CS0219 — assigned, never used (compiler warning, errors via the ratchet).</summary>
	internal static void UnusedLocal()
	{
		int unused = 42;
	}
}

/// <summary>CA1810 — static field init in static ctor instead of inline init (Performance, latest-All-only).</summary>
public sealed class StaticCtorCanary
{
	static StaticCtorCanary()
	{
		Seed = 7;
	}

	internal static readonly int Seed;
}

/// <summary>CS8618 — non-nullable property uninitialized; fires alongside CS0219 unlike CS1591 (Nullable+TWE, compiler warning via the ratchet).</summary>
public sealed class UninitializedNonNullable
{
	/// <summary>Gets or sets the value.</summary>
	public string Value { get; set; }
}
#endif
