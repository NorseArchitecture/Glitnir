# `Norse.Persistence.EntityFramework` — SequentialGuid Provider-Aware Converter

**Date:** 2026-07-26
**Status:** Implemented (Tasks 1-3 shipped across Urðarbrunnr/Himinbjörg; Task 4, added during final review to fix a `default(SequentialGuid)` hazard, is in progress in Svartálfheim). Plan: `../plans/2026-07-26-sequentialguid-provider-aware-ef-converter-plan.md`.
**Owner:** Buvy

**Amendment (2026-07-26):** `NorseModelConventions.Apply`'s `applyFixedLength` parameter has a
second real call site outside Urðarbrunnr — Himinbjörg's `NorseIdentityDbContext.ConfigureConventions`
(`Identity.Web.Server/NorseIdentityDbContext.cs:57-58`) calls it with the **named** argument
`applyFixedLength:`, since `NorseIdentityDbContext` inherits `IdentityDbContext` rather than
`NorseDbContext` and replicates its conventions manually. In scope, added below: a minimal
companion edit to Himinbjörg for whatever `Apply`'s new signature turns out to be — see the second
amendment immediately below, which supersedes this one's original plan to rename this parameter to
`isSqlServer`.

**Amendment 2 (2026-07-26, during Task 2 implementation):** Reusing one renamed `isSqlServer` bool
to drive both the fixed-length convention and the `SequentialGuid` converter selection was wrong —
caught mid-implementation, not at design time. The two facts are independent: "does fixed-length
storage help this provider" is a general RDBMS storage-engine question (SQL Server: yes; Postgres:
no, per its own docs; SQLite: the question doesn't even apply — no padding, no length-prefix
distinction, `CHAR`/`VARCHAR` are pure type-affinity hints; Oracle: probably no real performance win
either, per the well-known Ask Tom position on `CHAR` vs `VARCHAR2` — though that specific claim
wants a real citation before it's platform doctrine, not just recollection). "Does this provider
shuffle GUID bytes for `uniqueidentifier`-style comparison" is a SQL-Server-specific historical
quirk with no equivalent in any other engine — it will almost certainly stay `isSqlServer`-shaped
forever, unlike the fixed-length question. Collapsing both into one renamed bool made them look
like the same fact because they currently agree for the only two providers this platform has; they
aren't the same fact, and a future provider that decouples them would silently break whichever
behavior didn't win the argument.

**Corrected design:** `applyFixedLength` is not renamed and does not change meaning.
`NorseModelConventions.Apply` gains a second, independently-required parameter — the actual
`GuidByteOrder` enum, not another bool, since the enum is already the correct domain type and
avoids inventing a second boolean whose name would just restate "is this SQL Server" under a new
label:

```csharp
public static ModelConfigurationBuilder Apply(
	ModelConfigurationBuilder configurationBuilder,
	bool applyFixedLength,
	GuidByteOrder sequentialGuidOrder)
```

Both arguments are computed from the same `Database.ProviderName == SqlServerProviderName` check at
each call site today — deliberately duplicated, because it documents two separate facts that
currently agree rather than one fact wearing two hats. This also shrinks the Himinbjörg companion
edit: no rename there at all, only adding the new `sequentialGuidOrder:` argument to the existing
call.

**Amendment 3 (2026-07-26, real defect found while redoing Task 2):** `Properties<T>().HaveConversion(Type)`
constructs the registered converter type via `Activator.CreateInstance(Type)` (verified by
decompilation, not guessed) — the single-`Type`-argument overload, which requires a constructor with
true zero arity. Task 1's `Rfc9562SequentialGuidValueConverter(ConverterMappingHints? mappingHints = null)`
/ `SqlServerSequentialGuidValueConverter(ConverterMappingHints? mappingHints = null)` have arity 1 (one
parameter with a default value, not zero parameters) — `Activator.CreateInstance(Type)` does not
resolve default-valued parameters the way a compiler-side call site does, so construction throws
`MissingMethodException` at model-build time. Task 1's own tests never caught this because they
construct the converters directly (`new Rfc9562SequentialGuidValueConverter()`, a normal C# call site
that legally omits the defaulted argument) rather than through EF's reflection-based `HaveConversion(Type)`
path — the two are not equivalent.

**Fix:** drop the unused `mappingHints` parameter entirely (nothing in this design ever needs custom
mapping hints) and give each leaf converter a literal empty-parameter-list primary constructor, which
is a genuine zero-arity constructor:

```csharp
abstract class SequentialGuidValueConverter(GuidByteOrder expectedOrder) :
	ValueConverter<SequentialGuid, Guid>(
		guid => Guard(guid, expectedOrder),
		value => new SequentialGuid(value, expectedOrder))
{
	static Guid Guard(SequentialGuid guid, GuidByteOrder expectedOrder) => /* unchanged */;
}

sealed class Rfc9562SequentialGuidValueConverter() : SequentialGuidValueConverter(GuidByteOrder.Rfc9562);
sealed class SqlServerSequentialGuidValueConverter() : SequentialGuidValueConverter(GuidByteOrder.SqlServer);
```

This is a fix to Task 1's already-reviewed, already-committed code, landing as a follow-up commit
rather than reopening Task 1's whole review cycle — the change is additive/simplifying (removes an
unused parameter) and Task 1's existing 6 tests still hold once re-run against it.

**Amendment 4 (2026-07-26, found during the final whole-branch review):** `default(SequentialGuid)`
(`Order == GuidByteOrder.Unspecified`) is a live hazard, not a theoretical one. EF derives a default
`ValueComparer<SequentialGuid>` from `Equals`/`GetHashCode` when none is registered, and both call
`ToRfcOrder()`, which for a default value takes its "convert" branch and calls the two-arg
constructor on `Guid.Empty` — failing the RFC 9562 version-7 check and throwing a confusing
`ArgumentException` from two calls deep, rather than a clear diagnosis at the point of misuse. The
same dead end applies to `SequentialGuidValueConverter.Guard`'s own remediation advice
("call ToSqlOrder()/ToRfcOrder()") when the mismatched value handed to it is itself `default`.

Svartálfheim already has a house pattern for exactly this hazard class — `default(Result<T>)`/
`default(Failure)` are "malformed by construction," documented in XML remarks and pinned by canary
tests (Svartálfheim `CLAUDE.md`, Architecture Facts). `SequentialGuid` gets the same treatment:
not made to silently work on a default value, but made to fail immediately and clearly instead of
confusingly. Fix (Task 4, Svartálfheim): `ToRfcOrder()`/`ToSqlOrder()` throw `InvalidOperationException`
directly when `Order == GuidByteOrder.Unspecified`, before reaching the byte-shuffle/constructor
path. `Equals`/`GetHashCode` inherit the fix automatically since both already route through
`ToRfcOrder()`. `CompareTo` has a separate, narrower quirk (two `default` values compare as "equal"
without calling either conversion method) that was not flagged by the review and is deliberately
out of scope here — not a drive-by fix.

## Finding

Svartálfheim's `SequentialGuid`/`DeterministicGuid` are live but nothing downstream converts either to
a database column yet. Urðarbrunnr does not reference `Norse.Primitives` at all today.

Checking `DeterministicGuid` against Mímisbrunnr's already-shipped `CountryOrArea`/`Region` entities
(`Id`, `ParentRegionId` typed `DeterministicGuid`/`DeterministicGuid?`) shows it already round-trips
with **zero** converter code — the initial migration emits plain `uuid` columns with no
`HasConversion` anywhere in the repo. EF Core's automatic value-converter inference finds
`DeterministicGuid`'s `public static implicit operator Guid(...)` for the write side and its
single-arg `public DeterministicGuid(Guid value)` constructor for the read side, and wires the
conversion up on its own. Decided: leave this alone. Confirmed working, not in scope.

`SequentialGuid` has no single-arg `Guid` constructor — only the two-arg
`SequentialGuid(Guid value, GuidByteOrder order)` — so EF's inference can't find a reverse path.
It needs a hand-written `ValueConverter<SequentialGuid, Guid>`, and that converter is exactly where
the SQL-Server-byte-order concern has to be enforced anyway: `GuidByteOrder` (`Rfc9562` /
`SqlServer`) is a real tag on the value (`SequentialGuid.Order`, `ToSqlOrder()`/`ToRfcOrder()`,
Svartálfheim's `../Svartalfheim/specs/2026-07-03-svartalfheim-identifiers-design.md`), and SQL
Server's `uniqueidentifier` sort order famously disagrees with RFC 9562's own byte order. The
platform's stance, decided here: **never silently reshuffle**. A `SequentialGuid` presented to the
wrong provider in the wrong byte order is a bug, and the converter throws immediately rather than
"fixing" it — silent reshuffling would make debugging a genuine nightmare (indistinguishable-looking
GUIDs that sort differently than the value the caller thinks they set).

## Scope

**In scope (Himinbjörg, minimal companion edit only):**
- `NorseIdentityDbContext.cs` — add the new `sequentialGuidOrder:` argument to the existing
  `NorseModelConventions.Apply(...)` call. `applyFixedLength:` is untouched — see Amendment 2.
  Nothing else in Himinbjörg changes.

**In scope (Urðarbrunnr):**
- `Norse.Persistence.EntityFramework` takes on `Norse.Primitives` as a dependency (via `NorseRef`,
  dev-mode `ProjectReference` — `Norse.Primitives` has no NuGet package yet).
- A `SequentialGuid` ↔ `Guid` `ValueConverter`, provider-aware, registered model-wide so any entity
  property typed `SequentialGuid` gets it automatically — no per-property `.HasConversion()` calls
  in consuming realms.
- The converter throws `InvalidOperationException` on write when `SequentialGuid.Order` doesn't
  match the destination provider's expected order (SQL Server expects `SqlServer`; every other
  provider expects `Rfc9562`).

**Out of scope:**
- `DeterministicGuid` — already works, not touched.
- Read-side order validation — `GuidByteOrder` is documented as not recoverable from raw bytes
  alone (`GuidByteOrder.cs` remarks); a converter reading from a known provider tags the value with
  that provider's expected order and trusts it, exactly as `SequentialGuid`'s own constructor
  contract already assumes for any caller wrapping a value it didn't generate itself.
- Actually exercising this against a running SQL Server instance — no entity uses `SequentialGuid`
  yet anywhere on the platform; this spec lands the mechanism ahead of its first consumer, mirroring
  how the SQL Server fixed-length trio landed ahead of Himinbjörg proving it live.

## Design

### 1. Dependency

`Urdarbrunnr/src/Persistence.EntityFramework/Persistence.EntityFramework.csproj` gains:

```xml
<NorseRef Include="Primitives">
	<Repo>Svartalfheim</Repo>
</NorseRef>
```

Matches Asgard's and Mímisbrunnr's existing `NorseRef` usage for the same package exactly — resolves
to a `ProjectReference` under `$(UseProjectReferences)` (Bifröst dev mode, the only mode that exists
today).

### 2. Converter types (`Norse.Persistence.EntityFramework`, internal)

An abstract base carries the one guard both directions share; two sealed leaves supply the expected
order. Both leaves need the `ConverterMappingHints?`-only constructor shape EF Core's
`Properties<T>().HaveConversion(Type)` requires (the built-in-converter shape), so the expected
order can't be a converter constructor parameter — it's baked into the leaf type instead:

```csharp
abstract class SequentialGuidValueConverter(GuidByteOrder expectedOrder, ConverterMappingHints? mappingHints = null)
	: ValueConverter<SequentialGuid, Guid>(
		guid => Guard(guid, expectedOrder),
		value => new SequentialGuid(value, expectedOrder),
		mappingHints)
{
	static Guid Guard(SequentialGuid guid, GuidByteOrder expectedOrder) =>
		guid.Order == expectedOrder
			? guid.Value
			: throw new InvalidOperationException(
				$"SequentialGuid is in {guid.Order} byte order but this provider requires {expectedOrder}. " +
				$"Call {(expectedOrder == GuidByteOrder.SqlServer ? "ToSqlOrder()" : "ToRfcOrder()")} explicitly " +
				"before assigning — this converter never silently reshuffles.");
}

sealed class Rfc9562SequentialGuidValueConverter(ConverterMappingHints? mappingHints = null)
	: SequentialGuidValueConverter(GuidByteOrder.Rfc9562, mappingHints);

sealed class SqlServerSequentialGuidValueConverter(ConverterMappingHints? mappingHints = null)
	: SequentialGuidValueConverter(GuidByteOrder.SqlServer, mappingHints);
```

Both are `internal` — nothing outside this assembly ever names them; an entity just declares a
`SequentialGuid`-typed property and the model-wide convention (below) picks the right one up.

### 3. Wiring

`NorseModelConventions.Apply`'s `applyFixedLength` parameter is renamed `isSqlServer` — it already
carries exactly the signal the new converter selection needs, so it drives both instead of each
call site recomputing its own provider check:

```csharp
public static ModelConfigurationBuilder Apply(ModelConfigurationBuilder configurationBuilder, bool isSqlServer)
{
	configurationBuilder.Conventions.Add(_ => new RequireExplicitLengthConvention(isSqlServer));
	configurationBuilder.Conventions.Add(static _ => new RequireEntityConfigurationConvention());
	configurationBuilder.Properties<SequentialGuid>().HaveConversion(
		isSqlServer ? typeof(SqlServerSequentialGuidValueConverter) : typeof(Rfc9562SequentialGuidValueConverter));
	return configurationBuilder;
}
```

`NorseDbContext.ConfigureConventions`'s call site changes only its argument label
(`applyFixedLength:` → `isSqlServer:`); the `Database.ProviderName ==
NorseDbContextOptionsExtensions.SqlServerProviderName` check it already computes is reused as-is —
no new provider-detection logic anywhere.

### 4. Testing

No database connection needed anywhere:

- Direct unit tests against `Rfc9562SequentialGuidValueConverter` and
  `SqlServerSequentialGuidValueConverter`: compile `ConvertToProviderExpression` /
  `ConvertFromProviderExpression` and assert pass-through on matching order,
  `InvalidOperationException` on mismatch, both converters, both directions.
- One model-building test per provider, mirroring
  `RequireExplicitLengthConventionTests`'s existing Sqlite-vs-fake-SqlServer-connection-string
  harness (`CreateContext<T>()` / `CreateSqlServerContext<T>()` — the SQL Server case never opens a
  real connection; building `ctx.Model` is enough to select a provider without contacting one):
  build a throwaway `NorseDbContext` with a `SequentialGuid`-typed entity property, inspect
  `property.GetValueConverter()!.GetType()`, and confirm the right converter got selected per
  provider.
