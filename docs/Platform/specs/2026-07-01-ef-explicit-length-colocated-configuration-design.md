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
2. **No config archaeology, compiler-enforced.** Every entity is its own configuration — the fluent mapping lives as a `static Configure` method on the entity class itself, not a separate file, not even a separate nested type. There is exactly one place to go look for `.HasMaxLength(25)` fluent calls or `[MaxLength(25)]` attributes on any given entity, and porting a property between the two styles never means hunting across files. Declare an entity that opts into the pattern and the compiler refuses to build until `Configure` exists.

Applying this discipline surfaced a live example: OpenIddict's own EF Core entity model (verified against `openiddict-core` tag `7.5.0`) leaves several non-JSON columns unbounded by omission — `Application.ClientSecret`, `Application.DisplayName`, `Authorization.Scopes`, `Scope.Description`, `Scope.DisplayName`, `Token.Payload`. §4.3 wraps them the same way this spec already wraps ASP.NET Core Identity.

---

## 1. Placement

| Concern | Realm | Project |
|---|---|---|
| `MaxLengthAttribute`, `FixedLengthAttribute`, `UnboundedLengthAttribute` | Urdarbrunnr | `Norse.EntityFramework` |
| `RequireExplicitLengthConvention` | Urdarbrunnr | `Norse.EntityFramework` |
| `RequireEntityConfigurationConvention` (new) | Urdarbrunnr | `Norse.EntityFramework` |
| `NorseModelConventions.Apply(...)` (shared registration helper) | Urdarbrunnr | `Norse.EntityFramework` |
| `NorseDbContext.ConfigureConventions` override (new) | Urdarbrunnr | `Norse.EntityFramework` |
| `NorseDbContext.OnModelCreating` override (new) | Urdarbrunnr | `Norse.EntityFramework` |
| `INorseEntity<TSelf>` (new) | Urdarbrunnr | `Norse.EntityFramework` |
| `NorseEntityBase<TSelf>` (new) | Urdarbrunnr | `Norse.EntityFramework` |
| `EntityConfigurationApplicationGenerator` (Roslyn `IIncrementalGenerator`) | Urdarbrunnr | `Norse.EntityFramework.Generator` (renamed post-ship from `.Configuration.Generator`; forwarded as an analyzer directly from `Norse.EntityFramework` itself — no separate wrapper package, see plan's "Post-ship amendments") |
| `NorseIdentityDbContext.ConfigureConventions`/`OnModelCreating` overrides (new) | Himinbjörg | `Norse.Identity` |
| Identity entities implementing `INorseEntity<TSelf>` directly (`NorseUser`, `NorseRole`, etc.) | Himinbjörg | `Norse.Identity` |
| `NorseOpenIddictApplication`/`Authorization`/`Scope`/`Token` (new wrapper entities) | Himinbjörg | `Norse.Identity` |

The attributes, the enforcing conventions, and the two-tier entity-configuration machinery are all generic, provider-agnostic platform machinery — any bounded context that rides on `Norse.EntityFramework` gets the same guarantee without writing it twice. Himinbjörg is the first consumer, not a special case.

**Amendment (2026-07-25):** every `Norse.EntityFramework`/`Norse.EntityFramework.Generator` reference in this spec (the placement table above included) names the namespace as it stood when this spec was written. Urðarbrunnr's own follow-on widening — `Norse.EntityFramework.*` → `Norse.Persistence.EntityFramework.*` — merged separately (PR #31, tag v0.0.4) and is what the platform builds against today.

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

Properties inherited from a brownfield base (e.g. `IdentityUser<Guid>.PhoneNumber`, `OpenIddictEntityFrameworkCoreApplication<TKey>.ClientSecret`) can't carry these attributes — Norse doesn't own the declaration. Those get their length fluently, inside the entity's own `Configure` method (§4).

---

## 3. Enforcement — `RequireExplicitLengthConvention` (Urdarbrunnr)

An `IModelFinalizingConvention` that runs once, at model build — application startup, `dotnet ef migrations add`, or the first test that touches a context. Never at request time; there is no later, safe point to catch this at runtime, and it is the earliest point EF exposes a fully-resolved model to inspect.

**Mechanics:**
- Walks every entity type in the finalized model, every property.
- **Checks storage type, not CLR type.** `property.GetValueConverter()?.ProviderClrType ?? property.ClrType` — a property converted *away* from `string`/`byte[]` (e.g. `NorseUser.ConcurrencyStamp`/`SecurityStamp`: CLR type `string?`, storage type `Guid?` via `StampConverter`) is correctly skipped, since it's never written to the database as a string at all. A property converted *into* `string` (any future `HasConversion<string>()` enum mapping, per the platform's own enum-at-the-database-boundary convention) is correctly caught, even though its CLR type is an enum, not `string`. A CLR-type-only check would get both of these backwards.
- **Skips properties of JSON-mapped owned entities** (`entityType.IsMappedToJson()`). `NorseUserPasskey.Data` (`OwnsOne(...).ToJson()`, §4.2) serializes its properties into one JSON column on the owner — each individual property never gets its own column or its own length, so it's out of scope for this check by construction, not by exemption.
- Checks EF's own resolved metadata (`property.GetMaxLength()`) — **not** attribute presence. This is what makes it work uniformly regardless of whether the length came from `[MaxLength]`, fluent `HasMaxLength`, ASP.NET Core Identity's own base `OnModelCreating`, or OpenIddict's `UseOpenIddict<...>()` conventions. Norse doesn't own the declaring class for most of Himinbjörg's properties today — the model-metadata check doesn't need to care.
- **Whole-model scope, no namespace exemption.** OpenIddict's own entity types are checked exactly like Norse's. If OpenIddict ever leaves a column unbounded by omission, the convention throws for it too — Norse doesn't get to silently trust a third party's defaults any more than its own.
- A `null` max length is the only failure condition. `-1` (explicit unbounded) and any positive integer both pass. **No safe-default fallback** — a property that reaches this convention with no length is a decision nobody made yet, not a number the framework should pick on a developer's behalf. Consistent with the platform's "no silent fallbacks" rule: a missing length is a hard fail, the same way a missing rate factor is a hard fail rather than defaulting to `1.0`.
- Also translates a `[FixedLength(n)]` attribute into `IsFixedLength()` — EF's stock attribute convention reads `MaxLengthAttribute` for the length but has no attribute-driven path to `IsFixedLength()`, so this convention closes that gap in the same pass, fulfilling the promise made in §2.
- Collects **every** violation across the whole model before throwing — one exception, not whack-a-mole:

```csharp
namespace Norse.EntityFramework;

sealed class RequireExplicitLengthConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder builder, IConventionContext<IConventionModelBuilder> context)
    {
        List<string> violations = [];

        foreach (var property in builder.Metadata.GetEntityTypes().SelectMany(static t => t.GetProperties()))
        {
            if (property.DeclaringType is IConventionEntityType entityType && entityType.IsMappedToJson())
                continue;

            var storageType = property.GetValueConverter()?.ProviderClrType ?? property.ClrType;
            if (storageType != typeof(string) && storageType != typeof(byte[]))
                continue;

            if (property.PropertyInfo?.GetCustomAttribute<FixedLengthAttribute>() is not null)
                property.Builder.IsFixedLength(true, fromDataAnnotation: true);

            if (property.GetMaxLength() is null)
                violations.Add($"{property.DeclaringType.ClrType.FullName}.{property.Name} ({storageType.Name})");
        }

        if (violations.Count == 0)
            return;

        throw new InvalidOperationException(
            $"{violations.Count} propert{(violations.Count == 1 ? "y has" : "ies have")} no explicit length declared. " +
            "Decorate with [MaxLength(n)]/[FixedLength(n)], configure HasMaxLength(n) in the entity's Configure method, " +
            "or declare HasMaxLength(-1) if truly unbounded:\n  - " + string.Join("\n  - ", violations));
    }
}
```

```
InvalidOperationException: 3 propert(y/ies) have no explicit length declared.
Decorate with [MaxLength(n)]/[FixedLength(n)], configure HasMaxLength(n) in the
entity's Configure method, or declare HasMaxLength(-1) if truly unbounded:
  - Norse.Identity.NorseUser.PhoneNumber (String)
  - Norse.Identity.NorseUser.PasswordHash (String)
  - Norse.Identity.NorseUserToken.Value (String)
```

**Scope note (deliberate, not deferred):** `DateTimeUtcConverter`, global `Properties<T>().HaveConversion<...>()` calls, and `SequentialGuid` value converters are out of scope for this spec — they don't exist in Urdarbrunnr yet and weren't part of what was asked for here (`SequentialGuid` folding into Norse.Primitives is tracked separately). Nothing in this design blocks adding them later.

### 3.1 Enforcement — `RequireEntityConfigurationConvention` (Urdarbrunnr)

Closes the one gap the compiler can't close on its own. C# forces *correct* implementation of `INorseEntity<TSelf>` the moment a class declares it — but nothing forces a class to declare it in the first place. A sibling `IModelFinalizingConvention`, same file family and same shape as §3:

**Mechanics:**
- Walks every entity type in the finalized model.
- Checks whether the entity's CLR type implements `INorseEntity<TSelf>` for itself.
- **Deliberate, necessary exemption — unlike §3's length check.** Types Norse doesn't declare and hasn't wrapped (raw framework types, if any ever slip into a future model) are out of scope for this specific check — Norse cannot retrofit an interface onto a class it doesn't own. This is the one asymmetry versus §3: the length convention has no exemption because *checking* a resolved property's metadata doesn't require owning the declaration, but *implementing an interface* does. By the time this spec ships, every entity actually present in `NorseIdentityDbContext`'s model — including every OpenIddict entity — is a Norse-owned type (§4.2, §4.3) that *does* implement `INorseEntity<TSelf>`, so the exemption is theoretical headroom, not a live gap today.
- Collects every violation before throwing, same fail-loud, no-whack-a-mole shape as §3.

**Wiring — shared helper, two call sites, both conventions registered together:**

```csharp
namespace Norse.EntityFramework;

public static class NorseModelConventions
{
    public static ModelConfigurationBuilder Apply(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Conventions.Add(static _ => new RequireExplicitLengthConvention());
        configurationBuilder.Conventions.Add(static _ => new RequireEntityConfigurationConvention());
        return configurationBuilder;
    }
}
```

- `NorseDbContext` (non-auth base) gains a new `ConfigureConventions` override calling `NorseModelConventions.Apply(configurationBuilder)` — every context that inherits it gets both guarantees for free, no opt-in step.
- `NorseIdentityDbContext` cannot inherit `NorseDbContext` (it inherits `IdentityDbContext`), so it gains its own `ConfigureConventions` override making the same call — mirroring how it already manually calls `ApplyNorseConventions` for snake-case naming instead of inheriting it.

---

## 4. Colocated Entity Configuration — Compile-Time Enforced

### 4.1 Two-tier enforcement (Urdarbrunnr)

```csharp
namespace Norse.EntityFramework;

public interface INorseEntity<TSelf> where TSelf : INorseEntity<TSelf>
{
    static abstract void Configure(EntityTypeBuilder<TSelf> builder);
}

public abstract class NorseEntityBase<TSelf> : INorseEntity<TSelf>
    where TSelf : NorseEntityBase<TSelf>, INorseEntity<TSelf>
{
    // Configure is deliberately left unimplemented here — every concrete TSelf
    // must supply its own, or the build fails. Static interface members are not
    // inherited via virtual dispatch; an abstract class can leave one unfulfilled
    // and defer the obligation to whichever concrete type closes the generic.
}
```

`Configure` is `static abstract`, not instance-based like EF Core's own `IEntityTypeConfiguration<T>`. `ModelBuilder.ApplyConfiguration` requires an instance to call `Configure` on — for a Norse-owned domain entity with validated construction (required constructor arguments, private constructors behind a factory), that would force a public parameterless constructor onto every entity purely so the generator (§4.4) could build a throwaway instance to invoke a method that never touches instance state. `INorseEntity<TSelf>` sidesteps this entirely: the generator calls `TSelf.Configure(builder)` directly, no instance ever constructed, and entities keep whatever constructor discipline their domain requires.

**Tier 1 — Norse-owned entities, no competing base class.** Inherit `NorseEntityBase<TSelf>`. No entity in Himinbjörg qualifies today (every entity there is brownfield — Tier 2, below); this is the platform default the moment a bounded context with its own domain entities (Policy, Claims, etc.) lands. **Post-ship correction:** `static abstract` interface members have no legal "leave unfulfilled through an intervening class" syntax in C#, so `NorseEntityBase<TSelf>` does not itself implement `INorseEntity<TSelf>` — a concrete `TSelf` must declare `: NorseEntityBase<TSelf>, INorseEntity<TSelf>` explicitly (both), not just the base class. This is one explicit interface declaration short of the "free by inheritance" framing originally intended; the compile-time guarantee itself (omitting `Configure` fails the build) still holds.

**Tier 2 — brownfield entities, base-class slot already spent.** A class that must inherit a third-party base (`IdentityUser<Guid>`, `OpenIddictEntityFrameworkCoreApplication<TKey,...>`) can't also inherit `NorseEntityBase<TSelf>` — C# is single-inheritance, and the slot is taken. These implement `INorseEntity<TSelf>` directly, alongside the brownfield base: `NorseUser : IdentityUser<Guid>, INorseEntity<NorseUser>`. Same compile-time guarantee as Tier 1 — the interface still won't compile without `Configure` — just without a shared base class forcing the declaration to exist at all (closed by §3.1 at the model-finalization level instead).

### 4.2 Himinbjörg's Identity entities (Tier 2)

```csharp
public sealed class NorseUser : IdentityUser<Guid>, INorseEntity<NorseUser>
{
    static readonly ValueConverter<string?, Guid?> StampConverter = new(
        static s => s != null ? Guid.Parse(s) : null,
        static g => g.HasValue ? g.ToString() : null);

    static readonly ValueConverter<string?, byte[]?> HashConverter = new(
        static s => s != null ? Convert.FromBase64String(s) : null,
        static b => b != null ? Convert.ToBase64String(b) : null);

    public static void Configure(EntityTypeBuilder<NorseUser> builder)
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
```

Value converters (`StampConverter`, `HashConverter`) are private static fields on `NorseUser` itself — **not** generalized into Urdarbrunnr. No other realm runs ASP.NET Identity today; a shared abstraction would have exactly one caller. Extract later if a second consumer appears.

The same pattern applies to every other Himinbjörg Identity entity:

| Entity | `Configure` highlights |
|---|---|
| `NorseRole` | `ConcurrencyStamp` → `StampConverter`; `NormalizedName` unique index |
| `NorseRoleClaim` | `ClaimType`/`ClaimValue` — already bounded by Identity's own base config; colocated anyway for discoverability |
| `NorseUserClaim` | Same as above |
| `NorseUserLogin` | `LoginProvider`/`ProviderKey`/`ProviderDisplayName` lengths |
| `NorseUserToken` | `LoginProvider`/`Name` bounded; `Value` gets `HasMaxLength(-1)` — explicit now, not assumed |
| `NorseUserPasskey` | `HasKey(p => p.CredentialId)`; `OwnsOne(p => p.Data, o => o.ToJson())` |
| `NorseUserRole` | Table name only — join entity, no scalar properties of its own |

The `User`↔`Role` many-to-many `UsingEntity<UserRole>` wiring stays in `NorseUser.Configure` — `User` is the natural owner of that relationship, not the join table.

### 4.3 OpenIddict entities become Tier 2 as well (Himinbjörg)

Checked against `openiddict-core` tag `7.5.0`'s actual EF Core configuration source (`OpenIddictEntityFrameworkCore{Application,Authorization,Scope,Token}Configuration.cs`): every property tagged `[StringSyntax(StringSyntaxAttribute.Json)]` is deliberately left unbounded by OpenIddict itself — that's intentional and stays that way. But six non-JSON columns are unbounded purely by omission:

| Entity | Property | Bound |
|---|---|---|
| Application | `ClientSecret` | `HasMaxLength(-1)` — hashed/encrypted, algorithm-dependent length, same category as `NorseUserToken.Value` |
| Application | `DisplayName` | `HasMaxLength(200)` |
| Authorization | `Scopes` | `HasMaxLength(-1)` — serialized array, same category as OpenIddict's own JSON columns; just not `[StringSyntax(Json)]`-tagged upstream |
| Scope | `Description` | `HasMaxLength(1000)` |
| Scope | `DisplayName` | `HasMaxLength(200)` |
| Token | `Payload` | `HasMaxLength(-1)` — reference-token payload, may be hashed/encrypted, same reasoning as `ClientSecret` |

OpenIddict's own entity base classes are designed to be subclassed — its own `OpenIddictEntityFrameworkCoreApplication<TKey>` is itself a subclass of `OpenIddictEntityFrameworkCoreApplication<TKey, TAuthorization, TToken>` — and `ModelBuilder.UseOpenIddict<TApplication, TAuthorization, TScope, TToken, TKey>()` accepts custom subclasses directly. Norse wraps them the same way it already wraps `IdentityUser<Guid>`:

```csharp
public sealed class NorseOpenIddictApplication
    : OpenIddictEntityFrameworkCoreApplication<Guid, NorseOpenIddictAuthorization, NorseOpenIddictToken>,
      INorseEntity<NorseOpenIddictApplication>
{
    public static void Configure(EntityTypeBuilder<NorseOpenIddictApplication> builder)
    {
        builder.Property(a => a.ClientSecret).HasMaxLength(-1);
        builder.Property(a => a.DisplayName).HasMaxLength(200);
    }
}

public sealed class NorseOpenIddictAuthorization
    : OpenIddictEntityFrameworkCoreAuthorization<Guid, NorseOpenIddictApplication, NorseOpenIddictToken>,
      INorseEntity<NorseOpenIddictAuthorization>
{
    public static void Configure(EntityTypeBuilder<NorseOpenIddictAuthorization> builder) =>
        builder.Property(a => a.Scopes).HasMaxLength(-1);
}

public sealed class NorseOpenIddictScope
    : OpenIddictEntityFrameworkCoreScope<Guid>, INorseEntity<NorseOpenIddictScope>
{
    public static void Configure(EntityTypeBuilder<NorseOpenIddictScope> builder)
    {
        builder.Property(s => s.Description).HasMaxLength(1000);
        builder.Property(s => s.DisplayName).HasMaxLength(200);
    }
}

public sealed class NorseOpenIddictToken
    : OpenIddictEntityFrameworkCoreToken<Guid, NorseOpenIddictApplication, NorseOpenIddictAuthorization>,
      INorseEntity<NorseOpenIddictToken>
{
    public static void Configure(EntityTypeBuilder<NorseOpenIddictToken> builder) =>
        builder.Property(t => t.Payload).HasMaxLength(-1);
}
```

Fluent-only, same as `NorseUser`'s wrapping of inherited `IdentityUser<Guid>` properties — these properties are declared (and `virtual`) on OpenIddict's base classes, so a `[MaxLength]` attribute has no property to attach to without an override that exists solely to carry it. `Naming: NorseOpenIddict{Application,Authorization,Scope,Token}` — keeps `OpenIddict` in the name since this is genuinely OpenIddict's schema with Norse's length bounds layered on, and avoids `NorseApplication` ever being read as a product realm or an Aspire AppHost resource.

**DI wiring changes (`IdentityBuilderExtensions.AddNorseIdentity`):** `ReplaceDefaultEntities<Guid>()` (today's TKey-only shorthand, using OpenIddict's raw default entities) becomes the fully-specified overload:

```csharp
services
    .AddOpenIddict()
    .AddCore(o => o
        .UseEntityFrameworkCore()
        .UseDbContext<NorseIdentityDbContext>()
        .ReplaceDefaultEntities<
            NorseOpenIddictApplication, NorseOpenIddictAuthorization,
            NorseOpenIddictScope, NorseOpenIddictToken, Guid>());
```

`NorseIdentityDbContext.OnModelCreating` shrinks to:

```csharp
protected override void OnModelCreating(ModelBuilder builder)
{
    base.OnModelCreating(builder);
    builder.HasDefaultSchema("identity");
    builder.UseOpenIddict<
        NorseOpenIddictApplication, NorseOpenIddictAuthorization,
        NorseOpenIddictScope, NorseOpenIddictToken, Guid>();
    builder.ApplyNorseConfigurations();
}
```

`builder.UseOpenIddict<...>()` still applies OpenIddict's own `OpenIddictEntityFrameworkCore*Configuration` classes first (setting the bounds OpenIddict already gets right — `ClientId`, `ApplicationType`, `Subject`, etc.), and `ApplyNorseConfigurations()` (§4.4) layers the wrapper entities' own `Configure` methods on top for the columns OpenIddict left unbounded. EF merges non-conflicting property configuration from multiple sources without complaint.

`ApplyNorseConfigurations()` replaces EF Core's own `ApplyConfigurationsFromAssembly` reflection scan with a source-generated equivalent — consistent with the platform's "no reflection, source generators preferred" rule (`../CLAUDE.md` §2.7/§8), the same standard the migrations framework already met with `MigrationContributorGenerator`.

### 4.4 `EntityConfigurationApplicationGenerator` (Urdarbrunnr)

A Roslyn `IIncrementalGenerator` in the `Norse.EntityFramework.Generator` project — provider-agnostic, unlike the Postgres-scoped `EntityFramework.Migrations.PostgreSQL.Generator` it originally took its project-layout shape from. **Post-ship correction:** unlike that precedent, there is no separate wrapper package here — `NorseDbContext` already unconditionally requires `INorseEntity<TSelf>` (§3.1), so no consumer of `Norse.EntityFramework` would ever want it without the generator. The analyzer-forwarding and NuGet packing logic live directly in `Norse.EntityFramework`'s own `.csproj`; `Norse.EntityFramework.Generator` stays a separate project only because Roslyn analyzers must target `netstandard2.0` and can never be a normal assembly reference.

**Mechanics:**
- **Same-compilation scan only.** Uses a syntax provider over the compiling project's own class declarations — not the cross-assembly symbol walk `MigrationContributorGenerator` uses. Himinbjörg colocates entities and the `DbContext` in one project (`Norse.Identity`); there is no cross-assembly case to serve yet. A future context that splits entities across projects extends this then — YAGNI now.
- **Shallow scan — no nested-type recursion.** `INorseEntity<TSelf>` is implemented directly on the entity type itself (Tier 1 via `NorseEntityBase<TSelf>`, Tier 2 directly), never on a nested type, so this is the same shallow interface-list check `MigrationContributorGenerator.ImplementsContributorInterface` already does — one of the two mechanical differences originally expected against that precedent no longer exists.
- **Trigger is the interface alone.** Any non-abstract, non-generic class implementing `INorseEntity<T>` is discovered — no marker attribute. Declaring the interface is already an explicit, deliberate signal.
- **No self-match check needed.** `INorseEntity<TSelf>`'s own `where TSelf : INorseEntity<TSelf>` constraint means the compiler already guarantees any type implementing `INorseEntity<X>` has `X` equal to itself — the generator doesn't need to verify this independently.
- **No constructor requirement, no missing-constructor diagnostic.** `Configure` is `static abstract` (§4.1) — the generator never constructs an instance, so the failure mode that originally motivated a compile-time diagnostic here doesn't exist in this shape. This is the second of the two mechanical differences originally expected against `MigrationContributorGenerator` that disappeared once the interface went static.

**Emitted source:**

```csharp
// <auto-generated />
internal static class GeneratedNorseModelConfigurations
{
    public static ModelBuilder ApplyNorseConfigurations(this ModelBuilder builder)
    {
        builder.Entity<NorseUser>(eb => NorseUser.Configure(eb));
        builder.Entity<NorseRole>(eb => NorseRole.Configure(eb));
        builder.Entity<NorseOpenIddictApplication>(eb => NorseOpenIddictApplication.Configure(eb));
        // ...one line per discovered INorseEntity<T>
        return builder;
    }
}
```

**Wiring — two call sites, mirroring §3.1's `ConfigureConventions` pattern exactly:**
- `NorseDbContext` (Urdarbrunnr, non-auth base) gains a new `OnModelCreating` override: `base.OnModelCreating(builder); builder.ApplyNorseConfigurations();`. Every future bounded context that inherits `NorseDbContext` gets colocated-config discovery for free, no per-context call — the same free-by-inheritance guarantee `ConfigureConventions` already gives for both conventions in §3/§3.1.
- `NorseIdentityDbContext` cannot inherit `NorseDbContext` (it inherits `IdentityDbContext`), so its `OnModelCreating` calls `builder.ApplyNorseConfigurations()` explicitly, shown in §4.3.

---

## 5. Testing

- **Unit tests for `RequireExplicitLengthConvention`** (Urdarbrunnr, xUnit v3 + Shouldly): build small throwaway `DbContext`s with intentionally-unbounded and intentionally-bounded properties; assert the convention throws with all violations named for the bad case, and is silent for the good case (including the `-1` sentinel path). Two cases specifically guard the storage-type mechanics: a `string`-CLR-typed property converted to a non-`string`/`byte[]` storage type (e.g. `Guid` via a converter, mirroring `NorseUser.ConcurrencyStamp`) must **not** be flagged even with no `HasMaxLength`; a property of a `ToJson()`-owned entity type must **not** be flagged even with no `HasMaxLength`, since it never gets an individual column.
- **Unit tests for `RequireEntityConfigurationConvention`** (Urdarbrunnr, xUnit v3 + Shouldly): throwaway entities that do/don't implement `INorseEntity<TSelf>`; assert the convention throws naming every entity missing the interface, and is silent when all are covered.
- **Unit tests for `EntityConfigurationApplicationGenerator`** (`EntityFramework.Generator.Tests`, Urdarbrunnr, same shape as `MigrationContributorGeneratorTests`): assert discovery of `INorseEntity<T>` implementations via both `NorseEntityBase<TSelf>` (Tier 1) and direct brownfield implementation (Tier 2), that abstract/generic candidates (e.g. `NorseEntityBase<TSelf>` itself) are skipped, and that the Tier-1 partial-class emission produces valid, re-bindable C# for a namespaced consumer (not just the global-namespace case).
- **Integration test for Himinbjörg**: build `NorseIdentityDbContext`'s model (no live database needed — `context.Model` triggers finalization) and assert it does *not* throw — the real proof that every real column, Norse-owned or OpenIddict-owned, has an explicit length decision, applied via the generated `ApplyNorseConfigurations()` rather than a reflection scan, and that every entity in the model — including the four `NorseOpenIddict*` wrappers — satisfies `RequireEntityConfigurationConvention`.
- **`dotnet ef migrations add` re-run**: the existing `20260701171417_InitialCreate` migration predates this work and is already known-ephemeral (per the identity-schema-provider-defaults decision); it gets regenerated once this configuration lands, and any convention violations surface at that point rather than needing to be hand-enumerated in advance.

---

## 6. Self-Review

**Placeholder scan:** No TBDs. OpenIddict's unbounded columns are no longer an open item to be *discovered* at implementation time — §4.3 enumerates all six, verified directly against `openiddict-core` 7.5.0 source, with bounds assigned.

**Internal consistency:** §1 (placement) matches §3.1's two-call-site wiring (shared with §3), §4.1's two-tier interface/base-class split, and §4.4's matching two-call-site wiring for `ApplyNorseConfigurations()`. §3's whole-model, no-exemption scope is deliberately *not* mirrored by §3.1, which does carve out an exemption for un-owned types — §3.1 states explicitly why that asymmetry is correct (checking resolved metadata doesn't require declaration ownership; implementing an interface does) rather than leaving it to look like an inconsistency. §4.1's static-abstract choice is justified against the alternative (EF's own instance-based `IEntityTypeConfiguration<T>`) by the constructor-forcing problem it avoids, and that justification is carried through consistently into §4.4's generator mechanics (no instantiation, no constructor diagnostic).

**Scope:** Two realms (Urdarbrunnr, Himinbjörg), one coherent mechanism — enforcement machinery and its first real consumer, same shape as the migrations-framework spec's Urdarbrunnr/Himinbjörg split. §4.3's OpenIddict wrapper entities expand Himinbjörg's entity count (four new types) but not the realm split — they're the same colocation mechanism applied to a second brownfield library, not new scope. Not decomposed further; splitting would separate a rule from its only proof that it works.

**Ambiguity:** "Explicit" (§3) is defined precisely as "EF's resolved model metadata has a non-null max length" — not "an attribute is present," which is what makes inherited framework properties and OpenIddict's own configuration both fall correctly under the same check without special-casing. "Implements" (§3.1) is defined precisely as "the CLR type closes `INorseEntity<TSelf>` over itself" — the self-constraint on the interface means there is no ambiguous partial-implementation case to define away.
