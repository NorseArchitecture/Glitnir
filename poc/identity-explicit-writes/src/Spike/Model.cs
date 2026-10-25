using Microsoft.EntityFrameworkCore;

namespace Spike;

/// <summary>
///     The user, one entity split across two tables (spec §3): <c>users</c> carries the identity record and is
///     temporal; <c>user_credentials</c> carries credential and lookup state and is not.
/// </summary>
sealed record SpikeUser
{
	// users — temporal, the identity record
	public required Guid Id { get; init; }
	public required string UserName { get; init; }
	public required string Email { get; init; }
	public bool EmailConfirmed { get; init; }
	public string? PhoneNumber { get; init; }
	public bool PhoneNumberConfirmed { get; init; }
	public bool TwoFactorEnabled { get; init; }
	public bool LockoutEnabled { get; init; }
	public required Guid ConcurrencyStamp { get; init; }

	// user_credentials — non-temporal
	public byte[]? PasswordHash { get; init; }
	public required string SecurityStamp { get; init; }
	public int AccessFailedCount { get; init; }
	public DateTimeOffset? LockoutEnd { get; init; }
	public required string NormalizedEmail { get; init; }
	public required string NormalizedUserName { get; init; }

	public ICollection<SpikeClaim> Claims { get; init; } = [];
	public ICollection<SpikeToken> Tokens { get; init; } = [];
	public ICollection<SpikePasskey> Passkeys { get; init; } = [];
}

/// <summary>A user claim; <see cref="ClaimValue" /> is unbounded, which is why G6 needs a stored hash to index it.</summary>
sealed record SpikeClaim
{
	public int Id { get; init; }
	public required Guid UserId { get; init; }
	public required string ClaimType { get; init; }
	public required string ClaimValue { get; init; }
	public SpikeUser User { get; init; } = null!;
}

/// <summary>A user token keyed by <c>(user_id, login_provider, name)</c> — the G4 upsert target.</summary>
sealed record SpikeToken
{
	public required Guid UserId { get; init; }
	public required string LoginProvider { get; init; }
	public required string Name { get; init; }
	public string? Value { get; init; }
	public SpikeUser User { get; init; } = null!;
}

/// <summary>A passkey; <see cref="Data" /> is a JSON-mapped column (<c>jsonb</c> / native SQL Server 2025 <c>json</c>).</summary>
sealed record SpikePasskey
{
	public required byte[] CredentialId { get; init; }
	public required Guid UserId { get; init; }
	public required SpikePasskeyData Data { get; init; }
	public SpikeUser User { get; init; } = null!;
}

/// <summary>The passkey document — a reduced <c>IdentityPasskeyData</c>; <see cref="SignCount" /> is the G-gate guard.</summary>
sealed record SpikePasskeyData
{
	public required byte[] PublicKey { get; init; }
	public string? Name { get; init; }
	public required DateTimeOffset CreatedAt { get; init; }
	public required uint SignCount { get; init; }
	public bool IsUserVerified { get; init; }
	public bool IsBackedUp { get; init; }
}

static class SpikeModel
{
	/// <summary>Configures the model; <paramref name="name" /> maps a PascalCase store name to the provider's naming.</summary>
	public static void Configure(ModelBuilder modelBuilder, Func<string, string> name) =>
		modelBuilder
			.Entity<SpikeUser>(static b =>
			{
				b.HasKey(static u => u.Id);
				b.Property(static u => u.UserName).HasMaxLength(256);
				b.Property(static u => u.Email).HasMaxLength(256);
				b.Property(static u => u.PhoneNumber).HasMaxLength(256);
				b.Property(static u => u.ConcurrencyStamp).IsConcurrencyToken();
				b.Property(static u => u.PasswordHash).HasMaxLength(128);
				b.Property(static u => u.SecurityStamp).HasMaxLength(32);
				b.Property(static u => u.NormalizedEmail).HasMaxLength(256);
				b.Property(static u => u.NormalizedUserName).HasMaxLength(256);
				b
					.HasMany(static u => u.Claims)
					.WithOne(static c => c.User)
					.HasForeignKey(static c => c.UserId);
				b
					.HasMany(static u => u.Tokens)
					.WithOne(static t => t.User)
					.HasForeignKey(static t => t.UserId);
				b
					.HasMany(static u => u.Passkeys)
					.WithOne(static p => p.User)
					.HasForeignKey(static p => p.UserId);
			})
			.Entity<SpikeUser>(b =>
			{
				b.ToTable(name("Users"));
				b.SplitToTable(name("UserCredentials"), static t =>
				{
					t.Property(static u => u.PasswordHash);
					t.Property(static u => u.SecurityStamp);
					t.Property(static u => u.AccessFailedCount);
					t.Property(static u => u.LockoutEnd);
					t.Property(static u => u.NormalizedEmail);
					t.Property(static u => u.NormalizedUserName);
				});
			})
			.Entity<SpikeClaim>(b =>
			{
				b.ToTable(name("UserClaims"));
				b.HasKey(static c => c.Id);
				b.Property(static c => c.ClaimType).HasMaxLength(256);
				b.Property(static c => c.ClaimValue).HasMaxLength(-1);
			})
			.Entity<SpikeToken>(b =>
			{
				b.ToTable(name("UserTokens"));
				b.HasKey(static t => new { t.UserId, t.LoginProvider, t.Name });
				b.Property(static t => t.LoginProvider).HasMaxLength(128);
				b.Property(static t => t.Name).HasMaxLength(128);
				b.Property(static t => t.Value).HasMaxLength(-1);
			})
			.Entity<SpikePasskey>(b =>
			{
				b.ToTable(name("UserPasskeys"));
				b.HasKey(static p => p.CredentialId);
				b.Property(static p => p.CredentialId).HasMaxLength(1024);
				b.ComplexProperty(static p => p.Data, d => d.ToJson(name("Data")));
			});
}
