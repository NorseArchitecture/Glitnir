namespace Spike;

internal static class Program
{
	private static int Main(string[] args) => args.Contains("--self-test", StringComparer.Ordinal)
		? SpikeProbe.RunSelfTest()
		: 0;
}
