using System.Text;
using Microsoft.Extensions.Logging;

namespace Glitnir.Probe
{
	/// <summary>One member per style rule the law must catch. In the compilation only when EnableCanaries=true.</summary>
	internal static class StyleCanaryNest
	{
		/// <summary>IDE0007 — explicit built-in type where var is law.</summary>
		internal static int ExplicitWhereVarIsLaw()
		{
			int count = CountThings();
			return count;
		}

		/// <summary>IDE0090 — verbose new T() where var is impossible (field initializer), the
		/// rule's enforceable territory under the all-var buckets (re-ruled 2026-06-06): on locals
		/// the implicit-new analyzer defers to use-var (IDE0007 fires there instead). IDE0008 is
		/// unreachable under the all-var buckets, so no canary can exist for it; the construction
		/// form (`var x = new T();` banned) is YGG analyzer bench territory.</summary>
		internal static readonly LawfulCitizen VerboseCitizen = new LawfulCitizen();

		/// <summary>IDE0305 — fluent .ToList() with an explicit collection target; the law wants [.. spread].</summary>
		internal static IList<int> FluentMaterialization()
		{
			IList<int> values = Enumerable.Range(1, 10).Where(v => v > 5).ToList();
			return values;
		}

		/// <summary>CA1727 + CA2254 + CA1848 — the logging law, all three fronts.</summary>
		internal static void UnlawfulLogging(ILogger logger, int value)
		{
			logger.LogInformation($"interpolated {value}");
			logger.LogInformation("lowercase {placeholder}", value);
		}

		/// <summary>IDE0055 — the space-indented method (formatting law). Indentation below is spaces, deliberately.</summary>
		internal static int SpaceIndented()
		{
        return 7;
		}

		static int CountThings() => 42;
	}

	/// <summary>IDE0040 + IDE1006 — redundant 'private' and an m_-prefixed field.</summary>
	internal sealed class ModifierCanary
	{
		private int m_badName = 7;

		internal int Read() => m_badName;
	}

	/// <summary>
	/// CA1852 canary — an unsealed internal type with no subtypes. CA1852 fires here
	/// because the law sets dotnet_code_quality.CA1852.ignore_internalsvisibleto = true.
	/// This assembly grants InternalsVisibleTo (§2.3), under which CA1852 self-disables by
	/// default (a friend could derive); the option overrides that, so the rule runs and
	/// flags this type. See FINDINGS.md deviation #12.
	/// </summary>
	internal class UnsealedCanary
	{
		internal static string Kind => "unsealed";
	}
}
