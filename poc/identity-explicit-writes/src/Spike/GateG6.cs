using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Spike;

/// <summary>A claim for G6's EF-native SQL Server probe; the hash is a shadow property, as spec §4.6 and §14 require.</summary>
sealed record G6Claim
{
	public int Id { get; init; }
	public required Guid UserId { get; init; }
	public required string ClaimType { get; init; }
	public required string ClaimValue { get; init; }
}

/// <summary>EF-native <c>IsTemporal()</c> claims with a stored computed <c>ClaimValueHash</c> shadow property.</summary>
/// <remarks>
///     With <paramref name="hashRequired" /> false the <c>byte[]</c> shadow property is nullable, so EF's SQL Server
///     convention filters the unique index (<c>WHERE [ClaimValueHash] IS NOT NULL</c>), and the engine refuses a filter
///     on a computed column. <c>IsRequired()</c> removes the filter.
/// </remarks>
sealed class G6SqlServerContext(string connectionString, bool hashRequired) : DbContext(new DbContextOptionsBuilder()
	.UseSqlServer(connectionString, static o => o.UseCompatibilityLevel(170))
	// Both variants share this type; a fresh internal service provider keeps EF from reusing the first variant's cached model.
	.EnableServiceProviderCaching(false)
	.Options)
{
	protected override void OnModelCreating(ModelBuilder modelBuilder) =>
		modelBuilder.Entity<G6Claim>(b =>
		{
			b.ToTable("UserClaims", static t => t.IsTemporal(static h =>
			{
				h.HasPeriodStart("SystemPeriodStart");
				h.HasPeriodEnd("SystemPeriodEnd");
				h.UseHistoryTable("UserClaimsHistory");
			}));
			b.HasKey(static c => c.Id);
			b.Property(static c => c.ClaimType).HasMaxLength(256);
			b
				.Property<byte[]>("ClaimValueHash")
				.HasColumnType("binary(32)")
				.IsRequired(hashRequired)
				.HasComputedColumnSql("CAST(HASHBYTES('SHA2_256', [ClaimValue]) AS binary(32))", stored: true);
			b
				.HasIndex("UserId", "ClaimType", "ClaimValueHash")
				.IsUnique();
		});
}

/// <summary>
///     G6 — can a stored, provider-computed SHA-256 <c>claim_value_hash</c> sit on temporal <c>user_claims</c> (SQL
///     Server EF-native system versioning; the PostgreSQL chassis apparatus, here a stand-in trigger), with a unique
///     index on <c>(user_id, claim_type, claim_value_hash)</c> serving insert-if-absent under replay and under two
///     concurrent writers? Runs on a scratch database per provider, on hand-issued DDL.
/// </summary>
static partial class Program
{
	const string ClaimsDatabase = "identity_spike_g6";

	const string ClaimsEfDatabase = "identity_spike_g6_ef";

	/// <summary>Non-ASCII on purpose: the hash input encoding differs by provider.</summary>
	const string NonAsciiClaimValue = "é-ü-😀";

	static async Task GateG6Async(SpikeProvider provider, string artifacts, CancellationToken cancellationToken = default)
	{
		var gate = $"G6.{provider.Name}";
		await provider.RecreateDatabaseAsync(ClaimsDatabase, cancellationToken);
		try
		{
			await using (var connection = await provider.OpenAsync(ClaimsDatabase, cancellationToken))
			{
				if (provider is PostgresProvider)
					await AssertAsync($"{gate}.spec-expression-is-a-valid-generation-expression", async () =>
					{
						await ExecuteAsync(connection,
							"CREATE TABLE g6_spec_probe (claim_value text NOT NULL, claim_value_hash bytea GENERATED ALWAYS AS (sha256(convert_to(claim_value, 'UTF8'))) STORED)",
							cancellationToken);
						return (true, "sha256(convert_to(claim_value, 'UTF8')) accepted");
					});

				foreach (var batch in provider.Batches(ReadDdl($"g6.{provider.Name}.sql")))
					await ExecuteAsync(connection, batch, cancellationToken);
				Check($"{gate}.ddl-applied", true, "");
				await AssertAsync($"{gate}.hash-stored-on-main-plain-in-history", () => HashColumnsAsync(provider, connection, cancellationToken));
			}

			await ClaimSetSemanticsAsync(provider, gate, cancellationToken);
			if (provider is PostgresProvider)
				await PostgresGeneratedColumnProbesAsync(provider, gate, cancellationToken);
			else
				await SqlServerComputedColumnProbesAsync(provider, gate, artifacts, cancellationToken);
		}
		finally
		{
			await provider.DropDatabaseAsync(ClaimsDatabase, cancellationToken);
		}
	}

	static async Task<(bool, string)> HashColumnsAsync(SpikeProvider provider, DbConnection connection, CancellationToken cancellationToken)
	{
		const string PostgresColumns =
			"""
			SELECT string_agg(attrelid::regclass || '.' || attname || ':' || format_type(atttypid, atttypmod) || ' ' ||
				CASE attgenerated WHEN 's' THEN 'stored-generated' WHEN 'v' THEN 'virtual-generated' ELSE 'plain' END ||
				CASE WHEN attnotnull THEN ' not-null' ELSE ' null' END, ', ' ORDER BY attrelid::regclass::text)
			FROM pg_attribute WHERE attname = 'claim_value_hash' AND attrelid IN ('user_claims'::regclass, 'user_claims_history'::regclass)
			""";
		const string PostgresIndex =
			"SELECT pg_get_indexdef('ix_user_claims_user_id_claim_type_claim_value_hash'::regclass)";
		const string SqlServerColumns =
			"""
			SELECT STRING_AGG(CONCAT(OBJECT_NAME(c.object_id), '.', c.name, ':', TYPE_NAME(c.user_type_id), '(', c.max_length, ') ',
				IIF(c.is_computed = 1, CONCAT('computed', IIF(cc.is_persisted = 1, '-persisted', '')), 'plain'),
				IIF(c.is_nullable = 1, ' null', ' not-null')), ', ') WITHIN GROUP (ORDER BY OBJECT_NAME(c.object_id))
			FROM sys.columns AS c LEFT JOIN sys.computed_columns AS cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
			WHERE c.name = N'ClaimValueHash' AND c.object_id IN (OBJECT_ID(N'dbo.UserClaims'), OBJECT_ID(N'dbo.UserClaimsHistory'))
			""";
		const string SqlServerIndex =
			"""
			SELECT CONCAT(i.name, ' unique=', i.is_unique, ' temporal_type=', t.temporal_type, ' history=', OBJECT_NAME(t.history_table_id))
			FROM sys.indexes AS i JOIN sys.tables AS t ON t.object_id = i.object_id
			WHERE i.name = N'IX_UserClaims_UserId_ClaimType_ClaimValueHash'
			""";
		var postgres = provider is PostgresProvider;
		var columns = (string)(await ScalarAsync(connection, postgres ? PostgresColumns : SqlServerColumns, cancellationToken))!;
		var index = (string)(await ScalarAsync(connection, postgres ? PostgresIndex : SqlServerIndex, cancellationToken))!;
		var expected = postgres ?
			"user_claims.claim_value_hash:bytea stored-generated not-null, user_claims_history.claim_value_hash:bytea plain null" :
			"UserClaims.ClaimValueHash:binary(32) computed-persisted not-null, UserClaimsHistory.ClaimValueHash:binary(32) plain not-null";
		var unique = postgres ?
			index.StartsWith("CREATE UNIQUE INDEX", StringComparison.Ordinal) :
			index.Contains("unique=1 temporal_type=2 history=UserClaimsHistory", StringComparison.Ordinal);
		return (columns == expected && unique, $"{columns}; {index}");
	}

	static async Task ClaimSetSemanticsAsync(SpikeProvider provider, string gate, CancellationToken cancellationToken)
	{
		await using var observer = provider.ScratchContext(ClaimsDatabase);
		Guid
			replayUser = Guid.CreateVersion7(),
			raceUser = Guid.CreateVersion7(),
			replaceUser = Guid.CreateVersion7();
		foreach (var user in (Guid[])[replayUser, raceUser, replaceUser])
			await observer.Database.ExecuteSqlAsync(provider.InsertG6User(user), cancellationToken);

		async Task<int> ClaimCountAsync(Guid userId)
		{
			await using var connection = await provider.OpenAsync(ClaimsDatabase, cancellationToken);
			return Convert.ToInt32(await ScalarAsync(connection, provider.CountSql("UserClaims", "UserId", userId), cancellationToken));
		}

		await AssertAsync($"{gate}.insert-if-absent-idempotent-under-replay", async () =>
		{
			List<int> rows = [];
			for (var i = 0; i < 3; i++)
				rows.Add(await observer.Database.ExecuteSqlAsync(provider.InsertClaimIfAbsent(replayUser, "role", NonAsciiClaimValue), cancellationToken));
			var count = await ClaimCountAsync(replayUser);
			return (rows is [1, 0, 0] && count is 1, $"rows affected per replay=[{string.Join(", ", rows)}]; claim rows={count}");
		});

		await AssertAsync($"{gate}.hash-matches-client-sha256", async () =>
		{
			var stored = await observer.Database
				.SqlQueryRaw<byte[]>(provider is PostgresProvider ?
					$"SELECT claim_value_hash AS \"Value\" FROM user_claims WHERE user_id = '{replayUser}'" :
					$"SELECT [ClaimValueHash] AS [Value] FROM [UserClaims] WHERE [UserId] = '{replayUser}'")
				.ToListAsync(cancellationToken);
			var expected = provider.ExpectedClaimHash(NonAsciiClaimValue);
			Info($"{gate}.hash-of-non-ascii-value", $"{Convert.ToHexString(stored.Single())} for \"{NonAsciiClaimValue}\"");
			return (stored is [var hash] && hash.SequenceEqual(expected),
				$"stored hash = SHA-256 over the value's {(provider is PostgresProvider ? "UTF-8" : "UTF-16LE (nvarchar)")} bytes");
		});

		var control = await ClaimRaceAsync(provider, raceUser, provider.NaiveInsertClaim, cancellationToken);
		Info($"{gate}.control-naive-insert-if-absent", control.Detail);
		await AssertAsync($"{gate}.insert-if-absent-race-one-row-no-violation", () =>
			ClaimRaceAsync(provider, raceUser, provider.InsertClaimIfAbsent, cancellationToken));

		await AssertAsync($"{gate}.replace-claim-by-hash-and-history-carries-hash", async () =>
		{
			const string OldValue = "old-é", NewValue = "new-ü";
			await observer.Database.ExecuteSqlAsync(provider.InsertClaimIfAbsent(replaceUser, "role", OldValue), cancellationToken);
			var replaced = await observer.Database.ExecuteSqlAsync(provider.ReplaceClaim(replaceUser, "role", OldValue, NewValue), cancellationToken);
			var replayed = await observer.Database.ExecuteSqlAsync(provider.ReplaceClaim(replaceUser, "role", OldValue, NewValue), cancellationToken);
			await observer.Database.ExecuteSqlAsync(provider.DeleteClaims(replaceUser), cancellationToken);
			List<(string Value, byte[] Hash)> history = [];
			await using (var connection = await provider.OpenAsync(ClaimsDatabase, cancellationToken))
			await using (var command = connection.CreateCommand())
			{
				command.CommandText = provider.ClaimHistorySql(replaceUser);
				await using var reader = await command.ExecuteReaderAsync(cancellationToken);
				while (await reader.ReadAsync(cancellationToken))
					history.Add((reader.GetString(0), (byte[])reader.GetValue(1)));
			}

			var carried = history is [var first, var second] && first.Value == OldValue && first.Hash.SequenceEqual(provider.ExpectedClaimHash(OldValue)) &&
				second.Value == NewValue && second.Hash.SequenceEqual(provider.ExpectedClaimHash(NewValue));
			return (replaced is 1 && replayed is 0 && carried,
				$"replace rows={replaced}, replayed replace rows={replayed} (new value present: a replay); history after replace+delete: {string.Join("; ", history.Select(static h => $"{h.Value}={Convert.ToHexString(h.Hash)[..12]}…"))} — each version's materialized hash matches its value");
		});
	}

	/// <summary>
	///     Races two writers inserting one identical claim <see cref="RaceIterations" /> times, deleting the user's claims
	///     before each round; reports whether every round ended with no exception and exactly one row.
	/// </summary>
	static async Task<(bool, string Detail)> ClaimRaceAsync(SpikeProvider provider, Guid userId,
		Func<Guid, string, string, FormattableString> statement, CancellationToken cancellationToken = default)
	{
		await using SpikeContext
			first = provider.ScratchContext(ClaimsDatabase),
			second = provider.ScratchContext(ClaimsDatabase),
			observer = provider.ScratchContext(ClaimsDatabase);
		await first.Database.OpenConnectionAsync(cancellationToken);
		await second.Database.OpenConnectionAsync(cancellationToken);
		await observer.Database.OpenConnectionAsync(cancellationToken);
		await using var count = observer.Database.GetDbConnection().CreateCommand();
		count.CommandText = provider.CountSql("UserClaims", "UserId", userId);
		int errors = 0, notOneRow = 0;
		List<string> samples = [];
		for (var i = 0; i < RaceIterations; i++)
		{
			await observer.Database.ExecuteSqlAsync(provider.DeleteClaims(userId), cancellationToken);
			var value = $"race-{i}-{NonAsciiClaimValue}";
			var outcomes = await RaceAsync(first, statement(userId, "role", value), second, statement(userId, "role", value), cancellationToken);
			foreach (var outcome in outcomes.Where(static o => o is not null))
			{
				errors++;
				if (samples.Count < 3)
					samples.Add(provider.UniqueViolation(outcome!) ?? Describe(outcome!));
			}

			if (Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken)) is not 1)
				notOneRow++;
		}

		return (errors is 0 && notOneRow is 0,
			$"iterations={RaceIterations} exceptions={errors} rounds-not-exactly-one-row={notOneRow} {string.Join(" | ", samples)}");
	}

	static async Task PostgresGeneratedColumnProbesAsync(SpikeProvider provider, string gate, CancellationToken cancellationToken)
	{
		await using var connection = (NpgsqlConnection)await provider.OpenAsync(ClaimsDatabase, cancellationToken);
		var userId = Guid.CreateVersion7();
		await ExecuteAsync(connection, $"INSERT INTO users (id) VALUES ('{userId}')", cancellationToken);
		await ExecuteAsync(connection, $"INSERT INTO user_claims (user_id, claim_type, claim_value) VALUES ('{userId}', 'role', 'probe')", cancellationToken);
		var historySql = $"SELECT count(*)::int FROM user_claims_history WHERE user_id = '{userId}'";
		var noOpUpdate = $"UPDATE user_claims SET claim_value = claim_value WHERE user_id = '{userId}'";

		await AssertAsync($"{gate}.main-refuses-explicit-hash", async () =>
		{
			try
			{
				await ExecuteAsync(connection,
					$"INSERT INTO user_claims (user_id, claim_type, claim_value, claim_value_hash) VALUES ('{userId}', 'role', 'explicit', '\\x00'::bytea)",
					cancellationToken);
				return (false, "an explicit hash was accepted");
			}
			catch (PostgresException exception) when (exception.SqlState is "428C9")
			{
				return (true, $"{nameof(PostgresException)} SqlState={exception.SqlState}: {exception.MessageText} — the chassis and the store never name the column in an INSERT/UPDATE into user_claims");
			}
		});

		// What a BEFORE trigger sees of the generated column (rolled back).
		List<string> notices = [];
		void OnNotice(object? sender, NpgsqlNoticeEventArgs e) =>
			notices.Add(e.Notice.MessageText);
		connection.Notice += OnNotice;
		await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
		{
			await ExecuteAsync(connection,
				"""
				CREATE FUNCTION g6_probe() RETURNS trigger LANGUAGE plpgsql AS $probe$
				BEGIN
					RAISE NOTICE '% %: NEW.claim_value_hash is %, OLD.claim_value_hash is %', TG_WHEN, TG_OP,
						CASE WHEN NEW.claim_value_hash IS NULL THEN 'NULL' ELSE 'set' END,
						CASE WHEN OLD.claim_value_hash IS NULL THEN 'NULL' ELSE 'set' END;
					RETURN NEW;
				END $probe$;
				CREATE TRIGGER a_g6_probe_before BEFORE UPDATE ON user_claims FOR EACH ROW EXECUTE FUNCTION g6_probe();
				CREATE TRIGGER z_g6_probe_after AFTER UPDATE ON user_claims FOR EACH ROW EXECUTE FUNCTION g6_probe();
				""", cancellationToken);
			await ExecuteAsync(connection, noOpUpdate, cancellationToken);
			await transaction.RollbackAsync(cancellationToken);
		}

		connection.Notice -= OnNotice;
		Info($"{gate}.trigger-view-of-generated-column-on-no-op-update", string.Join("; ", notices));

		await AssertAsync($"{gate}.no-op-update-writes-no-history-when-hash-excluded-from-comparison", async () =>
		{
			var before = Convert.ToInt32(await ScalarAsync(connection, historySql, cancellationToken));
			await ExecuteAsync(connection, noOpUpdate, cancellationToken);
			var after = Convert.ToInt32(await ScalarAsync(connection, historySql, cancellationToken));
			return (before is 0 && after is 0, $"history rows before={before} after={after}");
		});

		// Control: the same no-op update with the hash in the comparison (function replaced and rolled back).
		await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
		{
			await ExecuteAsync(connection,
				"""
				CREATE OR REPLACE FUNCTION user_claims_versioning() RETURNS trigger
				LANGUAGE plpgsql AS $spike$
				DECLARE ts timestamptz;
				BEGIN
					IF TG_OP = 'INSERT' THEN
						NEW.system_period := tstzrange(clock_timestamp(), 'infinity');
						RETURN NEW;
					END IF;
					IF TG_OP = 'UPDATE' AND ROW(OLD.id, OLD.user_id, OLD.claim_type, OLD.claim_value, OLD.claim_value_hash)
						IS NOT DISTINCT FROM ROW(NEW.id, NEW.user_id, NEW.claim_type, NEW.claim_value, NEW.claim_value_hash) THEN
						NEW.system_period := OLD.system_period;
						RETURN NEW;
					END IF;
					ts := greatest(clock_timestamp(), lower(OLD.system_period) + interval '1 microsecond');
					INSERT INTO user_claims_history (id, user_id, claim_type, claim_value, claim_value_hash, system_period)
						VALUES (OLD.id, OLD.user_id, OLD.claim_type, OLD.claim_value, OLD.claim_value_hash, tstzrange(lower(OLD.system_period), ts));
					IF TG_OP = 'UPDATE' THEN
						NEW.system_period := tstzrange(ts, 'infinity');
						RETURN NEW;
					END IF;
					RETURN OLD;
				END $spike$;
				""", cancellationToken);
			await ExecuteAsync(connection, noOpUpdate, cancellationToken);
			var minted = Convert.ToInt32(await ScalarAsync(connection, historySql, cancellationToken));
			await transaction.RollbackAsync(cancellationToken);
			Info($"{gate}.control-hash-in-no-op-comparison", $"the same no-op UPDATE wrote {minted} history row(s) when the comparison included claim_value_hash");
		}

		// The extension alternative to the wrapper function.
		try
		{
			await ExecuteAsync(connection, "CREATE EXTENSION IF NOT EXISTS pgcrypto", cancellationToken);
			var volatility = await ScalarAsync(connection,
				"""
				SELECT string_agg(p.oid::regprocedure::text || '=' || p.provolatile::text, ', ' ORDER BY p.oid::regprocedure::text)
				FROM pg_proc AS p WHERE p.proname IN ('convert_to', 'sha256', 'digest', 'utf8_sha256')
				""", cancellationToken);
			var same = await ScalarAsync(connection, $"SELECT digest('{NonAsciiClaimValue}'::text, 'sha256') = utf8_sha256('{NonAsciiClaimValue}')", cancellationToken);
			Info($"{gate}.immutability-and-pgcrypto-alternative",
				$"provolatile (i=immutable, s=stable): {volatility}; pgcrypto digest(text, 'sha256') equals utf8_sha256 on a UTF8 database: {same}");
		}
		catch (PostgresException exception)
		{
			Info($"{gate}.immutability-and-pgcrypto-alternative", Describe(exception));
		}
	}

	static async Task SqlServerComputedColumnProbesAsync(SpikeProvider provider, string gate, string artifacts, CancellationToken cancellationToken)
	{
		await using (var connection = (SqlConnection)await provider.OpenAsync(ClaimsDatabase, cancellationToken))
		{
			// Bare HASHBYTES: column type and the index warning (rolled back).
			List<string> messages = [];
			void OnInfo(object sender, SqlInfoMessageEventArgs e) =>
				messages.Add(e.Message);
			connection.InfoMessage += OnInfo;
			await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken))
			{
				await using SqlCommand command = new(
					"""
					CREATE TABLE [dbo].[G6Uncast] ([Id] int NOT NULL PRIMARY KEY, [UserId] uniqueidentifier NOT NULL, [ClaimType] nvarchar(256) NOT NULL,
						[ClaimValue] nvarchar(max) NOT NULL, [ClaimValueHash] AS HASHBYTES('SHA2_256', [ClaimValue]) PERSISTED);
					CREATE UNIQUE INDEX [IX_G6Uncast] ON [dbo].[G6Uncast] ([UserId], [ClaimType], [ClaimValueHash]);
					SELECT CONCAT(TYPE_NAME(user_type_id), '(', max_length, ')') FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.G6Uncast') AND name = N'ClaimValueHash';
					""", connection, transaction);
				var type = await command.ExecuteScalarAsync(cancellationToken);
				await transaction.RollbackAsync(cancellationToken);
				Info($"{gate}.bare-hashbytes-column", $"type {type}; index creation: {(messages.Count is 0 ? "no warning" : string.Join(" ", messages))}");
			}

			connection.InfoMessage -= OnInfo;

			Info($"{gate}.probe.history-table-declaring-the-computed-column",
				await ProbeAsync(connection,
					"""
					CREATE TABLE [dbo].[G6Pre] ([Id] int NOT NULL PRIMARY KEY, [ClaimValue] nvarchar(max) NOT NULL,
						[ClaimValueHash] AS CAST(HASHBYTES('SHA2_256', [ClaimValue]) AS binary(32)) PERSISTED,
						[SystemPeriodStart] datetime2 GENERATED ALWAYS AS ROW START NOT NULL, [SystemPeriodEnd] datetime2 GENERATED ALWAYS AS ROW END NOT NULL,
						PERIOD FOR SYSTEM_TIME ([SystemPeriodStart], [SystemPeriodEnd]));
					CREATE TABLE [dbo].[G6PreHistory] ([Id] int NOT NULL, [ClaimValue] nvarchar(max) NOT NULL,
						[ClaimValueHash] AS CAST(HASHBYTES('SHA2_256', [ClaimValue]) AS binary(32)) PERSISTED,
						[SystemPeriodStart] datetime2 NOT NULL, [SystemPeriodEnd] datetime2 NOT NULL);
					ALTER TABLE [dbo].[G6Pre] SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[G6PreHistory]));
					""", cancellationToken));
			Info($"{gate}.probe.add-computed-column-versioning-on",
				await ProbeAsync(connection,
					"ALTER TABLE [dbo].[UserClaims] ADD [Hash2] AS CAST(HASHBYTES('SHA2_256', [ClaimValue]) AS binary(32)) PERSISTED;", cancellationToken));
			string[] addComputed =
			[
				"ALTER TABLE [dbo].[UserClaims] SET (SYSTEM_VERSIONING = OFF);",
				"ALTER TABLE [dbo].[UserClaims] ADD [Hash2] AS CAST(HASHBYTES('SHA2_256', [ClaimValue]) AS binary(32)) PERSISTED;",
				"ALTER TABLE [dbo].[UserClaimsHistory] ADD [Hash2] binary(32) NULL;",
				"ALTER TABLE [dbo].[UserClaims] SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[UserClaimsHistory], DATA_CONSISTENCY_CHECK = ON));",
			];
			Info($"{gate}.probe.add-computed-column-off-history-plain-on.one-batch",
				await ProbeAsync(connection, string.Join(' ', addComputed), cancellationToken));
			Info($"{gate}.probe.add-computed-column-off-history-plain-on.separate-batches",
				await ProbeBatchesAsync(connection, addComputed, cancellationToken));

			var userId = Guid.CreateVersion7();
			await ExecuteAsync(connection, $"INSERT INTO [dbo].[Users] ([Id]) VALUES ('{userId}')", cancellationToken);
			await ExecuteAsync(connection, "SET QUOTED_IDENTIFIER OFF;", cancellationToken);
			try
			{
				await ExecuteAsync(connection, $"INSERT INTO [dbo].[UserClaims] ([UserId], [ClaimType], [ClaimValue]) VALUES ('{userId}', N'role', N'qi')",
					cancellationToken);
				Info($"{gate}.quoted-identifier-off-write", "accepted");
			}
			catch (SqlException exception)
			{
				Info($"{gate}.quoted-identifier-off-write", $"Msg {exception.Number}: {exception.Message}");
			}
			finally
			{
				await ExecuteAsync(connection, "SET QUOTED_IDENTIFIER ON;", cancellationToken);
			}

			await ExecuteAsync(connection,
				$"INSERT INTO [dbo].[UserClaims] ([UserId], [ClaimType], [ClaimValue]) VALUES ('{userId}', N'role', N'Admin'), ('{userId}', N'role', N'admin')",
				cancellationToken);
			var equal = await ScalarAsync(connection,
				$"SELECT count(*) FROM [dbo].[UserClaims] WHERE [UserId] = '{userId}' AND [ClaimValue] = N'ADMIN'", cancellationToken);
			var collation = await ScalarAsync(connection, "SELECT CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128))", cancellationToken);
			Info($"{gate}.set-is-binary-exact",
				$"'Admin' and 'admin' are two claims under the hash index; ClaimValue = N'ADMIN' matches {equal} of them under {collation}");
			var narrow = await ScalarAsync(connection, $"SELECT CONVERT(varchar(64), HASHBYTES('SHA2_256', '{NonAsciiClaimValue}'), 2)", cancellationToken);
			Info($"{gate}.hash-input-type-matters", $"HASHBYTES over a varchar literal of the same text: {narrow} (differs from the nvarchar hash)");
		}

		try
		{
			var (_, detail) = await EfNativeClaimsAsync(provider, artifacts, false, cancellationToken);
			Info($"{gate}.ef-native-nullable-shadow-hash", detail);
		}
		catch (SqlException exception)
		{
			Info($"{gate}.ef-native-nullable-shadow-hash",
				$"Msg {exception.Number}: {exception.Message} — EF filtered the unique index on the nullable shadow property (artifacts/G6.ef-create.nullable.sqlserver.sql)");
		}

		await AssertAsync($"{gate}.ef-native-temporal-computed-hash-required", () =>
			EfNativeClaimsAsync(provider, artifacts, true, cancellationToken));
	}

	/// <summary>Applies EF's own create script for the claims model and round-trips a claim through temporal LINQ.</summary>
	static async Task<(bool, string)> EfNativeClaimsAsync(SpikeProvider provider, string artifacts, bool hashRequired, CancellationToken cancellationToken)
	{
		await provider.RecreateDatabaseAsync(ClaimsEfDatabase, cancellationToken);
		try
		{
			var connectionString = $"{Program.SqlServerServer};Database={ClaimsEfDatabase}";
			await using var context = new G6SqlServerContext(connectionString, hashRequired);
			var script = context.Database.GenerateCreateScript();
			await File.WriteAllTextAsync(Path.Combine(artifacts, $"G6.ef-create.{(hashRequired ? "required" : "nullable")}.sqlserver.sql"), script, cancellationToken);
			string columns;
			await using (var connection = (SqlConnection)await provider.OpenAsync(ClaimsEfDatabase, cancellationToken))
			{
				foreach (var batch in provider.Batches(script))
					await ExecuteAsync(connection, batch, cancellationToken);
				columns = (string)(await ScalarAsync(connection,
					"""
					SELECT STRING_AGG(CONCAT(OBJECT_NAME(object_id), '.', name, ':', TYPE_NAME(user_type_id), '(', max_length, ')', IIF(is_computed = 1, ' computed', ' plain')), ', ')
					FROM sys.columns WHERE name = N'ClaimValueHash'
					""", cancellationToken))!;
			}

			var userId = Guid.CreateVersion7();
			context.Add(new G6Claim { UserId = userId, ClaimType = "role", ClaimValue = "old-é" });
			await context.SaveChangesAsync(cancellationToken);
			await context
				.Set<G6Claim>()
				.Where(c => c.UserId == userId)
				.ExecuteUpdateAsync(static s => s.SetProperty(static c => c.ClaimValue, "new-ü"), cancellationToken);
			var versions = await context
				.Set<G6Claim>()
				.TemporalAll()
				.Where(c => c.UserId == userId)
				.OrderBy(static c => EF.Property<DateTime>(c, "SystemPeriodStart"))
				.Select(static c => new { c.ClaimValue, Hash = EF.Property<byte[]>(c, "ClaimValueHash") })
				.ToListAsync(cancellationToken);
			var matched = versions.Count is 2 && versions.All(v => v.Hash.SequenceEqual(provider.ExpectedClaimHash(v.ClaimValue)));
			return (matched && columns.Contains("UserClaimsHistory.ClaimValueHash:binary(32) plain", StringComparison.Ordinal),
				$"EF GenerateCreateScript applied (artifacts/G6.ef-create.{(hashRequired ? "required" : "nullable")}.sqlserver.sql); {columns}; SaveChanges insert + ExecuteUpdate; TemporalAll returned {versions.Count} versions, each hash matching its value");
		}
		finally
		{
			await provider.DropDatabaseAsync(ClaimsEfDatabase, cancellationToken);
		}
	}
}
