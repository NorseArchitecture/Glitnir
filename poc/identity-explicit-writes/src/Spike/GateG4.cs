using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace Spike;

/// <summary>
///     G4 — do the provider-native upserts (<c>ON CONFLICT … DO UPDATE</c>, <c>MERGE … WITH (HOLDLOCK)</c>) behave as spec
///     §4.6 claims when two writers race on one key, and is a conflicting passkey enrollment a clean, catchable unique
///     violation? Every writer is on its own connection, inside its own explicit transaction, released by a barrier.
/// </summary>
static partial class Program
{
	const int RaceIterations = 200;

	const string PasskeyJson =
		"""{"PublicKey":"AQID","Name":null,"CreatedAt":"1970-01-01T00:00:00+00:00","SignCount":0,"IsUserVerified":true,"IsBackedUp":false}""";

	static async Task GateG4Async(SpikeProvider provider, CancellationToken cancellationToken = default)
	{
		var gate = $"G4.{provider.Name}";
		var user = await SeedUserAsync(provider, cancellationToken: cancellationToken);

		// Control first: the naive insert-if-absent shows the barrier produces real races.
		var control = await TokenRaceAsync(provider, user.Id, provider.NaiveInsertToken, cancellationToken);
		Info($"{gate}.control-naive-insert-if-absent", control.Detail);

		await AssertAsync($"{gate}.token-upsert-race-no-violation-defined-value", () =>
			TokenRaceAsync(provider, user.Id, provider.UpsertToken, cancellationToken));

		SpikeUser
			owner = await SeedUserAsync(provider, cancellationToken: cancellationToken),
			intruder = await SeedUserAsync(provider, cancellationToken: cancellationToken);
		var credentialId = RandomNumberGenerator.GetBytes(32);

		await AssertAsync($"{gate}.passkey-enrollment-insert", async () =>
		{
			await using var context = provider.Context();
			await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
			var rows = await context.Database.ExecuteSqlAsync(provider.InsertPasskey(credentialId, owner.Id, PasskeyJson), cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			return (rows is 1, $"rows={rows}");
		});

		await AssertAsync($"{gate}.passkey-raw-json-reads-through-ef", async () =>
		{
			await using var context = provider.Context();
			var passkey = await context.Passkeys.SingleAsync(p => p.CredentialId == credentialId, cancellationToken);
			return (passkey is { Data: { SignCount: 0, PublicKey: [1, 2, 3], IsUserVerified: true } } && passkey.UserId == owner.Id,
				$"SignCount={passkey.Data.SignCount} CreatedAt={passkey.Data.CreatedAt:O}");
		});

		await AssertAsync($"{gate}.passkey-conflicting-enrollment-clean-unique-violation", async () =>
		{
			await using var context = provider.Context();
			await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
			string? violation;
			try
			{
				await context.Database.ExecuteSqlAsync(provider.InsertPasskey(credentialId, intruder.Id, PasskeyJson), cancellationToken);
				violation = null;
			}
			catch (DbException exception)
			{
				violation = provider.UniqueViolation(exception) ?? $"not a unique violation: {Describe(exception)}";
			}

			await transaction.RollbackAsync(cancellationToken);
			var usable = await context.Database
				.SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
				.ToListAsync(cancellationToken);
			var owners = await context.Passkeys
				.Where(p => p.CredentialId == credentialId)
				.Select(static p => p.UserId)
				.ToListAsync(cancellationToken);
			var classified = violation is not null && !violation.StartsWith("not a", StringComparison.Ordinal);
			return (classified && usable is [1] && owners is [var kept] && kept == owner.Id,
				$"{violation ?? "no exception"}; caught as DbException; context usable after rollback={usable is [1]}; row still owned by the first enrollee");
		});

		// The same conflict through EF's own SaveChanges, for comparison with the raw statement.
		try
		{
			await using var context = provider.Context();
			context.Passkeys.Add(new() { CredentialId = credentialId, UserId = intruder.Id, Data = PasskeyData() });
			await context.SaveChangesAsync(cancellationToken);
			Info($"{gate}.passkey-conflict-through-savechanges", "no exception");
		}
		catch (DbUpdateException exception)
		{
			Info($"{gate}.passkey-conflict-through-savechanges",
				$"{exception.GetType().Name} wrapping {(exception.InnerException is { } inner ? provider.UniqueViolation(inner) ?? Describe(inner) : "nothing")}");
		}

		await AssertAsync($"{gate}.passkey-concurrent-conflicting-enrollment-one-wins", async () =>
		{
			await using SpikeContext
				first = provider.Context(),
				second = provider.Context();
			await first.Database.OpenConnectionAsync(cancellationToken);
			await second.Database.OpenConnectionAsync(cancellationToken);
			int clean = 0, firstWins = 0;
			List<string> anomalies = [];
			for (var i = 0; i < RaceIterations; i++)
			{
				var id = RandomNumberGenerator.GetBytes(32);
				var outcomes = await RaceAsync(first, provider.InsertPasskey(id, owner.Id, PasskeyJson), second,
					provider.InsertPasskey(id, intruder.Id, PasskeyJson), cancellationToken);
				var winners = outcomes.Count(static o => o is null);
				var violations = outcomes.Count(o => o is not null && provider.UniqueViolation(o) is not null);
				if (winners is 1 && violations is 1)
					clean++;
				else
					anomalies.Add($"#{i}: winners={winners} {string.Join(" | ", outcomes.Where(static o => o is not null).Select(static o => Describe(o!)))}");
				if (outcomes[0] is null)
					firstWins++;
			}

			var total = await CountAsync(provider, "UserPasskeys", "UserId", owner.Id, cancellationToken) +
				await CountAsync(provider, "UserPasskeys", "UserId", intruder.Id, cancellationToken);
			return (clean is RaceIterations && total is RaceIterations + 1,
				$"{clean}/{RaceIterations} iterations exactly one winner and one unique violation; first writer won {firstWins}; passkey rows={total}; {string.Join("; ", anomalies.Take(3))}");
		});
	}

	/// <summary>
	///     Races two writers on one token key <see cref="RaceIterations" /> times, resetting the row before each round, and
	///     reports whether every round ended with no exception and exactly one row holding one of the two values.
	/// </summary>
	static async Task<(bool, string Detail)> TokenRaceAsync(SpikeProvider provider, Guid userId,
		Func<Guid, string, string, string, FormattableString> statement, CancellationToken cancellationToken = default)
	{
		const string LoginProvider = "spike", TokenName = "race";
		await using SpikeContext
			first = provider.Context(),
			second = provider.Context(),
			observer = provider.Context();
		await first.Database.OpenConnectionAsync(cancellationToken);
		await second.Database.OpenConnectionAsync(cancellationToken);
		int errors = 0, undefined = 0, firstWins = 0, secondWins = 0;
		List<string> samples = [];
		for (var i = 0; i < RaceIterations; i++)
		{
			await observer.Database.ExecuteSqlAsync(provider.DeleteToken(userId, LoginProvider, TokenName), cancellationToken);
			string
				a = $"A{i}",
				b = $"B{i}";
			var outcomes = await RaceAsync(first, statement(userId, LoginProvider, TokenName, a), second, statement(userId, LoginProvider, TokenName, b),
				cancellationToken);
			foreach (var outcome in outcomes.Where(static o => o is not null))
			{
				errors++;
				if (samples.Count < 3)
					samples.Add(provider.UniqueViolation(outcome!) ?? Describe(outcome!));
			}

			var values = await observer.Tokens
				.Where(t => t.UserId == userId && t.LoginProvider == LoginProvider && t.Name == TokenName)
				.Select(static t => t.Value)
				.ToListAsync(cancellationToken);
			if (values is [var value] && (value == a || value == b))
			{
				if (value == a)
					firstWins++;
				else
					secondWins++;
			}
			else
			{
				undefined++;
			}
		}

		return (errors is 0 && undefined is 0,
			$"iterations={RaceIterations} exceptions={errors} undefined-final={undefined} final=A {firstWins}/B {secondWins} {string.Join(" | ", samples)}");
	}

	/// <summary>
	///     Opens a transaction on each context, releases both statements at once, and commits each writer that succeeded.
	///     Returns each writer's exception, or <see langword="null" /> for a committed writer.
	/// </summary>
	static async Task<DbException?[]> RaceAsync(SpikeContext first, FormattableString firstSql, SpikeContext second, FormattableString secondSql,
		CancellationToken cancellationToken = default)
	{
		TaskCompletionSource
			go = new(TaskCreationOptions.RunContinuationsAsynchronously),
			firstReady = new(TaskCreationOptions.RunContinuationsAsynchronously),
			secondReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
		Task<DbException?>[] writers =
		[
			WriteAsync(first, firstSql, firstReady, go.Task, cancellationToken),
			WriteAsync(second, secondSql, secondReady, go.Task, cancellationToken),
		];
		await Task.WhenAll(firstReady.Task, secondReady.Task);
		go.SetResult();
		return await Task.WhenAll(writers);
	}

	static async Task<DbException?> WriteAsync(SpikeContext context, FormattableString sql, TaskCompletionSource ready, Task go,
		CancellationToken cancellationToken = default)
	{
		await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
		ready.SetResult();
		await go;
		try
		{
			await context.Database.ExecuteSqlAsync(sql, cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			return null;
		}
		catch (DbException exception)
		{
			await transaction.RollbackAsync(cancellationToken);
			return exception;
		}
	}
}
