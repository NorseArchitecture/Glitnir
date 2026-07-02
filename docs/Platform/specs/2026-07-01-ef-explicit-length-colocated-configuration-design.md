# EF Explicit-Length Enforcement & Colocated Entity Configuration

**Date:** 2026-07-01
**Status:** Approved design, ready for planning
**Owner:** Buvy
**Companion specs:**
- `2026-06-28-migrations-framework-identity-schema-design.md` — establishes Himinbjörg's entity types and `NorseIdentityDbContext`; this spec adds configuration and length-enforcement machinery on top of that foundation, before the schema is first migrated for real.
- `2026-06-11-entityframework-context-provenance-decision-inputs.md` — Urdarbrunnr's EF context provenance decisions this spec extends.

---

## 0. Why This Comes Now

Himinbjörg's entity types (`NorseUser`, `NorseRole`, etc.) exist as bare pass-through subclasses of ASP.NET Core Identity's generic base classes — no `OnModelCreating` configuration has been written yet beyond `builder.UseOpenIddict<Guid>()`. Every `string` and `byte[]` column in that model is one missed `HasMaxLength` call away from silently becoming `nvarchar(max)`/`text` — the database equivalent of a swallowed exception. This spec closes that gap before the Identity schema is migrated for the first time in earnest, and gives every future Norse bounded context the same protection for free.

Two problems, one design:

1. **No silent unbounded columns.** A `string`/`byte[]` property must carry an explicit length decision — a positive bound, or `-1` to explicitly opt into unbounded. Nothing reaches the database by omission.
2. **No config archaeology.** Whether a property is configured via attribute or fluent API, the configuration lives in the same file as the entity it describes — never off in a separate `OnModelCreating` wall of text a developer has to search to find.

---

## 1. Placement

| Concern | Realm | Project |
|---|---|---|
| `MaxLengthAttribute`, `FixedLengthAttribute`, `UnboundedLengthAttribute` | Urdarbrunnr | `Norse.EntityFramework` |
| `RequireExplicitLengthConvention` | Urdarbrunnr | `Norse.EntityFramework` |
| `NorseModelConventions.Apply(...)` (shared registration helper) | Urdarbrunnr | `Norse.EntityFramework` |
| `NorseDbContext.ConfigureConventions` override (new) | Urdarbrunnr | `Norse.EntityFramework` |
| `NorseIdentityDbContext.ConfigureConventions` override (new) | Himinbjörg | `Norse.Identity` |
| Colocated `Configuration : IEntityTypeConfiguration<T>` per entity | Himinbjörg | `Norse.Identity` |

The attributes and the enforcing convention are generic, provider-agnostic platform machinery — any bounded context that rides on `Norse.EntityFramework` gets the same guarantee without writing it twice. Himinbjörg is the first consumer, not a special case.

---

## 2. The Attribute Trio (Urdarbrunnr)

All three restrict `AttributeUsage` to `Property | Field` — the same restriction EF Core's own `PrecisionAttribute` uses, which makes omitting the `property:` target specifier on a positional record parameter a **compile error**. This matters for future Norse-authored domain entities (records are the platform default for value objects); it's inert for Himinbjörg's current entities, which are plain classes.

```csharp
namespace Norse.EntityFramework;

/// <summary>
/// Drop-in replacement for <see cref="System.ComponentModel.DataAnnotations.MaxLengthAttribute"/>,
/// restricted to properties and fields. Compile error if applied without the <c>property:</c>
/// target specifier on a positional record parameter, matching <see cref="PrecisionAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class MaxLengthAttribute(int length)
    : System.ComponentModel.DataAnnotations.MaxLengthAttribute(length);

/// <summary>
/// Marks a string property as fixed-length. Equivalent to <c>.HasMaxLength(n).IsFixedLength()</c> —
/// <c>nchar(n)</c>/<c>char(n)</c> depending on provider.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class FixedLengthAttribute(int length)
    : System.ComponentModel.DataAnnotations.MaxLengthAttribute(length);

/// <summary>
/// Marks a string or binary property as explicitly unbounded — <c>nvarchar(max)</c>/<c>text</c>,
/// <c>varbinary(max)</c>/<c>bytea</c>. Passes EF Core's own <c>-1</c> sentinel for "no maximum."
/// The only attribute-path escape hatch from <see cref="RequireExplicitLengthConvention"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class UnboundedLengthAttribute()
    : System.ComponentModel.DataAnnotations.MaxLengthAttribute(-1);
```

`FixedLengthAttribute` needs a companion convention step to also call `IsFixedLength()` — EF Core's stock attribute convention reads `MaxLengthAttribute` for the length but has no attribute-driven path to `IsFixedLength()`. `RequireExplicitLengthConvention` (below) or a small sibling convention translates presence of `FixedLengthAttribute` specifically into `IsFixedLength()` on that property, in addition to satisfying the length check.

---

## 3. Enforcement — `RequireExplicitLengthConvention` (Urdarbrunnr)

An `IModelFinalizingConvention` that runs once, at model build — application startup, `dotnet ef migrations add`, or the first test that touches a context. Never at request time; there is no later, safe point to catch this at runtime, and it is the earliest point EF exposes a fully-resolved model to inspect.

**Mechanics:**
- Walks every entity type in the finalized model, every property of CLR type `string` or `byte[]`.
- Checks EF's own resolved metadata (`property.GetMaxLength()`) — **not** attribute presence. This is what makes it work uniformly regardless of whether the length came from `[MaxLength]`, fluent `HasMaxLength`, ASP.NET Core Identity's own base `OnModelCreating`, or OpenIddict's `UseOpenIddict<Guid>()` conventions. Norse doesn't own the declaring class for most of Himinbjörg's properties today — the model-metadata check doesn't need to care.
- **Whole-model scope, no namespace exemption.** OpenIddict's own entity types are checked exactly like Norse's. If OpenIddict ever leaves a column unbounded by omission, the convention throws for it too — Norse doesn't get to silently trust a third party's defaults any more than its own.
- A `null` max length is the only failure condition. `-1` (explicit unbounded) and any positive integer both pass.
- Collects **every** violation across the whole model before throwing — one exception, not whack-a-mole:

```
InvalidOperationException: 3 propert(y/ies) have no explicit length declared.
Decorate with [MaxLength(n)]/[FixedLength(n)], configure HasMaxLength(n) in the
entity's Configuration, or declare [UnboundedLength]/HasMaxLength(-1) if truly unbounded:
  - Norse.Identity.NorseUser.PhoneNumber (string)
  - Norse.Identity.NorseUser.PasswordHash (string)
  - Norse.Identity.NorseUserToken.Value (string)
```

**Wiring — shared helper, two call sites:**

```csharp
namespace Norse.EntityFramework;

public static class NorseModelConventions
{
    public static ModelConfigurationBuilder Apply(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Conventions.Add(static _ => new RequireExplicitLengthConvention());
        return configurationBuilder;
    }
}
```

- `NorseDbContext` (non-auth base) gains a new `ConfigureConventions` override calling `NorseModelConventions.Apply(configurationBuilder)` — every context that inherits it gets the guarantee for free, no opt-in step.
- `NorseIdentityDbContext` cannot inherit `NorseDbContext` (it inherits `IdentityDbContext`), so it gains its own `ConfigureConventions` override making the same call — mirroring how it already manually calls `ApplyNorseConventions` for snake-case naming instead of inheriting it.

**Scope note (deliberate, not deferred):** `DateTimeUtcConverter`, global `Properties<T>().HaveConversion<...>()` calls, and `SequentialGuid` value converters are out of scope for this spec — they don't exist in Urdarbrunnr yet and weren't part of what was asked for here (`SequentialGuid` folding into Norse.Primitives is tracked separately). Nothing in this design blocks adding them later.

---

## 4. Colocated Configuration Pattern (Himinbjörg)

Each entity gets a nested `public sealed class Configuration : IEntityTypeConfiguration<TEntity>` in the same file as the entity — fluent API only for now, since none of Himinbjörg's entities declare their own properties yet (they're pure pass-throughs per the migrations-framework spec's "no additional properties" rule). The attribute path exists for the day a Norse-owned property lands (the documented `NorseUser<T>` abstract-base upgrade path).

```csharp
public sealed class NorseUser : IdentityUser<Guid>
{
    public sealed class Configuration : IEntityTypeConfiguration<NorseUser>
    {
        static readonly ValueConverter<string?, Guid?> StampConverter = new(
            static s => s != null ? Guid.Parse(s) : null,
            static g => g.HasValue ? g.ToString() : null);

        static readonly ValueConverter<string?, byte[]?> HashConverter = new(
            static s => s != null ? Convert.FromBase64String(s) : null,
            static b => b != null ? Convert.ToBase64String(b) : null);

        public void Configure(EntityTypeBuilder<NorseUser> builder)
        {
            builder.ToTable("users");
            builder.Property(u => u.ConcurrencyStamp).HasConversion(StampConverter);
            builder.Property(u => u.SecurityStamp).HasConversion(StampConverter);
            builder.Property(u => u.PasswordHash).HasConversion(HashConverter).HasMaxLength(128);
            builder.Property(u => u.PhoneNumber).HasMaxLength(20);

            builder.HasMany(u => u.Claims).WithOne(c => c.User).HasForeignKey(c => c.UserId).IsRequired();
            builder.HasMany(u => u.Logins).WithOne(l => l.User).HasForeignKey(l => l.UserId).IsRequired();
            builder.HasMany(u => u.Tokens).WithOne(t => t.User).HasForeignKey(t => t.UserId).IsRequired();
            builder.HasMany(u => u.Passkeys).WithOne(p => p.User).HasForeignKey(p => p.UserId).IsRequired();
            builder.HasIndex(u => u.NormalizedEmail).HasDatabaseName("ix_users_normalized_email");
            builder.HasIndex(u => u.NormalizedUserName).IsUnique().HasDatabaseName("ix_users_normalized_user_name");
        }
    }
}
```

Value converters (`StampConverter`, `HashConverter`) are private static fields scoped to the `Configuration` class that uses them — **not** generalized into Urdarbrunnr. No other realm runs ASP.NET Identity today; a shared abstraction would have exactly one caller. Extract later if a second consumer appears.

The same pattern applies to every other Himinbjörg entity:

| Entity | Configuration highlights |
|---|---|
| `NorseRole` | `ConcurrencyStamp` → `StampConverter`; `NormalizedName` unique index |
| `NorseRoleClaim` | `ClaimType`/`ClaimValue` — already bounded by Identity's own base config; colocated anyway for discoverability |
| `NorseUserClaim` | Same as above |
| `NorseUserLogin` | `LoginProvider`/`ProviderKey`/`ProviderDisplayName` lengths |
| `NorseUserToken` | `LoginProvider`/`Name` bounded; `Value` gets `[UnboundedLength]`/`HasMaxLength(-1)` — explicit now, not assumed |
| `NorseUserPasskey` | `HasKey(p => p.CredentialId)`; `OwnsOne(p => p.Data, o => o.ToJson())` |
| `NorseUserRole` | Table name only — join entity, no scalar properties of its own |

The `User`↔`Role` many-to-many `UsingEntity<UserRole>` wiring stays in `NorseUser.Configuration` — `User` is the natural owner of that relationship, not the join table.

`NorseIdentityDbContext.OnModelCreating` shrinks to:

```csharp
protected override void OnModelCreating(ModelBuilder builder)
{
    base.OnModelCreating(builder);
    builder.HasDefaultSchema("identity");
    builder.UseOpenIddict<Guid>();
    builder.ApplyConfigurationsFromAssembly(typeof(NorseUser).Assembly);

    // OpenIddict's entity types are framework-owned — no file exists to colocate a
    // Configuration into. Any property RequireExplicitLengthConvention flags for
    // OpenIddict gets an explicit fluent HasMaxLength/-1 call added here directly,
    // discovered by running the convention and reading its (multi-violation) throw —
    // there is no need to pre-enumerate OpenIddict's schema by hand.
}
```

`ApplyConfigurationsFromAssembly` is EF Core's own reflection-based scan, run once at model build — a startup-only cost, consistent with the platform's "reflection in hot paths is forbidden, one-time startup wiring is acceptable" rule. It requires no registration boilerplate as entities are added or Himinbjörg grows.

---

## 5. Testing

- **Unit tests for `RequireExplicitLengthConvention`** (Urdarbrunnr, xUnit v3 + Shouldly): build small throwaway `DbContext`s with intentionally-unbounded and intentionally-bounded properties; assert the convention throws with all violations named for the bad case, and is silent for the good case (including the `-1` sentinel path).
- **Integration test for Himinbjörg**: build `NorseIdentityDbContext`'s model (no live database needed — `context.Model` triggers finalization) and assert it does *not* throw — the real proof that every real column, Norse-owned or OpenIddict-owned, has an explicit length decision.
- **`dotnet ef migrations add` re-run**: the existing `20260701171417_InitialCreate` migration predates this work and is already known-ephemeral (per the identity-schema-provider-defaults decision); it gets regenerated once this configuration lands, and any convention violations surface at that point rather than needing to be hand-enumerated in advance.

---

## 6. Self-Review

**Placeholder scan:** No TBDs. The one open item — which exact OpenIddict properties (if any) need an explicit fluent length call — is deliberately left to be *discovered* by running the convention rather than pre-enumerated by hand; that's the design working as intended, not a gap.

**Internal consistency:** §1 (placement) matches §3's two-call-site wiring and §4's "Himinbjörg-local converters" decision. §3's whole-model scope is consistent with §4's explicit note that OpenIddict exceptions land directly in `OnModelCreating`, not colocated. §2's record-safety restriction is inert today (no records in Himinbjörg) but doesn't contradict anything.

**Scope:** Two realms (Urdarbrunnr, Himinbjörg), one coherent mechanism — enforcement machinery and its first real consumer, same shape as the migrations-framework spec's Urdarbrunnr/Himinbjörg split. Not decomposed further; splitting would separate a rule from its only proof that it works.

**Ambiguity:** "Explicit" is defined precisely as "EF's resolved model metadata has a non-null max length" — not "an attribute is present," which is what makes inherited framework properties and OpenIddict's own configuration both fall correctly under the same check without special-casing.
