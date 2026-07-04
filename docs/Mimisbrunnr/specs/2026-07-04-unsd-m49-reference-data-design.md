# UN M49 Reference Data — Region Hierarchy, CountryOrArea, and the Dossier View

**Date:** 2026-07-04
**Status:** Approved design, ready for planning
**Owner:** Buvy
**Companion specs:**
- `2026-07-03-seeding-framework-design.md` — `ISeedContributor`, `DeterministicGuid` (Asgard), the pattern this spec's seed contributor implements against.
- `2026-07-03-svartalfheim-identifiers-design.md` — the real `DeterministicGuid` struct (§4) this spec consumes for every entity's primary key.

---

## 0. Why This Comes Next

Mimisbrunnr is currently a bare shell — no code, no migrations, no seed data. This is its first seed case, sourced from the UN Statistics Division's M49 standard (`seeds/raw/UNSD — Methodology.csv`, downloaded from https://unstats.un.org/unsd/methodology/m49/overview/). It exists to finish the seed story end to end: an EF entity model, a migration, a seed contributor, and a query-optimized read view — the first realm to actually consume `DeterministicGuid` for real entity primary keys, and the reference-data template every future bounded context's own reference data points at.

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
    public Region? ParentRegion { get; init; } = null!;

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
    public Region? ParentRegion { get; init; } = null!;
    public bool IsLeastDevelopedCountry { get; init; }
    public bool IsLandLockedDevelopingCountry { get; init; }
    public bool IsSmallIslandDevelopingState { get; init; }

    static void Configure(EntityTypeBuilder<CountryOrArea> builder) { /* PK, three unique indexes, FK to Region */ }
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

## 5. JSONB Query-Optimized Dossier View

A Postgres view, `country_or_area_dossier` — one JSONB document per `CountryOrArea`, nesting the ancestor chain to mirror its real parent-child shape (Region contains Subregion contains IntermediateRegion) rather than flattening it into parallel sibling keys. Field names drop standard-provenance qualifiers (`m49`/`iso`) entirely — a consumer asking for a country's dossier doesn't need to know which standard minted which code:

```json
{
  "code": "566", "alpha2": "NG", "alpha3": "NGA", "name": "Nigeria",
  "isLeastDevelopedCountry": false, "isLandLockedDevelopingCountry": false, "isSmallIslandDevelopingState": false,
  "region": {
    "code": "002", "name": "Africa",
    "subregion": {
      "code": "202", "name": "Sub-Saharan Africa",
      "intermediateRegion": { "code": "011", "name": "Western Africa" }
    }
  }
}
```

Algeria (no Intermediate Region — Northern Africa has none) nests only two levels deep, `"intermediateRegion": null`. Antarctica (no ancestor at all) has `"region": null` and nothing further.

Built by walking `Region`'s self-reference from each leaf's `ParentRegionId` upward — three `LEFT JOIN`s keyed by `Level` (Region/Subregion/IntermediateRegion), not a recursive CTE, since the depth is fixed at three and never varies. Indexed on `code`, `alpha2`, `alpha3` for direct dossier lookups with no join at read time.

This view answers "give me everything about country X" — a per-region rollup (one document per Region embedding its descendant countries) was considered and explicitly not built; no read pattern needs it yet.

---

## 6. Explicitly Out of Scope

- **Snowflake export shape.** Mentioned only as a future consumer, not designed here. The normalized shape (Region hierarchy + `CountryOrArea` with M49-code FKs) flattens cleanly into a wide table later via the same joins that build the dossier view — nothing here forecloses that.
- **Richer ISO 3166 country attributes** (official name, independence status, active/historical status) from the README's original ERD sketch. This source file doesn't carry them; a later seed case against a separate ISO 3166 source adds them, kept honest to "no data not backed by a source."
- **Mímir's consumption of this data** (Blazor components, gRPC service, background worker). This spec covers `Norse.ReferenceData.Data` only.
- **A type-enforced deterministic-ID struct beyond what Svartalfheim already ships.** `DeterministicGuid` (the real struct, not the Asgard stopgap it superseded) is already the strong version; nothing further needed here.
- **Collapsing the three classification booleans into a `[Flags]` enum, wire-projected as an array.** Raised in discussion as a plausible alternative to three independent `bool` columns/fields; not adopted here. A separate debate once there's a concrete reason to prefer it over three named booleans.

---

## 7. Success Criteria

- `dotnet run` against the migrations service stands up `Region` and `CountryOrArea` tables in Mimisbrunnr's database and seeds all 248 `CountryOrArea` rows plus their deduplicated `Region` ancestors, with zero rows requiring a null `M49Code`/ISO code.
- Re-running the seed contributor against an already-seeded database is a no-op (idempotency proven by primary-key lookup, not a full content diff).
- `country_or_area_dossier` returns the correct nested shape for all three verified cases: a country with an Intermediate Region (Nigeria), a country with only Region/Subregion (Algeria), and the one country with no ancestor at all (Antarctica).
- Every `Region`/`CountryOrArea` row's `Id` is reproducible: re-running the CSV→TSV conversion and reseeding from scratch produces byte-identical GUIDs for every row.

---

## Self-Review

**Placeholder scan:** No TBDs. §6 enumerates deferred items with the reason each is deferred, rather than leaving gaps.

**Internal consistency:** §2's adjacency-list `Region` design is what makes §1's Antarctica edge case (no ancestor at all) representable without a special-case column; §3's namespace-per-entity-type scheme is applied consistently to both entities; §5's nesting order (Region → Subregion → IntermediateRegion) matches §2's `RegionLevel` enum order exactly.

**Scope:** Two entities and one read view, sourced from one file already on disk. Deliberately excludes the fuller README ERD (currency, language, script, locale, timezone, subdivision) — those are unconverged, separate future seed cases, not part of this one.

**Ambiguity:** §3 states explicitly that GUIDs are computed at seed time from the TSV's M49 code and never stored precomputed, closing off the alternative reading where the TSV might carry a precomputed `Id` column. §4 states explicitly that `Region` seeds before `CountryOrArea` within one contributor, closing off ambiguity about ordering given the seeding framework's own no-`Order`-no-`DependsOn` rule (§1.3 of the companion spec) applies *between* contributors, not *within* one.
