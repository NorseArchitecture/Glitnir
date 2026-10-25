using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Spike;

/// <summary>The provider-neutral context; each provider supplies its naming and connection.</summary>
abstract class SpikeContext(DbContextOptions options) : DbContext(options)
{
	public DbSet<SpikeUser> Users =>
		Set<SpikeUser>();

	public DbSet<SpikeClaim> Claims =>
		Set<SpikeClaim>();

	public DbSet<SpikeToken> Tokens =>
		Set<SpikeToken>();

	public DbSet<SpikePasskey> Passkeys =>
		Set<SpikePasskey>();

	protected abstract string StoreName(string pascalName);

	protected override void OnModelCreating(ModelBuilder modelBuilder) =>
		SpikeModel.Configure(modelBuilder, StoreName);
}

/// <summary>PostgreSQL: snake_case tables and columns, as the Norse chassis names them.</summary>
/// <remarks><paramref name="retryOnFailure" /> configures Npgsql's retrying strategy (G1's forced non-retrying case).</remarks>
sealed partial class PostgresSpikeContext(string connectionString, bool retryOnFailure = false, params IInterceptor[] interceptors) : SpikeContext(new DbContextOptionsBuilder()
	.UseNpgsql(connectionString, o =>
	{
		if (retryOnFailure)
			o.EnableRetryOnFailure();
	})
	.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
	.AddInterceptors(interceptors)
	.Options)
{
	protected override string StoreName(string pascalName) =>
		Snake(pascalName);

	/// <summary>Maps a PascalCase name to snake_case.</summary>
	internal static string Snake(string pascalName) =>
		SnakeBoundary().Replace(pascalName, "_$1").ToLowerInvariant();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);
		foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(static e => e.GetProperties()))
			property.SetColumnName(StoreName(property.Name));
	}

	[GeneratedRegex("(?<=[a-z0-9])([A-Z])")]
	private static partial Regex SnakeBoundary();
}

/// <summary>SQL Server 2025: PascalCase names, compatibility level 170 (the platform floor; native <c>json</c>).</summary>
/// <remarks><paramref name="retryOnFailure" /> configures SqlClient's retrying strategy (G1's forced non-retrying case).</remarks>
sealed class SqlServerSpikeContext(string connectionString, bool retryOnFailure = false, params IInterceptor[] interceptors) : SpikeContext(new DbContextOptionsBuilder()
	.UseSqlServer(connectionString, o =>
	{
		o.UseCompatibilityLevel(170);
		if (retryOnFailure)
			o.EnableRetryOnFailure();
	})
	.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
	.AddInterceptors(interceptors)
	.Options)
{
	protected override string StoreName(string pascalName) =>
		pascalName;
}
