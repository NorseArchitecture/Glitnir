# UN M49 Reference Data — Region Hierarchy, CountryOrArea, and the `View` Column

**Date:** 2026-07-04
**Status:** Approved design, ready for planning
**Owner:** Buvy
**Revision (same day):** §5 originally specified a hand-written Postgres view (`country_or_area_dossier`). Buvy rejected maintaining raw SQL views ("I don't think we should be creating views I really don't want to maintain that dogwater") after §5 was already implemented and live-verified. §5 below replaces the view with an EF Core native owned-JSON column (`.ToJson()`) on `CountryOrArea` itself, hydrated in C# by the seed contributor rather than derived by the database at read time. Buvy flagged this revision as a first exploration toward a larger CQRS idea he's still developing (JSON-column writes on the command side, hydrating the system of record separately) — treat this design as likely to keep moving, not as a settled template, until that larger idea is itself specced.
**Revision 2 (2026-07-05):** the column/property was renamed from `RegionAncestry` to `View` — see §5's naming note. This is the platform's first instance of a pattern Buvy intends to generalize (every "many side" entity gets its own `View`); the rename happened after proving the mechanism worked, per his own instruction to "carry forwards, prove the system out, and then we can have the naming discussion."
**Companion specs:**
- `2026-07-03-seeding-framework-design.md` — `ISeedContributor`, `DeterministicGuid` (Asgard), the pattern this spec's seed contributor implements against.
- `2026-07-03-svartalfheim-identifiers-design.md` — the real `DeterministicGuid` struct (§4) this spec consumes for every entity's primary key.

---

## 0. Why This Comes Next

Mimisbrunnr is currently a bare shell — no code, no migrations, no seed data. This is its first seed case, sourced from the UN Statistics Division's M49 standard (`seeds/raw/UNSD — Methodology.csv`, downloaded from https://unstats.un.org/unsd/methodology/m49/overview/). It exists to finish the seed story end to end: an EF entity model, a migration, a seed contributor, and a query-optimized owned-JSON column — the first realm to actually consume `DeterministicGuid` for real entity primary keys, and the reference-data template every future bounded context's own reference data points at.

The README's earlier ERD sketch (region/country/currency/language/timezone/etc.) was a hypothetical exploration of where Mimisbrunnr might eventually go, not a binding shape. This spec supersedes it for the two entities it actually covers (`Region`, `CountryOrArea`); the rest of that sketch remains unconverged and out of scope here.

---

## 1. Source of Record

The raw file stays exactly where it landed — `seeds/raw/UNSD — Methodology.csv` — as permanent provenance. It is **never parsed directly by the seed contributor.** A one-time, human-run conversion step produces two derived TSVs (matching this realm's established convention: nietras Sep, AOT-friendly) that the seed contributor actually reads:

- `seeds/region.tsv`
- `seeds/country-or-area.tsv`

**Data facts that shaped this design** (verified against all 248 data rows of the source CSV):

- `Global Code`/`Global Name` (`001`/`World`) is constant across every row — zero discriminating information. Not modeled.
- Region (5 values), Sub-region (17 values), and Intermediate Region (7 values, present on only 105/248 rows) form a strict, ragged-depth tree. Antarctica is the sole row with no Region, Sub-region, or Intermediate Region at all — straight from nothing to leaf.
- M49 codes for Region, Sub-region, Intermediate Region, and each country/area's own M49 code are **pairwise disjoint** — one shared, collision-free numeric code space. A bare M49 code is therefore a safe natural key with no level discriminator needed.
- ISO-alpha2 and ISO-alpha3 codes are present and unique for all 248 rows; no duplicates anywhere.
- Three boolean classification flags (Least Developed Country, Land Locked Developing Country, Small Island Developing State) are `x`/blank per row.

---

## 2. Entities

Two entities, both in `Norse.ReferenceData.Data`, both Tier 1 (`NorseEntityBase<TSelf>` + `INorseEntity<TSelf>`, per Urdarbrunnr's convention):

**Amendment (2026-07-25):** `Norse.ReferenceData.Data` (here and at §6 below) is the working-title namespace; shipped source renamed the realm to `Norse.Reference.Data`. See Mímisbrunnr's current source and `docs/Mimisbrunnr/plans/2026-07-22-reference-data-migrations-project-split-design.md`.

```csharp
namespace Norse.ReferenceData.Data;

public enum RegionLevel
{
    Region = 1,
    Subregion = 2,
    IntermediateRegion = 3,
}

sealed class Region : NorseEntityBase<Region>, INorseEntity<Region>
{
    public Guid Id { get; init; }
    public string M49Code { get; init; } = null!;      // char(3), unique
    public string Name { get; init; } = null!;
    public RegionLevel Level { get; init; }
    public Guid? ParentRegionId { get; init; }          // null for the 5 top-level Regions
    public Region ParentRegion { get; init; } = null!;  // FK nullability alone governs optionality — nav is never `Region?`

    static void Configure(EntityTypeBuilder<Region> builder) { /* PK, unique index on M49Code, self-FK */ }
}

sealed class CountryOrArea : NorseEntityBase<CountryOrArea>, INorseEntity<CountryOrArea>
{
    public Guid Id { get; init; }
    public string M49Code { get; init; } = null!;       // char(3), unique
    public string IsoAlpha2Code { get; init; } = null!; // char(2), unique
    public string IsoAlpha3Code { get; init; } = null!; // char(3), unique
    public string Name { get; init; } = null!;
    public Guid? ParentRegionId { get; init; }          // null only for Antarctica
    public Region ParentRegion { get; init; } = null!;  // FK nullability alone governs optionality — nav is never `Region?`
    public bool IsLeastDevelopedCountry { get; init; }
    public bool IsLandLockedDevelopingCountry { get; init; }
    public bool IsSmallIslandDevelopingState { get; init; }
    public RegionNode? View { get; init; }              // see §5 — owned-JSON column, hydrated by the seed contributor

    static void Configure(EntityTypeBuilder<CountryOrArea> builder) { /* PK, three unique indexes, FK to Region, View.ToJson() */ }
}
```

Naming decisions made explicitly, not by default:

- **`Region`**, not `M49Region` or `GeoRegion` — shortest option, matches the CSV's own column names most literally. Accepted risk: a future generic "region" concept elsewhere in the platform would need to pick a different name to avoid colliding with this one.
- **`CountryOrArea`**, not `Country` — this CSV's own header calls it "Country or Area," and M49 deliberately includes non-sovereign territories (Antarctica, dependent territories) with no field in this source distinguishing them from sovereign countries. Naming it `Country` would overclaim.
- Boolean flags are spelled out in full (`IsLeastDevelopedCountry`, not `IsLdc`) — a future reader shouldn't need UN classification jargon to read the entity.
- `Region` is self-referencing (adjacency list: `ParentRegionId` + `Level` enum) rather than three explicit tables (`Region`/`Subregion`/`IntermediateRegion`) — this is what lets Antarctica's ragged depth (leaf with no ancestor at all) fall out naturally instead of needing three nullable FKs on `CountryOrArea`.
- No row is modeled for the constant `World` value — `CountryOrArea.ParentRegionId` is simply null for Antarctica, rather than every leaf carrying a non-null ancestor chain up to a row that never discriminates anything.

---

## 3. GUID / Namespace Scheme

Every row's primary key is a `Guid` derived via `DeterministicGuid`, not a natural-key PK and not `Guid.NewGuid()`. Two namespace GUIDs, one per entity type, each itself derived once (via the RFC well-known DNS namespace) and then frozen as a literal constant — reproducible from source, not an opaque hand-rolled random value:

```csharp
static readonly Guid NamespaceRegion =
    new DeterministicGuid(Namespaces.Dns, "region.m49.referencedata.norse");

static readonly Guid NamespaceCountryOrArea =
    new DeterministicGuid(Namespaces.Dns, "country-or-area.m49.referencedata.norse");
```

Every row's `Id` is then `new DeterministicGuid(NamespaceX, m49Code)` — computed at seed time from the M49 code, **never stored precomputed** in the TSV. The M49 code is the single source of truth; the `Guid` is a derived, reproducible surrogate that is identical across local dev, staging, and production for the same seed data. This also makes `ISeedContributor.SeedAsync`'s required idempotency check (§1.4 of the seeding-framework spec) a primary-key lookup rather than a content comparison.

---

## 4. Seed Contributor and TSV Shape

`seeds/region.tsv` — one row per distinct Region/Sub-region/Intermediate-Region node (deduplicated out of the CSV's repeating columns), columns: `M49Code`, `Name`, `Level`, `ParentM49Code` (blank for the 5 top-level Regions).

`seeds/country-or-area.tsv` — one row per CSV row, columns: `M49Code`, `IsoAlpha2Code`, `IsoAlpha3Code`, `Name`, `ParentM49Code` (blank only for Antarctica), `IsLeastDevelopedCountry`, `IsLandLockedDevelopingCountry`, `IsSmallIslandDevelopingState`.

The seed contributor reads both TSVs (nietras Sep), resolves `ParentM49Code` to a `Guid` via the same `DeterministicGuid` formula applied to the parent's own M49 code (never a stored FK GUID), and upserts by `Id`. `Region` rows must seed before `CountryOrArea` rows within the same contributor's `SeedAsync` — internal ordering the contributor owns itself, per §1.3 of the seeding-framework spec (no cross-contributor `Order`/`DependsOn`).

The CSV→TSV conversion itself is a one-time, human-run step (not part of the runtime seed path) — out of scope for this spec to script, since it runs once against a static source file.

---

## 5. `View` — Owned-JSON Column on `CountryOrArea`

**No database view.** The ancestor chain lives as a JSON column directly on `CountryOrArea` itself, mapped via EF Core's native owned-entity-to-JSON support (`.ToJson()`, confirmed present and Npgsql-supported in the platform's installed EF Core 10.0.9 / Npgsql.EntityFrameworkCore.PostgreSQL 10.0.2) — hydrated in C# by the seed contributor at seed time, never derived by the database at read time.

**Naming — deliberate, not a placeholder (revised 2026-07-05):** the property and column are named `View`, a deliberate double homage — to the SQL `VIEW` this design replaces ("we used to need a database view for this; now it's just a column called `View`"), and to the "view model" language Buvy used while describing the C# object he wanted hydrated and inserted. Buvy has signaled this is the first instance of a platform-wide pattern — every entity on the "many side" of a hierarchy is a candidate for its own `View` column, holding only its ancestor/peer data, enabling reads that filter/sort against the entity's own indexed relational columns and then "snap in" each row's `View` for display with no joins. `RegionNode`/`SubregionNode`/`IntermediateRegionNode` keep their existing, level-specific names — only the top-level property/column changed; the pattern generalizes by property name (`View`), not by inventing a new wrapper type per entity.

```csharp
sealed class RegionNode
{
    public string Code { get; init; } = null!;
    public string Name { get; init; } = null!;
    public SubregionNode? Subregion { get; init; }
}

sealed class SubregionNode
{
    public string Code { get; init; } = null!;
    public string Name { get; init; } = null!;
    public IntermediateRegionNode? IntermediateRegion { get; init; }
}

sealed class IntermediateRegionNode
{
    public string Code { get; init; } = null!;
    public string Name { get; init; } = null!;
}
```

**Design rule this follows — the "many side" heuristic, refined 2026-07-06:** a denormalized ancestor/peer document belongs on the entity at the *many* side of a one-to-many hierarchy (each row has exactly one bounded upward path). `CountryOrArea` is the many side relative to `Region`, so `View` lives there; `Region` never gets a reciprocal "my descendant countries" JSON column.

To-one relationships (ancestors, peers — anywhere the cardinality is exactly one) are **unconditional**: always bring them in. To-many relationships (children, dependents) are **not** universally excluded — that was true for `CountryOrArea`/`Region` only because the one candidate collection (a Region's descendant countries) was rejected on cardinality/utility grounds, not because collections are forbidden by construction. The real rule is a per-relationship judgment call: would this collection realistically be small and something a consumer actually wants alongside the row, or is it unbounded/rarely useful? Buvy's own example: an ASP.NET Identity `User`'s `View` should embed their `Roles` (small, bounded, exactly what you want when you load a user) — but a `Role`'s `View` should never embed "every `User` in this role" (unbounded, and nobody looks at a role wanting that). Decide case by case, per relationship, not with a blanket to-many ban.

This means a future generic "projection expression" helper over any entity's `View` cannot assume collections never appear — it must support narrowing into an already-embedded bounded collection (select fields off each element) while still only ever *whittling down* what's already materialized in the JSON, never triggering a new join/query to expand into something that wasn't hydrated in.

**Contents are the ancestor chain only — not a self-contained document.** `View` does not repeat `CountryOrArea`'s own `M49Code`/`IsoAlpha2Code`/`IsoAlpha3Code`/`Name`/the three booleans — those are already plain relational columns on the same row, and duplicating them into JSON would add nothing. A caller wanting "everything about country X" as one document combines the row's own columns with `View` in C#, not via a single database-side projection.

**Two more rules for the generalized pattern (not yet exercised by this entity, recorded for the next one that is):**
- **`View` is always current state, independent of any temporal/audit setup on the base table.** If an entity's table is system-versioned (e.g. a future SQL Server temporal table) or otherwise carries history, that history lives on the base table/its temporal shadow — `View` never becomes a point-in-time or historical document itself, and never needs duplicating per history row.
- **Shadow properties and audit columns (`CreatedAt`, `ModifiedBy`, row-version tokens, etc.) never belong inside `View`.** Beyond simply being irrelevant to peer/ancestry business data, shadow properties have no CLR member on the entity class — a projection expression written against the class shape structurally cannot reference them, so including them would be dead weight with no path to ever being read back out.

**EF configuration** (inside `CountryOrArea.Configure`):

```csharp
builder.OwnsOne(c => c.View, region =>
{
    region.ToJson();
    region.OwnsOne(r => r.Subregion, sub => sub.OwnsOne(s => s.IntermediateRegion));
});
```

`.ToJson()` is called once, on the outermost owned type — nested owned types in the same chain map into the same JSON column automatically.

**Hydration happens in the seed contributor, in C#** — not the database. After `Region` rows are resolved (§4), the seed contributor walks each `CountryOrArea`'s parent chain upward through the in-memory region rows to build its `RegionNode` graph, and sets `View` on the entity before `Add`. Algeria's `View.Subregion.IntermediateRegion` is `null` (Northern Africa has none); Antarctica's `View` is `null` entirely (no ancestor at all).

**Querying is a plain LINQ projection, translated to native JSON path expressions server-side** — on both Npgsql and SQL Server's EF provider, not a hand-written view or raw SQL:

```csharp
context.Set<CountryOrArea>()
    .Where(c => c.M49Code == code)
    .Select(c => c.View);
```

A caller can project into a specific nested field (`.Select(c => c.View!.Subregion!.Name)`) without hydrating the whole graph — this is the "projection expression filter down the data" capability the raw-SQL view could never offer without hand-written JSON-path SQL of its own.

---

## 6. Explicitly Out of Scope

- **Snowflake export shape.** Mentioned only as a future consumer, not designed here. The normalized shape (Region hierarchy + `CountryOrArea` with M49-code FKs) flattens cleanly into a wide table later — nothing here forecloses that.
- **Richer ISO 3166 country attributes** (official name, independence status, active/historical status) from the README's original ERD sketch. This source file doesn't carry them; a later seed case against a separate ISO 3166 source adds them, kept honest to "no data not backed by a source."
- **Mímir's consumption of this data** (Blazor components, gRPC service, background worker). This spec covers `Norse.ReferenceData.Data` only.
- **A type-enforced deterministic-ID struct beyond what Svartalfheim already ships.** `DeterministicGuid` (the real struct, not the Asgard stopgap it superseded) is already the strong version; nothing further needed here.
- **Collapsing the three classification booleans into a `[Flags]` enum, wire-projected as an array.** Raised in discussion as a plausible alternative to three independent `bool` columns/fields; not adopted here. A separate debate once there's a concrete reason to prefer it over three named booleans.
- **The CQRS command-side JSONB pattern this revision is a proving ground for** (insert a request into a JSONB column for a fast acknowledgment, hydrate the system of record separately). Buvy raised it explicitly as motivating context for why `View` is designed as an EF-native JSON column rather than a view — not as a requirement this spec solves. Getting the read/query side right here is the prerequisite; the write-side idea gets its own spec once it's actually designed.
- **A generalized, generator-assisted `View` pattern for other entities**, and a generic "projection expression" helper for querying into any entity's `View`. Both are explicitly deferred until a second real consumer exists — this spec proves the pattern once, concretely, by hand; generalizing it is future work, not scope creep to absorb here.

---

## 7. Success Criteria

- `dotnet run` against the migrations service stands up `Region` and `CountryOrArea` tables in Mimisbrunnr's database and seeds all 248 `CountryOrArea` rows plus their deduplicated `Region` ancestors, with zero rows requiring a null `M49Code`/ISO code.
- Re-running the seed contributor against an already-seeded database is a no-op (idempotency proven by primary-key lookup, not a full content diff).
- `CountryOrArea.View` carries the correct nested shape for all three verified cases: a country with an Intermediate Region (Nigeria), a country with only Region/Subregion (Algeria, `IntermediateRegion` null), and the one country with no ancestor at all (Antarctica, `View` null).
- Every `Region`/`CountryOrArea` row's `Id` is reproducible: re-running the CSV→TSV conversion and reseeding from scratch produces byte-identical GUIDs for every row.

---

## Self-Review

**Placeholder scan:** No TBDs. §6 enumerates deferred items with the reason each is deferred, rather than leaving gaps.

**Internal consistency:** §2's adjacency-list `Region` design is what makes §1's Antarctica edge case (no ancestor at all) representable without a special-case column; §3's namespace-per-entity-type scheme is applied consistently to both entities; §5's `RegionNode`/`SubregionNode`/`IntermediateRegionNode` nesting order matches §2's `RegionLevel` enum order exactly, and §5's "many side" heuristic is consistent with §2's own choice to put `ParentRegionId` on the many side (`CountryOrArea`/`Region` pointing up) rather than a collection on the one side.

**Scope:** Two entities and one owned-JSON column, sourced from one file already on disk. Deliberately excludes the fuller README ERD (currency, language, script, locale, timezone, subdivision) — those are unconverged, separate future seed cases, not part of this one.

**Ambiguity:** §3 states explicitly that GUIDs are computed at seed time from the TSV's M49 code and never stored precomputed, closing off the alternative reading where the TSV might carry a precomputed `Id` column. §4 states explicitly that `Region` seeds before `CountryOrArea` within one contributor, closing off ambiguity about ordering given the seeding framework's own no-`Order`-no-`DependsOn` rule (§1.3 of the companion spec) applies *between* contributors, not *within* one.
