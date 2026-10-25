using Microsoft.Data.SqlClient;

namespace Spike;

/// <summary>
///     G5 — the spec §6.2 lifecycle against a live SQL Server 2025, on hand-issued DDL, one database, one opted-in
///     table with <c>HIDDEN</c> periods: create; add, widen and drop a column with versioning on; disable; enable on a
///     populated table; drop. A history row is written before, and history is read after, every step. Composite
///     batches and wrong orderings are probed inside a transaction that is always rolled back.
/// </summary>
static partial class Program
{
	const string LifecycleDatabase = "identity_spike_g5";

	const string CreatePrincipals =
		"""
		CREATE TABLE [dbo].[Principals] (
			[Id] int NOT NULL,
			[Name] nvarchar(64) NOT NULL,
			[Note] nvarchar(32) NULL,
			[SystemPeriodStart] datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
			[SystemPeriodEnd] datetime2 GENERATED ALWAYS AS ROW END HIDDEN NOT NULL,
			PERIOD FOR SYSTEM_TIME ([SystemPeriodStart], [SystemPeriodEnd]),
			CONSTRAINT [PK_Principals] PRIMARY KEY ([Id])
		)
		WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[PrincipalsHistory]));
		""";

	const string VersioningOff =
		"ALTER TABLE [dbo].[Principals] SET (SYSTEM_VERSIONING = OFF);";

	const string VersioningOn =
		"ALTER TABLE [dbo].[Principals] SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[PrincipalsHistory]));";

	const string DropPeriod =
		"ALTER TABLE [dbo].[Principals] DROP PERIOD FOR SYSTEM_TIME;";

	const string DropPeriodColumns =
		"ALTER TABLE [dbo].[Principals] DROP COLUMN [SystemPeriodStart], [SystemPeriodEnd];";

	const string DropHistory =
		"DROP TABLE [dbo].[PrincipalsHistory];";

	const string DropPrincipals =
		"DROP TABLE [dbo].[Principals];";

	/// <summary>The enable shape (spec §6.2): hidden period columns with named defaults, then the period, in one statement.</summary>
	const string AddPeriod =
		"""
		ALTER TABLE [dbo].[Principals] ADD
			[SystemPeriodStart] datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL
				CONSTRAINT [DF_Principals_SystemPeriodStart] DEFAULT SYSUTCDATETIME(),
			[SystemPeriodEnd] datetime2 GENERATED ALWAYS AS ROW END HIDDEN NOT NULL
				CONSTRAINT [DF_Principals_SystemPeriodEnd] DEFAULT CONVERT(datetime2, '9999-12-31 23:59:59.9999999'),
			PERIOD FOR SYSTEM_TIME ([SystemPeriodStart], [SystemPeriodEnd]);
		""";

	static async Task GateG5Async(SpikeProvider provider, CancellationToken cancellationToken = default)
	{
		if (provider is not SqlServerProvider)
		{
			Info($"G5.{provider.Name}.not-applicable",
				"the PostgreSQL lifecycle is the chassis apparatus (chassis spec §3.3–§3.4), proven by poc/ef-temporal-emission");
			return;
		}

		await provider.RecreateDatabaseAsync(LifecycleDatabase, cancellationToken);
		try
		{
			await using var connection = (SqlConnection)await provider.OpenAsync(LifecycleDatabase, cancellationToken);
			await LifecycleAsync($"G5.{provider.Name}", connection, cancellationToken);
		}
		finally
		{
			await provider.DropDatabaseAsync(LifecycleDatabase, cancellationToken);
		}
	}

	static async Task LifecycleAsync(string gate, SqlConnection connection, CancellationToken cancellationToken)
	{
		var touches = 0;

		// Writes one history row while versioning is on: an UPDATE of a system-versioned row always versions (G2).
		async Task TouchAsync() =>
			await ExecuteAsync(connection, $"UPDATE [dbo].[Principals] SET [Name] = N'p1-{++touches}' WHERE [Id] = 1", cancellationToken);

		await AssertAsync($"{gate}.create", async () =>
		{
			await ExecuteAsync(connection, CreatePrincipals, cancellationToken);
			await ExecuteAsync(connection, "INSERT INTO [dbo].[Principals] ([Id], [Name], [Note]) VALUES (1, N'p1', N'n1'), (2, N'p2', N'n2')",
				cancellationToken);
			await TouchAsync();
			var state = await TemporalStateAsync(connection, cancellationToken);
			var history = await HistoryCountAsync(connection, cancellationToken);
			Info($"{gate}.create.columns",
				$"Principals: {await ColumnsAsync(connection, "Principals", cancellationToken)} | PrincipalsHistory: {await ColumnsAsync(connection, "PrincipalsHistory", cancellationToken)}");
			return (state is "main=2 history=1 periods=1 hidden=2" && history is 1, $"{state}; history rows={history}");
		});

		await AssertAsync($"{gate}.add-nullable-column-versioning-on", async () =>
		{
			await TouchAsync();
			var before = await HistoryCountAsync(connection, cancellationToken);
			await ExecuteAsync(connection, "ALTER TABLE [dbo].[Principals] ADD [Extra] nvarchar(32) NULL;", cancellationToken);
			var after = await HistoryCountAsync(connection, cancellationToken);
			await TouchAsync();
			var nullExtra = await IntAsync(connection, "SELECT count(*) FROM [dbo].[PrincipalsHistory] WHERE [Extra] IS NULL", cancellationToken);
			var state = await TemporalStateAsync(connection, cancellationToken);
			var historyColumns = await ColumnsAsync(connection, "PrincipalsHistory", cancellationToken);
			return (state.StartsWith("main=2 history=1", StringComparison.Ordinal) && after == before && nullExtra == before + 1 &&
				historyColumns.Contains("Extra:nvarchar(64) null", StringComparison.Ordinal),
				$"history rows before={before} after={after}; after one more touch {nullExtra} history rows, all Extra NULL; {state}; history columns: {historyColumns}");
		});

		await AssertAsync($"{gate}.add-not-null-column-with-default-versioning-on", async () =>
		{
			await TouchAsync();
			var before = await HistoryCountAsync(connection, cancellationToken);
			await ExecuteAsync(connection, "ALTER TABLE [dbo].[Principals] ADD [Flag] bit NOT NULL CONSTRAINT [DF_Principals_Flag] DEFAULT CAST(0 AS bit);",
				cancellationToken);
			var after = await HistoryCountAsync(connection, cancellationToken);
			var defaulted = await IntAsync(connection, "SELECT count(*) FROM [dbo].[PrincipalsHistory] WHERE [Flag] = 0", cancellationToken);
			var historyColumns = await ColumnsAsync(connection, "PrincipalsHistory", cancellationToken);
			return (after == before && defaulted == before && historyColumns.Contains("Flag:bit(1)", StringComparison.Ordinal),
				$"history rows before={before} after={after}; existing history rows given the default: {defaulted}; history columns: {historyColumns}");
		});

		// Batching: a column added and referenced in the same batch fails at compile time.
		var sameBatch = await ProbeAsync(connection,
			"ALTER TABLE [dbo].[Principals] ADD [Probe] int NULL; UPDATE [dbo].[Principals] SET [Probe] = 1;", cancellationToken);
		Info($"{gate}.batch.add-column-then-reference-same-batch", sameBatch);

		await AssertAsync($"{gate}.alter-column-widen-propagates-to-history", async () =>
		{
			await TouchAsync();
			var before = await HistoryCountAsync(connection, cancellationToken);
			var historyNameBefore = await ColumnAsync(connection, "PrincipalsHistory", "Name", cancellationToken);
			await ExecuteAsync(connection, "ALTER TABLE [dbo].[Principals] ALTER COLUMN [Name] nvarchar(128) NOT NULL;", cancellationToken);
			var historyNameAfter = await ColumnAsync(connection, "PrincipalsHistory", "Name", cancellationToken);
			var after = await HistoryCountAsync(connection, cancellationToken);
			var intact = await IntAsync(connection, "SELECT count(*) FROM [dbo].[PrincipalsHistory] WHERE [Name] LIKE N'p1%'", cancellationToken);
			await ExecuteAsync(connection, "UPDATE [dbo].[Principals] SET [Name] = REPLICATE(N'w', 100) WHERE [Id] = 1", cancellationToken);
			await TouchAsync();
			var longest = await IntAsync(connection, "SELECT max(len([Name])) FROM [dbo].[PrincipalsHistory]", cancellationToken);
			var state = await TemporalStateAsync(connection, cancellationToken);
			return (historyNameAfter is "Name:nvarchar(256)" && after == before && intact == before && longest is 100 &&
				state.StartsWith("main=2 history=1", StringComparison.Ordinal),
				$"history Name {historyNameBefore} -> {historyNameAfter} with no statement against the history table; history rows before={before} after={after}, old values intact={intact}; a 100-character name versioned into history (max len={longest}); {state}");
		});

		await AssertAsync($"{gate}.drop-column-versioning-on", async () =>
		{
			await TouchAsync();
			var before = await HistoryCountAsync(connection, cancellationToken);
			await ExecuteAsync(connection, "ALTER TABLE [dbo].[Principals] DROP COLUMN [Note];", cancellationToken);
			var after = await HistoryCountAsync(connection, cancellationToken);
			await TouchAsync();
			var afterTouch = await HistoryCountAsync(connection, cancellationToken);
			var historyColumns = await ColumnsAsync(connection, "PrincipalsHistory", cancellationToken);
			return (after == before && afterTouch == before + 1 && !historyColumns.Contains("Note:", StringComparison.Ordinal),
				$"history rows before={before} after={after} after-touch={afterTouch}; history columns: {historyColumns}");
		});

		await AssertAsync($"{gate}.disable", async () =>
		{
			await TouchAsync();
			var before = await HistoryCountAsync(connection, cancellationToken);
			Info($"{gate}.disable.probe.drop-period-while-versioning-on", await ProbeAsync(connection, DropPeriod, cancellationToken));
			Info($"{gate}.disable.probe.one-batch",
				await ProbeAsync(connection, $"{VersioningOff} {DropPeriod} {DropPeriodColumns} {DropHistory}", cancellationToken));

			await ExecuteAsync(connection, VersioningOff, cancellationToken);
			var afterOff = await TemporalStateAsync(connection, cancellationToken);
			var retained = await HistoryCountAsync(connection, cancellationToken);
			var startBefore = await ScalarAsync(connection, "SELECT [SystemPeriodStart] FROM [dbo].[Principals] WHERE [Id] = 1", cancellationToken);
			await TouchAsync();
			var startAfter = await ScalarAsync(connection, "SELECT [SystemPeriodStart] FROM [dbo].[Principals] WHERE [Id] = 1", cancellationToken);
			var afterOffTouch = await HistoryCountAsync(connection, cancellationToken);
			Info($"{gate}.disable.after-versioning-off",
				$"{afterOff}; history rows retained={retained} (before={before}); an UPDATE while off wrote {afterOffTouch - retained} history rows; the row's period start {(Equals(startBefore, startAfter) ? "did not move" : "still moves")} ({startBefore:O} -> {startAfter:O}); columns: {await ColumnsAsync(connection, "Principals", cancellationToken)}");
			Info($"{gate}.disable.probe.drop-period-columns-before-period", await ProbeAsync(connection, DropPeriodColumns, cancellationToken));

			await ExecuteAsync(connection, DropPeriod, cancellationToken);
			Info($"{gate}.disable.after-drop-period",
				$"{await TemporalStateAsync(connection, cancellationToken)}; columns: {await ColumnsAsync(connection, "Principals", cancellationToken)}");
			await ExecuteAsync(connection, DropPeriodColumns, cancellationToken);
			var readBeforeDrop = await HistoryCountAsync(connection, cancellationToken);
			await ExecuteAsync(connection, DropHistory, cancellationToken);
			var final = await TemporalStateAsync(connection, cancellationToken);
			return (retained == before && afterOffTouch == retained && final is "main=0 history=absent periods=0 hidden=0",
				$"order: SET (SYSTEM_VERSIONING = OFF) -> DROP PERIOD FOR SYSTEM_TIME -> DROP COLUMN period columns -> DROP TABLE history; history rows read before the history drop={readBeforeDrop}; final {final}; columns: {await ColumnsAsync(connection, "Principals", cancellationToken)}");
		});

		await AssertAsync($"{gate}.enable-on-populated-table", async () =>
		{
			await ExecuteAsync(connection, "INSERT INTO [dbo].[Principals] ([Id], [Name]) VALUES (3, N'p3')", cancellationToken);
			await TouchAsync();
			Info($"{gate}.enable.probe.period-columns-without-defaults",
				await ProbeAsync(connection,
					"""
					ALTER TABLE [dbo].[Principals] ADD
						[SystemPeriodStart] datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
						[SystemPeriodEnd] datetime2 GENERATED ALWAYS AS ROW END HIDDEN NOT NULL,
						PERIOD FOR SYSTEM_TIME ([SystemPeriodStart], [SystemPeriodEnd]);
					""", cancellationToken));
			Info($"{gate}.enable.probe.one-batch", await ProbeAsync(connection, $"{AddPeriod} {VersioningOn}", cancellationToken));

			var t0 = (DateTime)(await ScalarAsync(connection, "SELECT SYSUTCDATETIME()", cancellationToken))!;
			await ExecuteAsync(connection, AddPeriod, cancellationToken);
			await ExecuteAsync(connection, VersioningOn, cancellationToken);
			var t1 = (DateTime)(await ScalarAsync(connection, "SELECT SYSUTCDATETIME()", cancellationToken))!;
			var state = await TemporalStateAsync(connection, cancellationToken);
			var history = await HistoryCountAsync(connection, cancellationToken);
			DateTime start, end;
			int rows, distinctStarts;
			await using (var command = connection.CreateCommand())
			{
				command.CommandText = "SELECT count(*), count(DISTINCT [SystemPeriodStart]), min([SystemPeriodStart]), max([SystemPeriodEnd]) FROM [dbo].[Principals]";
				await using var reader = await command.ExecuteReaderAsync(cancellationToken);
				await reader.ReadAsync(cancellationToken);
				(rows, distinctStarts, start, end) = (reader.GetInt32(0), reader.GetInt32(1), reader.GetDateTime(2), reader.GetDateTime(3));
			}

			await TouchAsync();
			var historyStart = (DateTime)(await ScalarAsync(connection, "SELECT [SystemPeriodStart] FROM [dbo].[PrincipalsHistory]", cancellationToken))!;
			return (state is "main=2 history=1 periods=1 hidden=2" && history is 0 && distinctStarts is 1 && start >= t0 && start <= t1 &&
				end == new DateTime(9999, 12, 31, 23, 59, 59).AddTicks(9_999_999) && historyStart == start &&
				await HistoryCountAsync(connection, cancellationToken) is 1,
				$"{state}; {rows} existing rows share {distinctStarts} period start {start:O} (enable window {t0:O}..{t1:O}), end {end:O}; history created empty ({history}); the first versioned update closed a history row opened at the enable timestamp ({historyStart:O})");
		});

		// After enable the period columns carry DEFAULT constraints that create never had.
		Info($"{gate}.post-enable.probe.disable-without-dropping-defaults",
			await ProbeAsync(connection, $"{VersioningOff} {DropPeriod} {DropPeriodColumns} {DropHistory}", cancellationToken));
		Info($"{gate}.post-enable.probe.disable-dropping-defaults-first",
			await ProbeAsync(connection,
				$"{VersioningOff} {DropPeriod} ALTER TABLE [dbo].[Principals] DROP CONSTRAINT [DF_Principals_SystemPeriodStart], [DF_Principals_SystemPeriodEnd]; {DropPeriodColumns} {DropHistory}",
				cancellationToken));

		await AssertAsync($"{gate}.drop", async () =>
		{
			await TouchAsync();
			var before = await HistoryCountAsync(connection, cancellationToken);
			Info($"{gate}.drop.probe.drop-table-while-versioning-on", await ProbeAsync(connection, DropPrincipals, cancellationToken));
			Info($"{gate}.drop.probe.drop-history-while-versioning-on", await ProbeAsync(connection, DropHistory, cancellationToken));
			Info($"{gate}.drop.probe.one-batch", await ProbeAsync(connection, $"{VersioningOff} {DropHistory} {DropPrincipals}", cancellationToken));
			Info($"{gate}.drop.probe.table-before-history", await ProbeAsync(connection, $"{VersioningOff} {DropPrincipals} {DropHistory}", cancellationToken));

			await ExecuteAsync(connection, VersioningOff, cancellationToken);
			var retained = await HistoryCountAsync(connection, cancellationToken);
			await ExecuteAsync(connection, DropHistory, cancellationToken);
			await ExecuteAsync(connection, DropPrincipals, cancellationToken);
			var remaining = await IntAsync(connection, "SELECT count(*) FROM sys.tables WHERE name IN (N'Principals', N'PrincipalsHistory')", cancellationToken);
			return (retained == before && before > 0 && remaining is 0,
				$"order: SET (SYSTEM_VERSIONING = OFF) -> DROP TABLE history -> DROP TABLE table; history rows read after versioning off={retained} (before={before}); tables remaining={remaining}");
		});
	}

	/// <summary>
	///     Runs <paramref name="sql" /> as one batch under <c>XACT_ABORT</c> inside a transaction that is always rolled
	///     back, and describes the outcome: <c>OK</c> or the engine error number and message.
	/// </summary>
	static Task<string> ProbeAsync(SqlConnection connection, string sql, CancellationToken cancellationToken) =>
		ProbeBatchesAsync(connection, [sql], cancellationToken);

	/// <summary>As <see cref="ProbeAsync" />, issuing each of <paramref name="batches" /> as its own batch in one transaction.</summary>
	static async Task<string> ProbeBatchesAsync(SqlConnection connection, string[] batches, CancellationToken cancellationToken)
	{
		await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
		var outcome = batches.Length is 1 ?
			"OK (one batch, rolled back)" :
			$"OK ({batches.Length} batches, rolled back)";
		for (var i = 0; i < batches.Length; i++)
			try
			{
				await using SqlCommand command = new($"SET XACT_ABORT ON; {batches[i]}", connection, transaction);
				await command.ExecuteNonQueryAsync(cancellationToken);
			}
			catch (SqlException exception)
			{
				outcome = batches.Length is 1 ?
					$"Msg {exception.Number}: {exception.Message}" :
					$"batch {i + 1}/{batches.Length}: Msg {exception.Number}: {exception.Message}";
				break;
			}

		try
		{
			await transaction.RollbackAsync(cancellationToken);
		}
		catch (InvalidOperationException)
		{
			// XACT_ABORT already rolled the transaction back on the server.
		}

		await ExecuteAsync(connection, "SET XACT_ABORT OFF;", cancellationToken);
		return outcome;
	}

	/// <summary>Temporal state as <c>main=temporal_type history=temporal_type|absent periods=n hidden=n</c>.</summary>
	static async Task<string> TemporalStateAsync(SqlConnection connection, CancellationToken cancellationToken) =>
		(string)(await ScalarAsync(connection,
			"""
			SELECT CONCAT(
				'main=', (SELECT temporal_type FROM sys.tables WHERE name = N'Principals'),
				' history=', ISNULL((SELECT CAST(temporal_type AS varchar(8)) FROM sys.tables WHERE name = N'PrincipalsHistory'), 'absent'),
				' periods=', (SELECT count(*) FROM sys.periods WHERE object_id = OBJECT_ID(N'dbo.Principals')),
				' hidden=', (SELECT count(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Principals') AND is_hidden = 1))
			""", cancellationToken))!;

	/// <summary>History row count, or -1 when the history table does not exist.</summary>
	static async Task<int> HistoryCountAsync(SqlConnection connection, CancellationToken cancellationToken) =>
		await IntAsync(connection, "SELECT CASE WHEN OBJECT_ID(N'dbo.PrincipalsHistory') IS NULL THEN 1 ELSE 0 END", cancellationToken) is 1 ?
			-1 :
			await IntAsync(connection, "SELECT count(*) FROM [dbo].[PrincipalsHistory]", cancellationToken);

	static async Task<int> IntAsync(SqlConnection connection, string sql, CancellationToken cancellationToken) =>
		Convert.ToInt32(await ScalarAsync(connection, sql, cancellationToken));

	/// <summary>Every column of <paramref name="table" /> as <c>name:type(bytes)[ hidden][ rowstart|rowend][ computed][ null]</c>.</summary>
	static async Task<string> ColumnsAsync(SqlConnection connection, string table, CancellationToken cancellationToken) =>
		(await ScalarAsync(connection,
			$"""
			SELECT STRING_AGG(CONCAT(name, ':', TYPE_NAME(user_type_id), '(', max_length, ')',
				IIF(is_hidden = 1, ' hidden', ''),
				CASE generated_always_type WHEN 1 THEN ' rowstart' WHEN 2 THEN ' rowend' ELSE '' END,
				IIF(is_computed = 1, ' computed', ''),
				IIF(is_nullable = 1, ' null', '')), ', ') WITHIN GROUP (ORDER BY column_id)
			FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.{table}')
			""", cancellationToken)) as string ?? "(absent)";

	static async Task<string> ColumnAsync(SqlConnection connection, string table, string column, CancellationToken cancellationToken) =>
		(string)(await ScalarAsync(connection,
			$"SELECT CONCAT(name, ':', TYPE_NAME(user_type_id), '(', max_length, ')') FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.{table}') AND name = N'{column}'",
			cancellationToken))!;
}
