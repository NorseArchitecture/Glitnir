using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations;

namespace Spike;

public sealed class SpikeMigrationsSqlGenerator(
	MigrationsSqlGeneratorDependencies dependencies,
	INpgsqlSingletonOptions options) : NpgsqlMigrationsSqlGenerator(dependencies, options)
{
	protected override void Generate(CreateTableOperation operation, IModel? model, MigrationCommandListBuilder builder, bool terminate = true)
	{
		Log(operation, operation.Name, operation.Schema, model, null);
		base.Generate(operation, model, builder, terminate);
		if (IsTemporal(operation))
		{
			builder.AppendLine("CREATE TABLE IF NOT EXISTS \"widgets_temporal_spike_apparatus\" (\"id\" integer);");
			builder.EndCommand();
		}
	}

	protected override void Generate(AddColumnOperation operation, IModel? model, MigrationCommandListBuilder builder, bool terminate = true)
	{
		Log(operation, operation.Table, operation.Schema, model, null);
		base.Generate(operation, model, builder, terminate);
	}

	protected override void Generate(RenameColumnOperation operation, IModel? model, MigrationCommandListBuilder builder)
	{
		Log(operation, operation.Table, operation.Schema, model, null);
		base.Generate(operation, model, builder);
	}

	protected override void Generate(DropColumnOperation operation, IModel? model, MigrationCommandListBuilder builder, bool terminate = true)
	{
		Log(operation, operation.Table, operation.Schema, model, null);
		base.Generate(operation, model, builder, terminate);
	}

	protected override void Generate(AlterColumnOperation operation, IModel? model, MigrationCommandListBuilder builder)
	{
		Log(operation, operation.Table, operation.Schema, model, null);
		base.Generate(operation, model, builder);
	}

	protected override void Generate(AlterTableOperation operation, IModel? model, MigrationCommandListBuilder builder)
	{
		Log(operation, operation.Name, operation.Schema, model, operation.OldTable);
		base.Generate(operation, model, builder);
	}

	protected override void Generate(DropTableOperation operation, IModel? model, MigrationCommandListBuilder builder, bool terminate = true)
	{
		Log(operation, operation.Name, operation.Schema, model, null);
		base.Generate(operation, model, builder, terminate);
	}

	private static bool IsTemporal(MigrationOperation operation) =>
		operation.FindAnnotation(SpikeProbe.TemporalAnnotation)?.Value as bool? == true;

	private static void Log(MigrationOperation operation, string table, string? schema, IModel? model, TableOperation? oldTable)
	{
		var annotations = string.Join(", ", operation.GetAnnotations().Select(annotation => $"{annotation.Name}={annotation.Value}"));
		var oldAnnotations = oldTable is null
			? "<none>"
			: string.Join(", ", oldTable.GetAnnotations().Select(annotation => $"{annotation.Name}={annotation.Value}"));
		var targetTemporal = SpikeProbe.FindsTemporalEntity(model, table, schema);
		var path = Environment.GetEnvironmentVariable("SPIKE_LOG_PATH") ?? "operation-report.log";
		File.AppendAllText(path, $"operation={operation.GetType().Name}; table={schema ?? "public"}.{table}; annotations=[{annotations}]; oldAnnotations=[{oldAnnotations}]; targetTemporal={targetTemporal}{Environment.NewLine}");
	}
}
