// AOT-clean translator spike. The claim under test: an engineer's Expression<Func<TDoc,bool>>
// predicate and Expression<Func<TDoc,TProj>> projection can be turned into jsonb SQL by WALKING
// the expression tree (no Expression.Compile, no Reflection.Emit), with the result materialized by
// System.Text.Json source-gen. If that holds with the AOT analyzers on (and under PublishAot), the
// symmetric lambda surface (§4.10) and a NativeAOT .Server are compatible — which EF cannot offer
// for dynamic, parameter-passed expressions.
//
// Scope: a deliberately tiny translator — equality/comparison predicates, single-member and
// record-constructor projections. That is the bounded contract surface, not a general LINQ provider.

using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;

string conn = GetArg("--primary") ?? throw new ArgumentException("--primary connection string required");

await using NpgsqlDataSource db = NpgsqlDataSource.Create(conn);
await EnsureProbeTable(db);

// the predicate filters on the DOC id (doc->>'id'), so fetch that, not the table's id column
Guid id = await ScalarAsync<Guid>(db, "SELECT (doc->>'id')::uuid FROM aot_probe LIMIT 1");
Console.WriteLine($"target id = {id}\n");

// === Buvy's example, single-member projection: SingleAsync(p => p.Id == id, p => p.Premium) ===
Console.WriteLine("=== single-member projection ===");
decimal premium = await SingleScalarAsync<Probe, decimal>(db, p => p.Id == id, p => p.Premium);
Console.WriteLine($"-> premium = {premium}\n");

// === record projection: SingleAsync(p => p.Id == id, p => new PocViewModel(p.Id, p.Premium, p.ProductCode)) ===
Console.WriteLine("=== record projection ===");
PocViewModel vm = await SingleObjectAsync(db, p => p.Id == id,
	(Expression<Func<Probe, PocViewModel>>)(p => new PocViewModel(p.Id, p.Premium, p.ProductCode)));
Console.WriteLine($"-> {vm}\n");

// === predicate over a string member: count where ProductCode == "WC" ===
Console.WriteLine("=== member predicate ===");
string whereWc = JsonbTranslator.Predicate<Probe>(p => p.ProductCode == "WC", out List<object?> wcParams);
Console.WriteLine($"   WHERE {whereWc}");
long wc = await CountAsync(db, whereWc, wcParams);
Console.WriteLine($"-> {wc} WC rows\n");

Console.WriteLine("translator used no Expression.Compile and no Reflection.Emit — see JsonbTranslator.");
return 0;

// ── repository-shaped helpers ────────────────────────────────────────────────
async Task<TProj> SingleScalarAsync<T, TProj>(NpgsqlDataSource src,
	Expression<Func<T, bool>> predicate, Expression<Func<T, TProj>> projection)
{
	string where = JsonbTranslator.Predicate(predicate, out List<object?> ps);
	string select = JsonbTranslator.ScalarProjection(projection);
	string sql = $"SELECT {select} FROM aot_probe WHERE {where} LIMIT 1";
	Console.WriteLine($"   {sql}");
	await using NpgsqlCommand cmd = src.CreateCommand(sql);
	BindParams(cmd, ps);
	object? v = await cmd.ExecuteScalarAsync();
	return (TProj)Convert.ChangeType(v!, Nullable.GetUnderlyingType(typeof(TProj)) ?? typeof(TProj));
}

async Task<PocViewModel> SingleObjectAsync(NpgsqlDataSource src,
	Expression<Func<Probe, bool>> predicate, Expression<Func<Probe, PocViewModel>> projection)
{
	string where = JsonbTranslator.Predicate(predicate, out List<object?> ps);
	string select = JsonbTranslator.ObjectProjection(projection);   // jsonb_build_object(...)
	string sql = $"SELECT {select} FROM aot_probe WHERE {where} LIMIT 1";
	Console.WriteLine($"   {sql}");
	await using NpgsqlCommand cmd = src.CreateCommand(sql);
	BindParams(cmd, ps);
	string json = (string)(await cmd.ExecuteScalarAsync())!;
	// source-gen deserialize — the AOT-clean materialization path.
	return JsonSerializer.Deserialize(json, ProbeJson.Default.PocViewModel)!;
}

async Task<long> CountAsync(NpgsqlDataSource src, string where, List<object?> ps)
{
	await using NpgsqlCommand cmd = src.CreateCommand($"SELECT count(*) FROM aot_probe WHERE {where}");
	BindParams(cmd, ps);
	return (long)(await cmd.ExecuteScalarAsync())!;
}

static void BindParams(NpgsqlCommand cmd, List<object?> ps)
{
	foreach (object? p in ps) cmd.Parameters.AddWithValue(p ?? DBNull.Value);
}

static async Task<T> ScalarAsync<T>(NpgsqlDataSource src, string sql)
{
	await using NpgsqlCommand cmd = src.CreateCommand(sql);
	return (T)(await cmd.ExecuteScalarAsync())!;
}

static async Task EnsureProbeTable(NpgsqlDataSource src)
{
	await using NpgsqlCommand cmd = src.CreateCommand("""
		CREATE TABLE IF NOT EXISTS aot_probe (id uuid PRIMARY KEY, doc jsonb NOT NULL);
		INSERT INTO aot_probe (id, doc)
		SELECT gen_random_uuid(), jsonb_build_object('id', gen_random_uuid()::text, 'productCode', 'WC', 'premium', 4200.00)
		WHERE NOT EXISTS (SELECT 1 FROM aot_probe);
		INSERT INTO aot_probe (id, doc)
		SELECT gen_random_uuid(), jsonb_build_object('id', gen_random_uuid()::text, 'productCode', 'GL', 'premium', 900.00)
		WHERE (SELECT count(*) FROM aot_probe) < 2;
		""");
	await cmd.ExecuteNonQueryAsync();
}

static string? GetArg(string name)
{
	string[] argv = Environment.GetCommandLineArgs();
	for (int i = 0; i < argv.Length - 1; i++)
		if (argv[i] == name) return argv[i + 1];
	return null;
}

// ── the translator: WALKS the tree, never compiles it ────────────────────────
static class JsonbTranslator
{
	public static string Predicate<T>(Expression<Func<T, bool>> lambda, out List<object?> parameters)
	{
		List<object?> ps = [];
		string sql = Walk(lambda.Body, lambda.Parameters[0], ps);
		parameters = ps;
		return sql;
	}

	public static string ScalarProjection<T, TProj>(Expression<Func<T, TProj>> lambda)
	{
		if (lambda.Body is not MemberExpression m)
			throw new NotSupportedException("scalar projection must be a single member access");
		return DocPath(m, lambda.Parameters[0]);
	}

	public static string ObjectProjection<T, TProj>(Expression<Func<T, TProj>> lambda)
	{
		ParameterExpression p = lambda.Parameters[0];
		if (lambda.Body is not NewExpression n || n.Constructor is null)
			throw new NotSupportedException("object projection must be a constructor call");
		ParameterInfo[] ctor = n.Constructor.GetParameters();
		StringBuilder sb = new("jsonb_build_object(");
		for (int i = 0; i < n.Arguments.Count; i++)
		{
			if (i > 0) sb.Append(", ");
			// key matches the ctor parameter name → matches the record's STJ property name
			sb.Append('\'').Append(ctor[i].Name).Append("', ");
			sb.Append(DocPath((MemberExpression)n.Arguments[i], p));
		}
		return sb.Append(')').ToString();
	}

	// member access on the lambda parameter → a jsonb extraction with a type cast
	static string DocPath(MemberExpression m, ParameterExpression param)
	{
		if (!RootsAt(m, param))
			throw new NotSupportedException($"projection member must be rooted at the parameter: {m}");
		string key = Camel(m.Member.Name);
		string extract = $"doc->>'{key}'";
		Type t = Nullable.GetUnderlyingType(m.Type) ?? m.Type;
		if (t == typeof(string)) return extract;
		if (t == typeof(Guid)) return $"({extract})::uuid";
		if (t == typeof(decimal)) return $"({extract})::numeric";
		if (t == typeof(int) || t == typeof(long)) return $"({extract})::bigint";
		if (t == typeof(bool)) return $"({extract})::boolean";
		return extract;
	}

	static string Walk(Expression e, ParameterExpression param, List<object?> ps)
	{
		switch (e)
		{
			case BinaryExpression { NodeType: ExpressionType.AndAlso } b:
				return $"({Walk(b.Left, param, ps)} AND {Walk(b.Right, param, ps)})";
			case BinaryExpression { NodeType: ExpressionType.OrElse } b:
				return $"({Walk(b.Left, param, ps)} OR {Walk(b.Right, param, ps)})";
			case BinaryExpression b:
				return Comparison(b, param, ps);
			default:
				throw new NotSupportedException($"unsupported predicate node: {e.NodeType}");
		}
	}

	static string Comparison(BinaryExpression b, ParameterExpression param, List<object?> ps)
	{
		// one side is a doc member, the other is a value to parameterize
		(MemberExpression member, Expression value, bool flipped) = RootsAt(b.Left, param)
			? ((MemberExpression)b.Left, b.Right, false)
			: ((MemberExpression)b.Right, b.Left, true);

		string col = DocPath(member, param);
		object? val = Evaluate(value);        // NO Expression.Compile — see Evaluate
		ps.Add(val);
		string op = b.NodeType switch
		{
			ExpressionType.Equal => "=",
			ExpressionType.NotEqual => "<>",
			ExpressionType.GreaterThan => flipped ? "<" : ">",
			ExpressionType.GreaterThanOrEqual => flipped ? "<=" : ">=",
			ExpressionType.LessThan => flipped ? ">" : "<",
			ExpressionType.LessThanOrEqual => flipped ? ">=" : "<=",
			_ => throw new NotSupportedException($"unsupported comparison: {b.NodeType}")
		};
		return $"{col} {op} ${ps.Count}";
	}

	// Evaluate a value-bearing subtree WITHOUT compiling it: constants, and captured locals
	// (closure fields/props read via reflection — an existing, rooted member, never emitted code).
	static object? Evaluate(Expression e) => e switch
	{
		ConstantExpression c => c.Value,
		MemberExpression { Expression: ConstantExpression owner, Member: FieldInfo f } => f.GetValue(owner.Value),
		MemberExpression { Expression: ConstantExpression owner, Member: PropertyInfo p } => p.GetValue(owner.Value),
		MemberExpression { Expression: MemberExpression inner, Member: FieldInfo f } => f.GetValue(Evaluate(inner)),
		UnaryExpression { NodeType: ExpressionType.Convert } u => Evaluate(u.Operand),
		_ => throw new NotSupportedException($"cannot evaluate without compiling: {e.NodeType}")
	};

	static bool RootsAt(Expression e, ParameterExpression param) => e switch
	{
		MemberExpression m => RootsAt(m.Expression!, param),
		ParameterExpression p => p == param,
		_ => false
	};

	static string Camel(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s[1..];
}

// the document POCO the engineer writes lambdas against
sealed class Probe
{
	public Guid Id { get; set; }
	public decimal Premium { get; set; }
	public string ProductCode { get; set; } = null!;
}

// the projection target — flat, strongly typed, no jsonb anything
sealed record PocViewModel(Guid Id, decimal Premium, string ProductCode);

// source-gen JSON context — the AOT-clean materialization path
[JsonSerializable(typeof(PocViewModel))]
sealed partial class ProbeJson : JsonSerializerContext;
