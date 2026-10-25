using Microsoft.EntityFrameworkCore;

namespace Spike;

/// <summary>
///     G2 — does EF work against a principal table whose period columns it does not know about (SQL Server
///     <c>HIDDEN</c> periods), and what does each statement shape do to <c>UsersHistory</c>? PostgreSQL runs the same
///     sequence against the stand-in trigger, for contrast.
/// </summary>
static partial class Program
{
	static async Task GateG2Async(SpikeProvider provider, CancellationToken cancellationToken = default)
	{
		var gate = $"G2.{provider.Name}";
		var sqlServer = provider is SqlServerProvider;
		SpikeUser? user = null;

		Task<int> HistoryAsync() =>
			CountAsync(provider, "UsersHistory", "Id", user!.Id, cancellationToken);

		await AssertAsync($"{gate}.insert-through-savechanges", async () =>
		{
			user = await SeedUserAsync(provider, cancellationToken: cancellationToken);
			var history = await HistoryAsync();
			return (history is 0, $"history rows after insert={history}");
		});
		if (user is null)
			return;

		await AssertAsync($"{gate}.targeted-update-writes-one-history-row", async () =>
		{
			await using var context = provider.Context();
			var rows = await UpdateUsersAsync(context, user.Id, user.ConcurrencyStamp, "+15550200", Guid.CreateVersion7(), cancellationToken);
			var history = await HistoryAsync();
			return (rows is 1 && history is 1, $"rows={rows} history rows={history}");
		});

		await AssertAsync($"{gate}.full-entity-read", async () =>
		{
			var stored = await ReadUserAsync(provider, user.Id, cancellationToken);
			return (stored is { PhoneNumber: "+15550200", SecurityStamp: "STAMP-0" }, $"read phone={stored?.PhoneNumber} stamp={stored?.SecurityStamp}");
		});

		// The engine fact behind spec §4.3: SQL Server versions every UPDATE, even one that changes nothing.
		var expectedAfterEqual = sqlServer ?
			2 :
			1;
		await AssertAsync(sqlServer ?
			$"{gate}.equal-value-update-writes-a-history-row" :
			$"{gate}.equal-value-update-skipped-by-trigger", async () =>
		{
			await using var context = provider.Context();
			var rows = await context.Users
				.Where(u => u.Id == user.Id)
				.ExecuteUpdateAsync(static s => s.SetProperty(static u => u.PhoneNumber, "+15550200"), cancellationToken);
			var history = await HistoryAsync();
			return (rows is 1 && history == expectedAfterEqual, $"rows={rows} history rows={history} (expected {expectedAfterEqual})");
		});

		await AssertAsync($"{gate}.credentials-only-update-writes-no-history", async () =>
		{
			await using var context = provider.Context();
			var rows = await UpdateCredentialsAsync(context, user.Id, null, "STAMP-0", [4], "STAMP-4", cancellationToken);
			var history = await HistoryAsync();
			return (rows is 1 && history == expectedAfterEqual, $"rows={rows} history rows={history} (unchanged from {expectedAfterEqual})");
		});

		await AssertAsync($"{gate}.delete", async () =>
		{
			await using var context = provider.Context();
			var rows = await context.Database.ExecuteSqlAsync(provider.DeleteUser(user.Id), cancellationToken);
			var footprint = await FootprintAsync(provider, user.Id, cancellationToken);
			var history = await HistoryAsync();
			return (rows is 1 && footprint is NoFootprint && history == expectedAfterEqual + 1,
				$"rows={rows} after: {footprint}; history rows={history} (the deleted version is retained)");
		});
	}
}
