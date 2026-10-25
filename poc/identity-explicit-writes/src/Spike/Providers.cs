using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace Spike;

/// <summary>
///     Everything a gate needs that differs by provider: connections, contexts, store names, and the raw statements
///     the explicit store would issue through its own helper (spec §4.5, §4.6).
/// </summary>
abstract class SpikeProvider
{
	public abstract string Name { get; }

	/// <summary>Matches a reference to the principal table in captured SQL.</summary>
	public abstract Regex UsersTable { get; }

	/// <summary>Matches a reference to the credentials fragment in captured SQL.</summary>
	public abstract Regex CredentialsTable { get; }

	/// <summary>Lists every FK referencing the principal table as <c>(child table, ON DELETE action)</c> rows.</summary>
	public abstract string PrincipalForeignKeysSql { get; }

	/// <summary>The catalog's spelling of <c>ON DELETE CASCADE</c>.</summary>
	public abstract string CascadeAction { get; }

	public abstract SpikeContext Context(params IInterceptor[] interceptors);

	public abstract SpikeContext RetryingContext(params IInterceptor[] interceptors);

	public abstract DbConnection Connection(string database = Program.Database);

	/// <summary>The provider's own retrying strategy with zero retries: non-retrying, but an <c>ExecutionStrategy</c>.</summary>
	public abstract IExecutionStrategy ZeroRetryStrategy(DbContext context);

	public abstract string Store(string pascalName);

	public abstract string CountSql(string pascalTable, string pascalColumn, Guid id);

	public abstract IEnumerable<string> Batches(string script);

	public abstract Task RecreateDatabaseAsync(string database, CancellationToken cancellationToken = default);

	public abstract Task DropDatabaseAsync(string database, CancellationToken cancellationToken = default);

	public abstract FormattableString IncrementAccessFailedCountReturning(Guid id);

	public abstract FormattableString DeleteUser(Guid id);

	public abstract FormattableString UpsertToken(Guid userId, string loginProvider, string name, string value);

	/// <summary>The race-prone control: insert-if-absent without the engine's upsert guarantee.</summary>
	public abstract FormattableString NaiveInsertToken(Guid userId, string loginProvider, string name, string value);

	public abstract FormattableString DeleteToken(Guid userId, string loginProvider, string name);

	public abstract FormattableString InsertPasskey(byte[] credentialId, Guid userId, string data);

	/// <summary>Describes <paramref name="exception" /> if it is this provider's unique-key violation; otherwise <see langword="null" />.</summary>
	public abstract string? UniqueViolation(Exception exception);

	/// <summary>A context on another database (G5/G6 scratch databases); only raw statements are issued through it.</summary>
	public abstract SpikeContext ScratchContext(string database);

	/// <summary>The SHA-256 the engine's <c>claim_value_hash</c> expression computes, recomputed client-side.</summary>
	public abstract byte[] ExpectedClaimHash(string claimValue);

	/// <summary>The spec §4.6 add-claim statement: insert-if-absent on <c>(user_id, claim_type, claim_value_hash)</c>.</summary>
	public abstract FormattableString InsertClaimIfAbsent(Guid userId, string claimType, string claimValue);

	/// <summary>The race-prone control: insert-if-absent without the engine's guarantee.</summary>
	public abstract FormattableString NaiveInsertClaim(Guid userId, string claimType, string claimValue);

	/// <summary>The spec §4.6 replace-claim statement, matched by the hash of the old value.</summary>
	public abstract FormattableString ReplaceClaim(Guid userId, string claimType, string oldValue, string newValue);

	public abstract FormattableString DeleteClaims(Guid userId);

	public abstract FormattableString InsertG6User(Guid userId);

	/// <summary>Every <c>(claim_value, claim_value_hash)</c> pair of a user in the claims history table.</summary>
	public abstract string ClaimHistorySql(Guid userId);

	public async Task<DbConnection> OpenAsync(string database = Program.Database, CancellationToken cancellationToken = default)
	{
		var connection = Connection(database);
		await connection.OpenAsync(cancellationToken);
		return connection;
	}
}

sealed partial class PostgresProvider : SpikeProvider
{
	public override string Name =>
		"postgres";

	public override Regex UsersTable =>
		UsersPattern();

	public override Regex CredentialsTable =>
		CredentialsPattern();

	public override string PrincipalForeignKeysSql =>
		"SELECT conrelid::regclass::text, confdeltype::text FROM pg_constraint WHERE contype = 'f' AND confrelid = 'users'::regclass ORDER BY 1";

	public override string CascadeAction =>
		"c";

	public override SpikeContext Context(params IInterceptor[] interceptors) =>
		new PostgresSpikeContext(ConnectionString(Program.Database), false, interceptors);

	public override SpikeContext RetryingContext(params IInterceptor[] interceptors) =>
		new PostgresSpikeContext(ConnectionString(Program.Database), true, interceptors);

	public override DbConnection Connection(string database = Program.Database) =>
		new NpgsqlConnection(ConnectionString(database));

	public override IExecutionStrategy ZeroRetryStrategy(DbContext context) =>
		new NpgsqlRetryingExecutionStrategy(context, 0);

	public override string Store(string pascalName) =>
		PostgresSpikeContext.Snake(pascalName);

	public override string CountSql(string pascalTable, string pascalColumn, Guid id) =>
		$"SELECT count(*)::int FROM {Store(pascalTable)} WHERE {Store(pascalColumn)} = '{id}'";

	public override IEnumerable<string> Batches(string script) =>
		[script];

	public override async Task RecreateDatabaseAsync(string database, CancellationToken cancellationToken = default)
	{
		await using var server = await OpenAsync("postgres", cancellationToken);
		await Program.ExecuteAsync(server, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)", cancellationToken);
		await Program.ExecuteAsync(server, $"CREATE DATABASE {database}", cancellationToken);
	}

	public override async Task DropDatabaseAsync(string database, CancellationToken cancellationToken = default)
	{
		NpgsqlConnection.ClearAllPools();
		await using var server = await OpenAsync("postgres", cancellationToken);
		await Program.ExecuteAsync(server, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)", cancellationToken);
	}

	public override FormattableString IncrementAccessFailedCountReturning(Guid id) =>
		$"""UPDATE user_credentials SET access_failed_count = access_failed_count + 1 WHERE id = {id} RETURNING access_failed_count AS "Value" """;

	public override FormattableString DeleteUser(Guid id) =>
		$"DELETE FROM users WHERE id = {id}";

	public override FormattableString UpsertToken(Guid userId, string loginProvider, string name, string value) =>
		$"""
		INSERT INTO user_tokens (user_id, login_provider, name, value) VALUES ({userId}, {loginProvider}, {name}, {value})
		ON CONFLICT (user_id, login_provider, name) DO UPDATE SET value = EXCLUDED.value
		""";

	public override FormattableString NaiveInsertToken(Guid userId, string loginProvider, string name, string value) =>
		$"""
		INSERT INTO user_tokens (user_id, login_provider, name, value) SELECT {userId}, {loginProvider}, {name}, {value}
		WHERE NOT EXISTS (SELECT 1 FROM user_tokens WHERE user_id = {userId} AND login_provider = {loginProvider} AND name = {name})
		""";

	public override FormattableString DeleteToken(Guid userId, string loginProvider, string name) =>
		$"DELETE FROM user_tokens WHERE user_id = {userId} AND login_provider = {loginProvider} AND name = {name}";

	public override FormattableString InsertPasskey(byte[] credentialId, Guid userId, string data) =>
		$"INSERT INTO user_passkeys (credential_id, user_id, data) VALUES ({credentialId}, {userId}, CAST({data} AS jsonb))";

	public override string? UniqueViolation(Exception exception) =>
		exception is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres ?
			$"{nameof(PostgresException)} SqlState={postgres.SqlState} Constraint={postgres.ConstraintName}" :
			null;

	public override SpikeContext ScratchContext(string database) =>
		new PostgresSpikeContext(ConnectionString(database));

	public override byte[] ExpectedClaimHash(string claimValue) =>
		SHA256.HashData(Encoding.UTF8.GetBytes(claimValue));

	public override FormattableString InsertClaimIfAbsent(Guid userId, string claimType, string claimValue) =>
		$"""
		INSERT INTO user_claims (user_id, claim_type, claim_value) VALUES ({userId}, {claimType}, {claimValue})
		ON CONFLICT (user_id, claim_type, claim_value_hash) DO NOTHING
		""";

	public override FormattableString NaiveInsertClaim(Guid userId, string claimType, string claimValue) =>
		$"""
		INSERT INTO user_claims (user_id, claim_type, claim_value) SELECT {userId}, {claimType}, {claimValue}
		WHERE NOT EXISTS (SELECT 1 FROM user_claims WHERE user_id = {userId} AND claim_type = {claimType} AND claim_value_hash = utf8_sha256({claimValue}))
		""";

	public override FormattableString ReplaceClaim(Guid userId, string claimType, string oldValue, string newValue) =>
		$"UPDATE user_claims SET claim_value = {newValue} WHERE user_id = {userId} AND claim_type = {claimType} AND claim_value_hash = utf8_sha256({oldValue})";

	public override FormattableString DeleteClaims(Guid userId) =>
		$"DELETE FROM user_claims WHERE user_id = {userId}";

	public override FormattableString InsertG6User(Guid userId) =>
		$"INSERT INTO users (id) VALUES ({userId})";

	public override string ClaimHistorySql(Guid userId) =>
		$"SELECT claim_value, claim_value_hash FROM user_claims_history WHERE user_id = '{userId}' ORDER BY lower(system_period)";

	static string ConnectionString(string database) =>
		$"{Program.PostgresServer};Database={database}";

	[GeneratedRegex(@"\busers\b")]
	private static partial Regex UsersPattern();

	[GeneratedRegex(@"\buser_credentials\b")]
	private static partial Regex CredentialsPattern();
}

sealed partial class SqlServerProvider : SpikeProvider
{
	public override string Name =>
		"sqlserver";

	public override Regex UsersTable =>
		UsersPattern();

	public override Regex CredentialsTable =>
		CredentialsPattern();

	public override string PrincipalForeignKeysSql =>
		"SELECT OBJECT_NAME(parent_object_id), delete_referential_action_desc FROM sys.foreign_keys WHERE referenced_object_id = OBJECT_ID('dbo.Users') ORDER BY 1";

	public override string CascadeAction =>
		"CASCADE";

	public override SpikeContext Context(params IInterceptor[] interceptors) =>
		new SqlServerSpikeContext(ConnectionString(Program.Database), false, interceptors);

	public override SpikeContext RetryingContext(params IInterceptor[] interceptors) =>
		new SqlServerSpikeContext(ConnectionString(Program.Database), true, interceptors);

	public override DbConnection Connection(string database = Program.Database) =>
		new SqlConnection(ConnectionString(database));

	public override IExecutionStrategy ZeroRetryStrategy(DbContext context) =>
		new SqlServerRetryingExecutionStrategy(context, 0);

	public override string Store(string pascalName) =>
		pascalName;

	public override string CountSql(string pascalTable, string pascalColumn, Guid id) =>
		$"SELECT count(*) FROM [dbo].[{pascalTable}] WHERE [{pascalColumn}] = '{id}'";

	public override IEnumerable<string> Batches(string script) =>
		script
			.ReplaceLineEndings("\n")
			.Split("\nGO\n")
			.Where(static b => !string.IsNullOrWhiteSpace(b));

	public override async Task RecreateDatabaseAsync(string database, CancellationToken cancellationToken = default)
	{
		await DropDatabaseAsync(database, cancellationToken);
		await using var server = await OpenAsync("master", cancellationToken);
		await Program.ExecuteAsync(server, $"CREATE DATABASE [{database}]", cancellationToken);
	}

	public override async Task DropDatabaseAsync(string database, CancellationToken cancellationToken = default)
	{
		SqlConnection.ClearAllPools();
		await using var server = await OpenAsync("master", cancellationToken);
		await Program.ExecuteAsync(server,
			$"""
			IF DB_ID('{database}') IS NOT NULL
			BEGIN
				ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
				DROP DATABASE [{database}];
			END;
			""", cancellationToken);
	}

	public override FormattableString IncrementAccessFailedCountReturning(Guid id) =>
		$"UPDATE [UserCredentials] SET [AccessFailedCount] = [AccessFailedCount] + 1 OUTPUT inserted.[AccessFailedCount] AS [Value] WHERE [Id] = {id}";

	public override FormattableString DeleteUser(Guid id) =>
		$"DELETE FROM [Users] WHERE [Id] = {id}";

	public override FormattableString UpsertToken(Guid userId, string loginProvider, string name, string value) =>
		$"""
		MERGE [UserTokens] WITH (HOLDLOCK) AS t
		USING (VALUES ({userId}, {loginProvider}, {name}, {value})) AS s ([UserId], [LoginProvider], [Name], [Value])
		ON t.[UserId] = s.[UserId] AND t.[LoginProvider] = s.[LoginProvider] AND t.[Name] = s.[Name]
		WHEN MATCHED THEN UPDATE SET [Value] = s.[Value]
		WHEN NOT MATCHED THEN INSERT ([UserId], [LoginProvider], [Name], [Value]) VALUES (s.[UserId], s.[LoginProvider], s.[Name], s.[Value]);
		""";

	public override FormattableString NaiveInsertToken(Guid userId, string loginProvider, string name, string value) =>
		$"""
		MERGE [UserTokens] AS t
		USING (VALUES ({userId}, {loginProvider}, {name}, {value})) AS s ([UserId], [LoginProvider], [Name], [Value])
		ON t.[UserId] = s.[UserId] AND t.[LoginProvider] = s.[LoginProvider] AND t.[Name] = s.[Name]
		WHEN MATCHED THEN UPDATE SET [Value] = s.[Value]
		WHEN NOT MATCHED THEN INSERT ([UserId], [LoginProvider], [Name], [Value]) VALUES (s.[UserId], s.[LoginProvider], s.[Name], s.[Value]);
		""";

	public override FormattableString DeleteToken(Guid userId, string loginProvider, string name) =>
		$"DELETE FROM [UserTokens] WHERE [UserId] = {userId} AND [LoginProvider] = {loginProvider} AND [Name] = {name}";

	public override FormattableString InsertPasskey(byte[] credentialId, Guid userId, string data) =>
		$"INSERT INTO [UserPasskeys] ([CredentialId], [UserId], [Data]) VALUES ({credentialId}, {userId}, CAST({data} AS json))";

	public override string? UniqueViolation(Exception exception) =>
		exception is SqlException { Number: 2627 or 2601 } sqlServer ?
			$"{nameof(SqlException)} Number={sqlServer.Number} State={sqlServer.State} Class={sqlServer.Class}" :
			null;

	public override SpikeContext ScratchContext(string database) =>
		new SqlServerSpikeContext(ConnectionString(database));

	public override byte[] ExpectedClaimHash(string claimValue) =>
		SHA256.HashData(Encoding.Unicode.GetBytes(claimValue));

	public override FormattableString InsertClaimIfAbsent(Guid userId, string claimType, string claimValue) =>
		$"""
		MERGE [UserClaims] WITH (HOLDLOCK) AS t
		USING (VALUES ({userId}, {claimType}, CAST({claimValue} AS nvarchar(max)))) AS s ([UserId], [ClaimType], [ClaimValue])
		ON t.[UserId] = s.[UserId] AND t.[ClaimType] = s.[ClaimType] AND t.[ClaimValueHash] = CAST(HASHBYTES('SHA2_256', s.[ClaimValue]) AS binary(32))
		WHEN NOT MATCHED THEN INSERT ([UserId], [ClaimType], [ClaimValue]) VALUES (s.[UserId], s.[ClaimType], s.[ClaimValue]);
		""";

	public override FormattableString NaiveInsertClaim(Guid userId, string claimType, string claimValue) =>
		$"""
		MERGE [UserClaims] AS t
		USING (VALUES ({userId}, {claimType}, CAST({claimValue} AS nvarchar(max)))) AS s ([UserId], [ClaimType], [ClaimValue])
		ON t.[UserId] = s.[UserId] AND t.[ClaimType] = s.[ClaimType] AND t.[ClaimValueHash] = CAST(HASHBYTES('SHA2_256', s.[ClaimValue]) AS binary(32))
		WHEN NOT MATCHED THEN INSERT ([UserId], [ClaimType], [ClaimValue]) VALUES (s.[UserId], s.[ClaimType], s.[ClaimValue]);
		""";

	public override FormattableString ReplaceClaim(Guid userId, string claimType, string oldValue, string newValue) =>
		$"""
		UPDATE [UserClaims] SET [ClaimValue] = {newValue}
		WHERE [UserId] = {userId} AND [ClaimType] = {claimType} AND [ClaimValueHash] = CAST(HASHBYTES('SHA2_256', CAST({oldValue} AS nvarchar(max))) AS binary(32))
		""";

	public override FormattableString DeleteClaims(Guid userId) =>
		$"DELETE FROM [UserClaims] WHERE [UserId] = {userId}";

	public override FormattableString InsertG6User(Guid userId) =>
		$"INSERT INTO [Users] ([Id]) VALUES ({userId})";

	public override string ClaimHistorySql(Guid userId) =>
		$"SELECT [ClaimValue], [ClaimValueHash] FROM [dbo].[UserClaimsHistory] WHERE [UserId] = '{userId}' ORDER BY [SystemPeriodStart]";

	static string ConnectionString(string database) =>
		$"{Program.SqlServerServer};Database={database}";

	[GeneratedRegex(@"\bUsers\b")]
	private static partial Regex UsersPattern();

	[GeneratedRegex(@"\bUserCredentials\b")]
	private static partial Regex CredentialsPattern();
}
