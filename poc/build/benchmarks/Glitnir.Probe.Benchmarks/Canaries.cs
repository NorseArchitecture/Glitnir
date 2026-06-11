#if CANARY
namespace Glitnir.Probe.Benchmarks;

/// <summary>Proves the law still fires in the benchmarks layer.</summary>
public static class ChainCanary
{
	/// <summary>CA2200 — rethrow destroys the stack (latest-Recommended baseline).</summary>
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
}
#endif
