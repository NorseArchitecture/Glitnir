using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Spike;

/// <summary>
///     G1 — can each staged statement shape (spec §4.3–4.5) be issued as <c>ExecuteUpdateAsync</c>/<c>ExecuteDeleteAsync</c>
///     against one fragment of the split entity, inside an explicit transaction under a non-retrying strategy?
/// </summary>
static partial class Program
{
	static async Task GateG1Async(SpikeProvider provider, string artifacts, CancellationToken cancellationToken = default)
	{
		var gate = $"G1.{provider.Name}";
		var log = Path.Combine(artifacts, $"{gate}.sql");
		await File.WriteAllTextAsync(log, $"-- G1 captured SQL, {provider.Name}\n\n", cancellationToken);

		// The users statement: identity columns plus the stamp CAS (spec §4.3 step 1).
		await AssertAsync($"{gate}.users-update-touches-users-only", async () =>
		{
			var user = await SeedUserAsync(provider, cancellationToken: cancellationToken);
			SqlCapture capture = new();
			await using var context = provider.Context(capture);
			var rows = await UpdateUsersAsync(context, user.Id, user.ConcurrencyStamp, "+15550100", Guid.CreateVersion7(), cancellationToken);
			return await OneTableAsync(provider, capture, log, "users-only ExecuteUpdate (stamp CAS)", rows, 1, 1, true, cancellationToken);
		});

		// The credentials statement: PasswordHash + SecurityStamp CAS against the baseline (spec §4.4).
		await AssertAsync($"{gate}.credentials-update-touches-credentials-only", async () =>
		{
			var user = await SeedUserAsync(provider, cancellationToken: cancellationToken);
			SqlCapture capture = new();
			await using var context = provider.Context(capture);
			var rows = await UpdateCredentialsAsync(context, user.Id, null, user.SecurityStamp, [1], "STAMP-1", cancellationToken);
			return await OneTableAsync(provider, capture, log, "credentials-only ExecuteUpdate (CAS, NULL baseline hash)", rows, 1, 1, false,
				cancellationToken);
		});

		await AssertAsync($"{gate}.credentials-cas-null-then-value-baseline-then-stale", async () =>
		{
			var user = await SeedUserAsync(provider, cancellationToken: cancellationToken);
			SqlCapture capture = new();
			await using var context = provider.Context(capture);
			var fromNull = await UpdateCredentialsAsync(context, user.Id, null, "STAMP-0", [1], "STAMP-1", cancellationToken);
			var fromValue = await UpdateCredentialsAsync(context, user.Id, [1], "STAMP-1", [2], "STAMP-2", cancellationToken);
			var stale = await UpdateCredentialsAsync(context, user.Id, [1], "STAMP-1", [3], "STAMP-3", cancellationToken);
			var commands = capture.Take();
			await File.AppendAllTextAsync(log, $"-- credentials CAS: NULL baseline, value baseline, stale baseline\n{string.Join("\n;\n", commands)}\n\n",
				cancellationToken);
			var stored = await ReadUserAsync(provider, user.Id, cancellationToken);
			return (fromNull is 1 && fromValue is 1 && stale is 0 && stored is { PasswordHash: [2], SecurityStamp: "STAMP-2" },
				$"rows null-baseline={fromNull} value-baseline={fromValue} stale={stale}; stored hash=[{string.Join(',', stored?.PasswordHash ?? [])}]");
		});

		// Probe: does any credentials-only predicate avoid the principal join? (The shared key binds to the principal table.)
		{
			var user = await SeedUserAsync(provider, cancellationToken: cancellationToken);
			SqlCapture capture = new();
			await using var context = provider.Context(capture);
			var rows = await context.Users
				.Where(u => u.NormalizedUserName == user.NormalizedUserName)
				.ExecuteUpdateAsync(static s => s.SetProperty(static u => u.SecurityStamp, "STAMP-P"), cancellationToken);
			var sql = string.Join('\n', capture.Take());
			await File.AppendAllTextAsync(log, $"-- probe: credentials-only predicate (no Id) and setter\n{sql}\n\n", cancellationToken);
			Info($"{gate}.probe-credentials-only-predicate-without-id",
				$"rows={rows} references users={provider.UsersTable.IsMatch(sql)} user_credentials={provider.CredentialsTable.IsMatch(sql)}");
		}

		// Both statements inside one explicit transaction under the default (non-retrying) strategy.
		await AssertAsync($"{gate}.transaction-commits-both-statements", async () =>
		{
			var user = await SeedUserAsync(provider, cancellationToken: cancellationToken);
			await using var context = provider.Context();
			var strategy = context.Database.CreateExecutionStrategy();
			var (usersRows, credentialRows) = await strategy.ExecuteAsync(async ct =>
			{
				await using var transaction = await context.Database.BeginTransactionAsync(ct);
				var u = await UpdateUsersAsync(context, user.Id, user.ConcurrencyStamp, "+15550101", Guid.CreateVersion7(), ct);
				var c = await UpdateCredentialsAsync(context, user.Id, null, user.SecurityStamp, [9], "STAMP-9", ct);
				await transaction.CommitAsync(ct);
				return (u, c);
			}, cancellationToken);
			var stored = await ReadUserAsync(provider, user.Id, cancellationToken);
			return (usersRows is 1 && credentialRows is 1 && stored is { PhoneNumber: "+15550101", SecurityStamp: "STAMP-9" },
				$"strategy={strategy.GetType().Name} RetriesOnFailure={strategy.RetriesOnFailure} rows users={usersRows} credentials={credentialRows}");
		});

		await AssertAsync($"{gate}.transaction-rollback-undoes-both-statements", async () =>
		{
			var user = await SeedUserAsync(provider, cancellationToken: cancellationToken);
			await using (var context = provider.Context())
			{
				await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
				await UpdateUsersAsync(context, user.Id, user.ConcurrencyStamp, "+15550102", Guid.CreateVersion7(), cancellationToken);
				await UpdateCredentialsAsync(context, user.Id, null, user.SecurityStamp, [9], "STAMP-9", cancellationToken);
				await transaction.RollbackAsync(cancellationToken);
			}

			var stored = await ReadUserAsync(provider, user.Id, cancellationToken);
			return (stored is { PhoneNumber: null, PasswordHash: null, SecurityStamp: "STAMP-0" } && stored.ConcurrencyStamp == user.ConcurrencyStamp,
				$"after rollback phone={stored?.PhoneNumber ?? "NULL"} stamp={stored?.SecurityStamp}");
		});

		// A context configured with the provider's retrying strategy: what the flush must do to force non-retrying.
		var naive = await RetryingTransactionAsync(provider, null, cancellationToken);
		Info($"{gate}.retrying-context-user-transaction-unwrapped", naive);
		var nonRetrying = await RetryingTransactionAsync(provider, static c => new NonRetryingExecutionStrategy(c), cancellationToken);
		Info($"{gate}.retrying-context-wrapped-in-NonRetryingExecutionStrategy", nonRetrying);
		await AssertAsync($"{gate}.retrying-context-forced-non-retrying-transaction", async () =>
		{
			var result = await RetryingTransactionAsync(provider, provider.ZeroRetryStrategy, cancellationToken);
			return (result.StartsWith("committed", StringComparison.Ordinal), $"provider retrying strategy with maxRetryCount 0: {result}");
		});

		// ExecuteDelete on the principal of a split entity, with children, relying on FK cascade (G3).
		await AssertAsync($"{gate}.executedelete-principal", async () =>
		{
			var user = await SeedUserAsync(provider, true, cancellationToken);
			SqlCapture capture = new();
			await using var context = provider.Context(capture);
			await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
			var rows = await context.Users
				.Where(u => u.Id == user.Id)
				.ExecuteDeleteAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			await File.AppendAllTextAsync(log, $"-- ExecuteDelete on the principal\n{string.Join("\n;\n", capture.Take())}\n\n", cancellationToken);
			var footprint = await FootprintAsync(provider, user.Id, cancellationToken);
			return (rows is 1 && footprint is NoFootprint, $"rows={rows} after: {footprint}");
		});

		await AssertAsync($"{gate}.raw-delete-principal-cascades", async () =>
		{
			var user = await SeedUserAsync(provider, true, cancellationToken);
			var before = await FootprintAsync(provider, user.Id, cancellationToken);
			SqlCapture capture = new();
			await using var context = provider.Context(capture);
			await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
			var rows = await context.Database.ExecuteSqlAsync(provider.DeleteUser(user.Id), cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			await File.AppendAllTextAsync(log, $"-- raw DELETE on the principal\n{string.Join("\n;\n", capture.Take())}\n\n", cancellationToken);
			var after = await FootprintAsync(provider, user.Id, cancellationToken);
			return (rows is 1 && after is NoFootprint, $"rows={rows} before: {before}; after: {after}");
		});

		// The immediate increment (spec §4.5, Task 4.7): one statement in its own explicit transaction, returning the count.
		Info($"{gate}.increment-returning-via-executeupdate",
			"not expressible: ExecuteUpdateAsync returns Task<int> (affected rows) and has no RETURNING/OUTPUT projection");
		await AssertAsync($"{gate}.increment-returning-raw-sql-in-transaction", async () =>
		{
			var user = await SeedUserAsync(provider, cancellationToken: cancellationToken);
			SqlCapture capture = new();
			await using var context = provider.Context(capture);
			await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
			var first = await IncrementReturningAsync(provider, context, user.Id, cancellationToken);
			var second = await IncrementReturningAsync(provider, context, user.Id, cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			var stored = await ReadUserAsync(provider, user.Id, cancellationToken);
			var (passed, detail) = await OneTableAsync(provider, capture, log, "raw +1 RETURNING/OUTPUT (x2, one transaction)", 1, 1, 2, false,
				cancellationToken);
			return (passed && first is 1 && second is 2 && stored?.AccessFailedCount is 2,
				$"returned {first},{second}; stored {stored?.AccessFailedCount}; {detail}");
		});

		await AssertAsync($"{gate}.increment-returning-concurrent-distinct", async () =>
		{
			const int Writers = 16;
			var user = await SeedUserAsync(provider, cancellationToken: cancellationToken);
			var returned = await Task.WhenAll(Enumerable
				.Range(0, Writers)
				.Select(_ => Task.Run(async () =>
				{
					await using var context = provider.Context();
					await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
					var value = await IncrementReturningAsync(provider, context, user.Id, cancellationToken);
					await transaction.CommitAsync(cancellationToken);
					return value;
				}, cancellationToken)));
			var stored = await ReadUserAsync(provider, user.Id, cancellationToken);
			return (returned.Order().SequenceEqual(Enumerable.Range(1, Writers)) && stored?.AccessFailedCount is Writers,
				$"{Writers} concurrent transactions returned {string.Join(',', returned)}; stored {stored?.AccessFailedCount}");
		});

		await AssertAsync($"{gate}.increment-executeupdate-then-reread-in-transaction", async () =>
		{
			var user = await SeedUserAsync(provider, cancellationToken: cancellationToken);
			SqlCapture capture = new();
			await using var context = provider.Context(capture);
			await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
			var rows = await context.Users
				.Where(u => u.Id == user.Id)
				.ExecuteUpdateAsync(s => s.SetProperty(static u => u.AccessFailedCount, static u => u.AccessFailedCount + 1), cancellationToken);
			var update = capture.Take();
			var count = await context.Users
				.Where(u => u.Id == user.Id)
				.Select(static u => u.AccessFailedCount)
				.SingleAsync(cancellationToken);
			var reread = capture.Take();
			await transaction.CommitAsync(cancellationToken);
			await File.AppendAllTextAsync(log, $"-- ExecuteUpdate +1, then re-read in the same transaction\n{string.Join("\n;\n", [.. update, .. reread])}\n\n",
				cancellationToken);
			var updateSql = string.Join('\n', update);
			var oneTable = provider.CredentialsTable.IsMatch(updateSql) && !provider.UsersTable.IsMatch(updateSql);
			var rereadSql = string.Join('\n', reread);
			return (rows is 1 && count is 1 && oneTable,
				$"rows={rows} count={count} update-one-table={oneTable} reread-joins-users={provider.UsersTable.IsMatch(rereadSql)} (two statements, not one)");
		});
	}

	static Task<int> UpdateUsersAsync(SpikeContext context, Guid id, Guid baselineStamp, string phone, Guid newStamp,
		CancellationToken cancellationToken = default) =>
		context.Users
			.Where(u => u.Id == id && u.ConcurrencyStamp == baselineStamp)
			.ExecuteUpdateAsync(s => s
				.SetProperty(static u => u.PhoneNumber, phone)
				.SetProperty(static u => u.ConcurrencyStamp, newStamp), cancellationToken);

	static Task<int> UpdateCredentialsAsync(SpikeContext context, Guid id, byte[]? baselineHash, string baselineStamp, byte[] newHash,
		string newStamp, CancellationToken cancellationToken = default) =>
		context.Users
			.Where(u => u.Id == id && u.PasswordHash == baselineHash && u.SecurityStamp == baselineStamp)
			.ExecuteUpdateAsync(s => s
				.SetProperty(static u => u.PasswordHash, newHash)
				.SetProperty(static u => u.SecurityStamp, newStamp), cancellationToken);

	static async Task<int> IncrementReturningAsync(SpikeProvider provider, SpikeContext context, Guid id, CancellationToken cancellationToken = default)
	{
		// Uncomposed on purpose: Single()/First() would wrap the UPDATE in a subquery, which neither provider accepts.
		var values = await context.Database
			.SqlQuery<int>(provider.IncrementAccessFailedCountReturning(id))
			.ToListAsync(cancellationToken);
		return values.Single();
	}

	/// <summary>
	///     Runs a users + credentials transaction on a context configured with the provider's retrying strategy, optionally
	///     wrapped in <paramref name="wrap" />'s strategy. Returns "committed …" or the exception it raised.
	/// </summary>
	static async Task<string> RetryingTransactionAsync(SpikeProvider provider, Func<SpikeContext, IExecutionStrategy>? wrap,
		CancellationToken cancellationToken = default)
	{
		var user = await SeedUserAsync(provider, cancellationToken: cancellationToken);
		await using var context = provider.RetryingContext();
		var configured = context.Database.CreateExecutionStrategy();
		try
		{
			async Task<int> FlushAsync(CancellationToken ct)
			{
				await using var transaction = await context.Database.BeginTransactionAsync(ct);
				var rows = await UpdateUsersAsync(context, user.Id, user.ConcurrencyStamp, "+15550103", Guid.CreateVersion7(), ct);
				rows += await UpdateCredentialsAsync(context, user.Id, null, user.SecurityStamp, [7], "STAMP-7", ct);
				await transaction.CommitAsync(ct);
				return rows;
			}

			var strategy = wrap?.Invoke(context);
			var rows = strategy is null ?
				await FlushAsync(cancellationToken) :
				await strategy.ExecuteAsync(FlushAsync, cancellationToken);
			var stored = await ReadUserAsync(provider, user.Id, cancellationToken);
			return $"committed rows={rows} stored-stamp={stored?.SecurityStamp} (configured {configured.GetType().Name}, wrapper {strategy?.GetType().Name ?? "none"} RetriesOnFailure={strategy?.RetriesOnFailure})";
		}
		catch (InvalidOperationException exception)
		{
			return $"threw (configured {configured.GetType().Name}) {Describe(exception)}";
		}
	}

	/// <summary>Logs the captured SQL and checks the statement count, affected rows, and that exactly one fragment is referenced.</summary>
	static async Task<(bool, string)> OneTableAsync(SpikeProvider provider, SqlCapture capture, string log, string label, int rows, int expectedRows,
		int expectedStatements, bool users, CancellationToken cancellationToken = default)
	{
		var commands = capture.Take();
		var sql = string.Join("\n;\n", commands);
		await File.AppendAllTextAsync(log, $"-- {label} (rows affected: {rows})\n{sql}\n\n", cancellationToken);
		bool
			touchesUsers = provider.UsersTable.IsMatch(sql),
			touchesCredentials = provider.CredentialsTable.IsMatch(sql);
		var oneTable = users ?
			touchesUsers && !touchesCredentials :
			touchesCredentials && !touchesUsers;
		return (commands.Length == expectedStatements && rows == expectedRows && oneTable,
			$"statements={commands.Length} rows={rows} references users={touchesUsers} user_credentials={touchesCredentials}");
	}
}
