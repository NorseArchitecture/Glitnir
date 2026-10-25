using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Spike;

/// <summary>Records the text of every command EF sends, so a gate can assert which tables a statement touches.</summary>
sealed class SqlCapture : DbCommandInterceptor
{
	readonly List<string> _commands = [];

	public IReadOnlyList<string> Commands =>
		_commands;

	public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) =>
		Record(command, result);

	public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
		InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
		new(Record(command, result));

	public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
		InterceptionResult<DbDataReader> result) =>
		Record(command, result);

	public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
		InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
		new(Record(command, result));

	public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result) =>
		Record(command, result);

	public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
		InterceptionResult<object> result, CancellationToken cancellationToken = default) =>
		new(Record(command, result));

	/// <summary>Returns the captured commands and clears the capture.</summary>
	public string[] Take()
	{
		string[] taken = [.. _commands];
		_commands.Clear();
		return taken;
	}

	T Record<T>(DbCommand command, T result)
	{
		_commands.Add(command.CommandText);
		return result;
	}
}

static partial class Program
{
	/// <summary>Runs one assertion; an exception is a FAIL carrying the exception's type, provider code and message.</summary>
	static async Task AssertAsync(string name, Func<Task<(bool Passed, string Detail)>> body)
	{
		try
		{
			var (passed, detail) = await body();
			Check(name, passed, detail);
		}
		catch (Exception exception) when (exception is DbException or InvalidOperationException or NotSupportedException or TimeoutException)
		{
			Check(name, false, Describe(exception));
		}
	}

	/// <summary>An evidence line run.ps1 does not tally; it lands in the gate log.</summary>
	static void Info(string name, string detail) =>
		Console.WriteLine($"INFO {name}: {detail.ReplaceLineEndings(" ")}");

	static string Describe(Exception exception)
	{
		var described = exception is DbException { SqlState: not null } db ?
			$"{exception.GetType().Name} (SqlState {db.SqlState}): {exception.Message}" :
			$"{exception.GetType().Name}: {exception.Message}";
		return exception.InnerException is { } inner ?
			$"{described} --> {Describe(inner)}" :
			described;
	}

	static SpikeUser NewUser(bool withChildren = false)
	{
		var id = Guid.CreateVersion7();
		var email = $"{id:N}@spike.test";
		return new()
		{
			Id = id,
			UserName = email,
			Email = email,
			LockoutEnabled = true,
			ConcurrencyStamp = Guid.CreateVersion7(),
			SecurityStamp = "STAMP-0",
			NormalizedEmail = email.ToUpperInvariant(),
			NormalizedUserName = email.ToUpperInvariant(),
			Claims = withChildren ?
				[new() { UserId = id, ClaimType = "spike", ClaimValue = "value" }] :
				[],
			Tokens = withChildren ?
				[new() { UserId = id, LoginProvider = "spike", Name = "token", Value = "value" }] :
				[],
			Passkeys = withChildren ?
				[new() { CredentialId = Guid.CreateVersion7().ToByteArray(), UserId = id, Data = PasskeyData() }] :
				[],
		};
	}

	static SpikePasskeyData PasskeyData() =>
		new() { PublicKey = [1, 2, 3], CreatedAt = DateTimeOffset.UnixEpoch, SignCount = 0, IsUserVerified = true };

	/// <summary>Inserts a user through EF's own <c>SaveChangesAsync</c> (both fragments, plus any children).</summary>
	static async Task<SpikeUser> SeedUserAsync(SpikeProvider provider, bool withChildren = false, CancellationToken cancellationToken = default)
	{
		var user = NewUser(withChildren);
		await using var context = provider.Context();
		context.Users.Add(user);
		await context.SaveChangesAsync(cancellationToken);
		return user;
	}

	/// <summary>A full-entity read (both fragments) on a fresh context.</summary>
	static async Task<SpikeUser?> ReadUserAsync(SpikeProvider provider, Guid id, CancellationToken cancellationToken = default)
	{
		await using var context = provider.Context();
		return await context.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
	}

	/// <summary>Counts rows of <paramref name="pascalTable" /> whose <paramref name="pascalColumn" /> is <paramref name="id" />.</summary>
	static async Task<int> CountAsync(SpikeProvider provider, string pascalTable, string pascalColumn, Guid id,
		CancellationToken cancellationToken = default)
	{
		await using var connection = await provider.OpenAsync(cancellationToken: cancellationToken);
		return Convert.ToInt32(await ScalarAsync(connection, provider.CountSql(pascalTable, pascalColumn, id), cancellationToken));
	}

	/// <summary>The rows a user owns in each table, as one comparable string.</summary>
	static async Task<string> FootprintAsync(SpikeProvider provider, Guid id, CancellationToken cancellationToken = default)
	{
		(string Table, string Column)[] tables =
			[("Users", "Id"), ("UserCredentials", "Id"), ("UserClaims", "UserId"), ("UserTokens", "UserId"), ("UserPasskeys", "UserId")];
		List<string> counts = [];
		foreach (var (table, column) in tables)
			counts.Add($"{table}={await CountAsync(provider, table, column, id, cancellationToken)}");
		return string.Join(' ', counts);
	}

	/// <summary>The footprint of a user with no rows anywhere.</summary>
	const string NoFootprint = "Users=0 UserCredentials=0 UserClaims=0 UserTokens=0 UserPasskeys=0";
}
