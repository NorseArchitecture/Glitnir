// Reconnaissance harness for poc/pg19-document-store. Two jobs psql cannot do:
//   Q1 (consume side) — prove raw Npgsql deserializes a jsonb projection into a POCO with
//                       NO EntityFramework in sight. This is the .Server wall redefinition.
//   Q3 — measure shim INSERT (primary) -> visible on the read replica, idle and under load.
// Not platform code: single file, manual arg parse, fail loudly, no silent fallbacks.

using System.Diagnostics;
using System.Text.Json;
using Npgsql;

string primaryConn = GetArg("--primary") ?? throw new ArgumentException("--primary connection string required");
string replicaConn = GetArg("--replica") ?? throw new ArgumentException("--replica connection string required");

await using NpgsqlDataSource primary = NpgsqlDataSource.Create(primaryConn);
await using NpgsqlDataSource replica = NpgsqlDataSource.Create(replicaConn);

await ProveProjectionConsumedWithoutEf(replica);
await MeasureShimToReplicaLatency(primary, replica);

return 0;

// ── Q1 ──────────────────────────────────────────────────────────────────────
// The read path .Server would use: query the replica, get a jsonb projection back as text,
// deserialize with System.Text.Json. The only data library on the call stack is Npgsql.
static async Task ProveProjectionConsumedWithoutEf(NpgsqlDataSource replica)
{
	Console.WriteLine("=== Q1 consume: Npgsql + System.Text.Json, no EF ===");

	const string sql = """
		SELECT jsonb_build_object(
			'id',      doc->'id',
			'status',  status,
			'product', doc->'productCode',
			'premium', doc->'premium'
		)
		FROM policy_view
		WHERE doc @> '{"customerId": "c-1"}'::jsonb
		ORDER BY doc->>'effectiveDate'
		""";

	await using NpgsqlCommand cmd = replica.CreateCommand(sql);
	await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync();

	JsonSerializerOptions opts = new() { PropertyNameCaseInsensitive = true };
	int count = 0;
	while (await reader.ReadAsync())
	{
		string json = reader.GetString(0);                       // jsonb column → text, no mapping
		PolicySummary summary = JsonSerializer.Deserialize<PolicySummary>(json, opts)
			?? throw new InvalidOperationException("projection deserialized to null");
		Console.WriteLine($"  {summary.Id} status={summary.Status} product={summary.Product} " +
			$"premium={summary.Premium.Amount:0.00} {summary.Premium.Currency}");
		count++;
	}

	if (count == 0)
		throw new InvalidOperationException("no rows on the replica — is it streaming? (run 03 / check pg_stat_replication)");
	Console.WriteLine($"  -> {count} rows projected and deserialized with no EF on the stack");
	Console.WriteLine();
}

// ── Q3 ──────────────────────────────────────────────────────────────────────
static async Task MeasureShimToReplicaLatency(NpgsqlDataSource primary, NpgsqlDataSource replica)
{
	Console.WriteLine("=== Q3 lag: shim INSERT (primary) -> visible on replica ===");

	double[] idle = await SampleLatencies(primary, replica, samples: 50, backgroundWriters: 0);
	Report("idle", idle);

	double[] loaded = await SampleLatencies(primary, replica, samples: 50, backgroundWriters: 4);
	Report("under write load (4 writers)", loaded);
	Console.WriteLine();
}

static async Task<double[]> SampleLatencies(NpgsqlDataSource primary, NpgsqlDataSource replica, int samples, int backgroundWriters)
{
	using CancellationTokenSource load = new();
	List<Task> writers = [];
	for (int w = 0; w < backgroundWriters; w++)
		writers.Add(HammerInserts(primary, load.Token));

	double[] results = new double[samples];
	for (int i = 0; i < samples; i++)
		results[i] = await MeasureOne(primary, replica);

	await load.CancelAsync();
	try { await Task.WhenAll(writers); } catch (OperationCanceledException) { /* expected */ }
	return results;
}

static async Task<double> MeasureOne(NpgsqlDataSource primary, NpgsqlDataSource replica)
{
	Guid id = Guid.NewGuid();
	await Insert(primary, id);

	Stopwatch sw = Stopwatch.StartNew();
	TimeSpan timeout = TimeSpan.FromSeconds(10);
	while (true)
	{
		await using NpgsqlCommand probe = replica.CreateCommand("SELECT 1 FROM policy_view WHERE id = $1");
		probe.Parameters.AddWithValue(id);
		object? hit = await probe.ExecuteScalarAsync();
		if (hit is not null) return sw.Elapsed.TotalMilliseconds;

		if (sw.Elapsed > timeout)
			throw new TimeoutException($"row {id} not visible on replica after {timeout.TotalSeconds}s — replication stalled");
		// busy poll: localhost streaming lag is sub-ms..low-ms; a Task.Delay floor would swamp the signal
	}
}

static async Task HammerInserts(NpgsqlDataSource primary, CancellationToken ct)
{
	while (!ct.IsCancellationRequested)
		await Insert(primary, Guid.NewGuid());
}

static async Task Insert(NpgsqlDataSource primary, Guid id)
{
	await using NpgsqlCommand cmd = primary.CreateCommand(
		"INSERT INTO policy_view (id, dedup_key, status, doc) VALUES ($1, $2, 'Pending', $3::jsonb)");
	cmd.Parameters.AddWithValue(id);
	cmd.Parameters.AddWithValue(Guid.NewGuid());
	cmd.Parameters.AddWithValue($$"""{"id":"{{id}}","shim":true}""");
	await cmd.ExecuteNonQueryAsync();
}

static void Report(string label, double[] ms)
{
	Array.Sort(ms);
	double Pct(double p) => ms[(int)Math.Min(ms.Length - 1, Math.Round(p / 100.0 * (ms.Length - 1)))];
	Console.WriteLine($"  {label,-28} n={ms.Length} " +
		$"min={ms[0]:0.00}ms p50={Pct(50):0.00}ms p95={Pct(95):0.00}ms max={ms[^1]:0.00}ms");
}

static string? GetArg(string name)
{
	string[] argv = Environment.GetCommandLineArgs();
	for (int i = 0; i < argv.Length - 1; i++)
		if (argv[i] == name) return argv[i + 1];
	return null;
}

// Records mirror the projected shape. No "Dto" suffix — the role is the name.
record PolicySummary(string Id, string Status, string Product, Premium Premium);
record Premium(decimal Amount, string Currency);
