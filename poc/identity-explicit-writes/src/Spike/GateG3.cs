using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Spike;

/// <summary>
///     G3 — does the fragment FK (<c>user_credentials.id → users.id</c>) carry <c>ON DELETE CASCADE</c>, read from the
///     catalog, both in the hand DDL and in EF's own generated create script? The child FKs are read alongside.
/// </summary>
static partial class Program
{
	const string ScratchDatabase = "identity_spike_ef";

	static async Task GateG3Async(SpikeProvider provider, string artifacts, CancellationToken cancellationToken = default)
	{
		var gate = $"G3.{provider.Name}";

		await using (var connection = await provider.OpenAsync(cancellationToken: cancellationToken))
			await CheckDeleteActionsAsync(provider, connection, $"{gate}.hand-ddl", cancellationToken);

		await provider.RecreateDatabaseAsync(ScratchDatabase, cancellationToken);
		try
		{
			var script = await File.ReadAllTextAsync(Path.Combine(artifacts, $"ef-create.{provider.Name}.sql"), cancellationToken);
			await using var connection = await provider.OpenAsync(ScratchDatabase, cancellationToken);
			foreach (var batch in provider.Batches(script))
				await ExecuteAsync(connection, batch, cancellationToken);
			await CheckDeleteActionsAsync(provider, connection, $"{gate}.ef-create-script", cancellationToken);
		}
		finally
		{
			await provider.DropDatabaseAsync(ScratchDatabase, cancellationToken);
		}

		// The relational model EF builds the script from: what a migration would emit for the fragment.
		await AssertAsync($"{gate}.relational-model-fragment-fk-cascade", async () =>
		{
			await using var context = provider.Context();
			var model = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
			var credentials = model.FindTable(provider.Store("UserCredentials"), null) ??
				throw new InvalidOperationException("The credentials table is not in the relational model.");
			var foreignKeys = credentials.ForeignKeyConstraints
				.Select(static fk => $"{fk.Name}→{fk.PrincipalTable.Name} OnDelete={fk.OnDeleteAction}")
				.ToArray();
			var cascade = credentials.ForeignKeyConstraints
				.Where(fk => fk.PrincipalTable.Name == provider.Store("Users"))
				.Select(static fk => fk.OnDeleteAction)
				.SingleOrDefault() is ReferentialAction.Cascade;
			return (cascade, string.Join("; ", foreignKeys));
		});
	}

	static async Task CheckDeleteActionsAsync(SpikeProvider provider, DbConnection connection, string prefix, CancellationToken cancellationToken)
	{
		List<(string Child, string Action)> rows = [];
		await using (var command = connection.CreateCommand())
		{
			command.CommandText = provider.PrincipalForeignKeysSql;
			await using var reader = await command.ExecuteReaderAsync(cancellationToken);
			while (await reader.ReadAsync(cancellationToken))
				rows.Add((reader.GetString(0), reader.GetString(1)));
		}

		var catalog = string.Join("; ", rows.Select(static r => $"{r.Child} ON DELETE {r.Action}"));
		var credentials = provider.Store("UserCredentials");
		var fragment = rows
			.Where(r => r.Child == credentials)
			.Select(static r => r.Action)
			.ToArray();
		Check($"{prefix}.fragment-fk-on-delete-cascade", fragment is [var action] && action == provider.CascadeAction, catalog);
		var children = rows
			.Where(r => r.Child != credentials)
			.ToArray();
		Check($"{prefix}.child-fks-on-delete-cascade", children.Length is 3 && children.All(r => r.Action == provider.CascadeAction), catalog);
	}
}
