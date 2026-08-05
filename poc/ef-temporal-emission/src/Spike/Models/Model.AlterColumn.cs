using Microsoft.EntityFrameworkCore;

namespace Spike;

public sealed partial class SpikeContext
{
	partial void ConfigureWidget(ModelBuilder modelBuilder) =>
		modelBuilder.Entity<Widget>(eb =>
		{
			eb.HasKey(w => w.Id);
			eb.Property(w => w.Name).HasMaxLength(128);
			eb.ToTable("widgets", tb => tb.Metadata.SetAnnotation(SpikeProbe.TemporalAnnotation, true));
		});
}
