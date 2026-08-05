using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Spike;

internal static class SpikeProbe
{
	internal const string TemporalAnnotation = "Norse:Temporal";

	internal static bool FindsTemporalEntity(IModel? model, string table, string? schema)
	{
		var relationalTable = model?.GetRelationalModel().FindTable(table, schema);
		return relationalTable is not null
			&& model!.GetEntityTypes().Any(entity =>
				entity.GetSchema() == schema
				&& entity.GetTableName() == table
				&& entity.FindAnnotation(TemporalAnnotation)?.Value as bool? == true);
	}

	internal static int RunSelfTest()
	{
		using var context = new SpikeContext();
		if (!FindsTemporalEntity(context.Model, "widgets", null))
			throw new InvalidOperationException("The marked widget table was not resolved from the target model.");

		Console.WriteLine("SELF-TEST: target-model temporal lookup passed.");
		return 0;
	}
}
