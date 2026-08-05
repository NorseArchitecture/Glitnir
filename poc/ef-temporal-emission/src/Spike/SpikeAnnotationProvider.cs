using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.Internal;

namespace Spike;

public sealed class SpikeAnnotationProvider(RelationalAnnotationProviderDependencies dependencies)
	: NpgsqlAnnotationProvider(dependencies)
{
	public override IEnumerable<IAnnotation> For(ITable table, bool designTime)
	{
		foreach (var annotation in base.For(table, designTime))
			yield return annotation;

		if (table.EntityTypeMappings.Any(mapping =>
			mapping.TypeBase.FindAnnotation(SpikeProbe.TemporalAnnotation)?.Value as bool? == true))
			yield return new Annotation(SpikeProbe.TemporalAnnotation, true);
	}
}
