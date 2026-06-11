#if CANARY
namespace Glitnir.Probe.Tests;

/// <summary>Proves the law still fires after the second chain hop.</summary>
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
