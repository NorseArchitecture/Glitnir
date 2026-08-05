using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Spike;

public sealed record Widget
{
	public required Guid Id { get; init; }
	public required string Name { get; init; }
}

public sealed partial class SpikeContext : DbContext
{
	protected override void OnModelCreating(ModelBuilder modelBuilder) => ConfigureWidget(modelBuilder);

	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
	{
		optionsBuilder
			.UseNpgsql("Host=localhost;Port=54329;Database=spike;Username=postgres;Password=spike")
			.ReplaceService<IMigrationsSqlGenerator, SpikeMigrationsSqlGenerator>();

		if (Environment.GetEnvironmentVariable("SPIKE_USE_ANNOTATION_PROVIDER") == "1")
			optionsBuilder.ReplaceService<IRelationalAnnotationProvider, SpikeAnnotationProvider>();
	}

	partial void ConfigureWidget(ModelBuilder modelBuilder);
}
