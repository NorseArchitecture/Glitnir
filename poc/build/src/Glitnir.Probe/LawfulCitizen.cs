namespace Glitnir.Probe;

/// <summary>Proves the law tolerates lawful code: documented public surface, internal detail.</summary>
public sealed class LawfulCitizen
{
	/// <summary>Gets the realm this citizen answers to.</summary>
	public string Realm { get; } = InternalRealm;

	internal static string InternalRealm => "src";
}
