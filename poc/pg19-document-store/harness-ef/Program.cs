// Proves the .Server read path Buvy ruled: a read-only, NoTracking, jsonb-mapped EF context
// translating an engineer's predicate + projection EXPRESSIONS into server-side jsonb SQL.
// No jsonb_build_object written by hand. The generated SQL is logged so we can confirm the
// translation happens in the database, not by client-eval. Read-only: no SaveChanges, no migrations.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

string replica = GetArg("--replica") ?? throw new ArgumentException("--replica connection string required");

await using ReadContext ctx = new(replica);

// Grab a real id so the predicate hits a row (the predicate is the point, not the value).
Guid id = await ctx.Policies.Select(p => p.Id).FirstAsync();
Console.WriteLine($"target id = {id}\n");

Console.WriteLine("=== single-member projection: Where(p => p.Id == id).Select(p => p.Body.Premium.Amount) ===");
decimal? premium = await ctx.Policies
	.Where(p => p.Id == id)
	.Select(p => p.Body.Premium.Amount)
	.SingleAsync();
Console.WriteLine($"-> premium = {premium}\n");

Console.WriteLine("=== record projection: Select(p => new PocViewModel(p.Id, p.Body.Premium.Amount, p.Body.ProductCode)) ===");
PocViewModel vm = await ctx.Policies
	.Where(p => p.Id == id)
	.Select(p => new PocViewModel(p.Id, p.Body.Premium.Amount, p.Body.ProductCode))
	.SingleAsync();
Console.WriteLine($"-> {vm}\n");

Console.WriteLine("=== predicate over a jsonb member: Where(p => p.Body.ProductCode == \"WC\") count ===");
int wc = await ctx.Policies.Where(p => p.Body.ProductCode == "WC").CountAsync();
Console.WriteLine($"-> {wc} WC rows\n");

return 0;

static string? GetArg(string name)
{
	string[] argv = Environment.GetCommandLineArgs();
	for (int i = 0; i < argv.Length - 1; i++)
		if (argv[i] == name) return argv[i + 1];
	return null;
}

// ── read model + mapping ─────────────────────────────────────────────────────
// The table is (id uuid pk, dedup_key uuid, status text, doc jsonb). EF maps Id -> id column
// and the owned Body -> the doc jsonb column (ToJson). Owned members carry HasJsonPropertyName
// to match the camelCase keys the documents actually use. dedup_key/status stay unmapped — EF
// selects only what the model declares.
sealed class ReadContext(string conn) : DbContext
{
	public DbSet<PolicyRow> Policies => Set<PolicyRow>();

	protected override void OnConfiguring(DbContextOptionsBuilder o) => o
		.UseNpgsql(conn)
		.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
		.LogTo(Console.WriteLine, [DbLoggerCategory.Database.Command.Name], LogLevel.Information)
		.EnableSensitiveDataLogging();

	protected override void OnModelCreating(ModelBuilder b)
	{
		var e = b.Entity<PolicyRow>();
		e.ToTable("policy_view");
		e.HasKey(x => x.Id);
		e.Property(x => x.Id).HasColumnName("id");
		e.OwnsOne(x => x.Body, ob =>
		{
			ob.ToJson("doc");
			ob.Property(x => x.ProductCode).HasJsonPropertyName("productCode");
			ob.OwnsOne(x => x.Premium, pb =>
			{
				pb.HasJsonPropertyName("premium");   // the nested navigation key, not just its members
				pb.Property(x => x.Amount).HasJsonPropertyName("amount");
				pb.Property(x => x.Currency).HasJsonPropertyName("currency");
			});
		});
	}
}

sealed class PolicyRow
{
	public Guid Id { get; set; }
	public PolicyBody Body { get; set; } = null!;
}

sealed class PolicyBody
{
	public string ProductCode { get; set; } = null!;
	public Premium Premium { get; set; } = null!;
}

sealed class Premium
{
	public decimal? Amount { get; set; }
	public string? Currency { get; set; }
}

// The engineer's projection target — flat, no jsonb anything in sight.
sealed record PocViewModel(Guid Id, decimal? Premium, string ProductCode);
