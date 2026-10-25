using System.Data.Common;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Spike;

/// <summary>
///     Spike entry point. Every assertion prints one <c>PASS name</c> or <c>FAIL name: detail</c> line; run.ps1
///     tallies them. Commands: <c>versions</c>, <c>apply-ddl postgres|sqlserver artifactsDir</c>, <c>gate G1..G6</c>.
/// </summary>
static partial class Program
{
	internal const string Database = "identity_spike";

	internal const string PostgresServer = "Host=localhost;Port=54339;Username=postgres;Password=spike";

	internal const string SqlServerServer = "Server=localhost,14333;User Id=sa;Password=Spike!Passw0rd;TrustServerCertificate=True";

	static int _failures;

	static async Task<int> Main(string[] args)
	{
		await (args switch
		{
			["versions"] => VersionsAsync(),
			["apply-ddl", "postgres", var artifacts] => ApplyPostgresAsync(artifacts),
			["apply-ddl", "sqlserver", var artifacts] => ApplySqlServerAsync(artifacts),
			["gate", var gate, var artifacts] => GateAsync(gate, artifacts),
			_ => throw new ArgumentException($"Unrecognized arguments: {string.Join(' ', args)}"),
		});
		return _failures is 0 ?
			0 :
			1;
	}

	static async Task VersionsAsync(CancellationToken cancellationToken = default)
	{
		await using NpgsqlConnection postgres = new($"{PostgresServer};Database=postgres");
		await postgres.OpenAsync(cancellationToken);
		var postgresVersion = await ScalarAsync(postgres, "SELECT current_setting('server_version_num')::int", cancellationToken);
		await using SqlConnection sqlServer = new($"{SqlServerServer};Database=master");
		await sqlServer.OpenAsync(cancellationToken);
		var sqlServerVersion = await ScalarAsync(sqlServer, "SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS int)", cancellationToken);
		var sqlServerBuild = await ScalarAsync(sqlServer, "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(64))", cancellationToken);
		Console.WriteLine($"postgres.server_version_num={postgresVersion}");
		Console.WriteLine($"sqlserver.product_major_version={sqlServerVersion}");
		Console.WriteLine($"sqlserver.product_version={sqlServerBuild}");
	}

	static async Task ApplyPostgresAsync(string artifacts, CancellationToken cancellationToken = default)
	{
		await using (NpgsqlConnection server = new($"{PostgresServer};Database=postgres"))
		{
			await server.OpenAsync(cancellationToken);
			await ExecuteAsync(server, $"DROP DATABASE IF EXISTS {Database} WITH (FORCE)", cancellationToken);
			await ExecuteAsync(server, $"CREATE DATABASE {Database}", cancellationToken);
		}

		var connectionString = $"{PostgresServer};Database={Database}";
		await using (NpgsqlConnection connection = new(connectionString))
		{
			await connection.OpenAsync(cancellationToken);
			await ExecuteAsync(connection, ReadDdl("postgres.sql"), cancellationToken);
			Check("postgres.ddl-applied", true, "");
			await CheckScalarAsync(connection, "postgres.users-trigger",
				"SELECT count(*) FROM pg_trigger WHERE tgrelid = 'users'::regclass AND tgname = 'users_versioning'", 1L, cancellationToken);
			await CheckScalarAsync(connection, "postgres.users-history-table",
				"SELECT count(*) FROM pg_class WHERE relname = 'users_history' AND relkind = 'r'", 1L, cancellationToken);
			await CheckScalarAsync(connection, "postgres.credentials-email-check",
				"SELECT count(*) FROM pg_constraint WHERE conname = 'ck_user_credentials_email_is_user_name' AND contype = 'c'", 1L, cancellationToken);
			await CheckScalarAsync(connection, "postgres.passkey-data-jsonb",
				"SELECT data_type FROM information_schema.columns WHERE table_name = 'user_passkeys' AND column_name = 'data'", "jsonb", cancellationToken);
		}

		await using PostgresSpikeContext context = new(connectionString);
		await CheckModelBindsAsync("postgres", context, artifacts, cancellationToken);
	}

	static async Task ApplySqlServerAsync(string artifacts, CancellationToken cancellationToken = default)
	{
		await using (SqlConnection server = new($"{SqlServerServer};Database=master"))
		{
			await server.OpenAsync(cancellationToken);
			await ExecuteAsync(server,
				$"""
				IF DB_ID('{Database}') IS NOT NULL
				BEGIN
					ALTER DATABASE [{Database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
					DROP DATABASE [{Database}];
				END;
				CREATE DATABASE [{Database}];
				""", cancellationToken);
		}

		var connectionString = $"{SqlServerServer};Database={Database}";
		await using (SqlConnection connection = new(connectionString))
		{
			await connection.OpenAsync(cancellationToken);
			foreach (var batch in ReadDdl("sqlserver.sql").Split("\nGO\n").Where(static b => !string.IsNullOrWhiteSpace(b)))
				await ExecuteAsync(connection, batch, cancellationToken);
			Check("sqlserver.ddl-applied", true, "");
			await CheckScalarAsync(connection, "sqlserver.compatibility-level",
				$"SELECT CAST(compatibility_level AS int) FROM sys.databases WHERE name = '{Database}'", 170, cancellationToken);
			await CheckScalarAsync(connection, "sqlserver.users-system-versioned",
				"SELECT CAST(temporal_type AS int) FROM sys.tables WHERE name = 'Users'", 2, cancellationToken);
			await CheckScalarAsync(connection, "sqlserver.users-history-table",
				"SELECT CAST(temporal_type AS int) FROM sys.tables WHERE name = 'UsersHistory'", 1, cancellationToken);
			await CheckScalarAsync(connection, "sqlserver.credentials-not-temporal",
				"SELECT CAST(temporal_type AS int) FROM sys.tables WHERE name = 'UserCredentials'", 0, cancellationToken);
			await CheckScalarAsync(connection, "sqlserver.users-period",
				"SELECT count(*) FROM sys.periods WHERE object_id = OBJECT_ID('dbo.Users')", 1, cancellationToken);
			await CheckScalarAsync(connection, "sqlserver.users-period-columns-hidden",
				"SELECT count(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND is_hidden = 1", 2, cancellationToken);
			await CheckScalarAsync(connection, "sqlserver.credentials-email-check",
				"SELECT count(*) FROM sys.check_constraints WHERE name = 'CK_UserCredentials_EmailIsUserName'", 1, cancellationToken);
			await CheckScalarAsync(connection, "sqlserver.passkey-data-native-json",
				"SELECT TYPE_NAME(user_type_id) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.UserPasskeys') AND name = 'Data'", "json", cancellationToken);
		}

		await using SqlServerSpikeContext context = new(connectionString);
		await CheckModelBindsAsync("sqlserver", context, artifacts, cancellationToken);
	}

	/// <summary>
	///     Proves the EF model and the hand-written DDL agree: a full-entity read of every set names every mapped
	///     column. Also writes EF's own create script for comparison (G3 reads the FK it would generate).
	/// </summary>
	static async Task CheckModelBindsAsync(string provider, SpikeContext context, string artifacts, CancellationToken cancellationToken)
	{
		await File.WriteAllTextAsync(Path.Combine(artifacts, $"ef-create.{provider}.sql"), context.Database.GenerateCreateScript(), cancellationToken);
		try
		{
			_ = await context.Users.ToListAsync(cancellationToken);
			_ = await context.Claims.ToListAsync(cancellationToken);
			_ = await context.Tokens.ToListAsync(cancellationToken);
			_ = await context.Passkeys.ToListAsync(cancellationToken);
			Check($"{provider}.model-binds-to-ddl", true, "");
		}
		catch (DbException exception)
		{
			Check($"{provider}.model-binds-to-ddl", false, exception.Message);
		}
	}

	/// <summary>Runs one gate against both providers (G5 is SQL Server only and says so for PostgreSQL).</summary>
	static async Task GateAsync(string gate, string artifacts, CancellationToken cancellationToken = default)
	{
		SpikeProvider[] providers = [new PostgresProvider(), new SqlServerProvider()];
		foreach (var provider in providers)
			await (gate switch
			{
				"G1" => GateG1Async(provider, artifacts, cancellationToken),
				"G2" => GateG2Async(provider, cancellationToken),
				"G3" => GateG3Async(provider, artifacts, cancellationToken),
				"G4" => GateG4Async(provider, cancellationToken),
				"G5" => GateG5Async(provider, cancellationToken),
				"G6" => GateG6Async(provider, artifacts, cancellationToken),
				_ => throw new ArgumentException($"Unknown gate {gate}"),
			});
	}

	static string ReadDdl(string name)
	{
		using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Ddl.{name}") ??
			throw new InvalidOperationException($"Embedded DDL {name} is missing.");
		using StreamReader reader = new(stream);
		return reader.ReadToEnd().ReplaceLineEndings("\n");
	}

	internal static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = sql;
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	internal static async Task<object?> ScalarAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = sql;
		return await command.ExecuteScalarAsync(cancellationToken);
	}

	static async Task CheckScalarAsync(DbConnection connection, string name, string sql, object expected, CancellationToken cancellationToken)
	{
		var actual = await ScalarAsync(connection, sql, cancellationToken);
		Check(name, Equals(actual, expected), $"expected {expected}, got {actual ?? "NULL"}");
	}

	static void Check(string name, bool passed, string detail)
	{
		detail = detail.ReplaceLineEndings(" ");
		if (passed)
		{
			Console.WriteLine(detail.Length is 0 ?
				$"PASS {name}" :
				$"PASS {name}: {detail}");
			return;
		}

		_failures++;
		Console.WriteLine($"FAIL {name}: {detail}");
	}
}
