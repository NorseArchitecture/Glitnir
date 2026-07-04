# Tabular Ingestion and Seed Tooling — `Norse.Primitives.Ingestion` and Mimisbrunnr's `SeedTool`

**Date:** 2026-07-04
**Status:** Approved design, ready for planning
**Owner:** Buvy
**Companion specs:**
- `2026-07-03-seeding-framework-design.md` — `ISeedContributor`, `DeterministicGuid`; the runtime seed contributor this tool's output feeds.
- `2026-07-04-unsd-m49-reference-data-design.md` — the first concrete consumer. §1 there called the CSV→TSV conversion "a one-time, human-run step... out of scope for this spec to script." This spec reopens that call, because the same conversion problem recurs for every future reference-data source (ISO 3166, currency, language, tzdata all named as later seed cases in that spec's §6), and is worth tooling once rather than hand-rolling per source.

---

## 0. Why This Comes Next

Svartalfheim's `Norse.Primitives` forge already handles scalar→domain conversion (`Result<T>`, the parser gateway) for values that are already isolated as text. It has no opinion on how that text gets isolated from a file in the first place — that's a different concern, deliberately kept out of the zero-dependency forge project. Every realm that seeds reference data from an external source (Mimisbrunnr today; future contexts later) needs to turn a raw source file into the curated TSVs its seed contributor reads, and that step recurs per source, per realm. Building it once, generically, avoids re-deriving the same delimited/spreadsheet-reading choreography by hand each time.

This is a two-realm effort, shipped in dependency order:

1. **Svartalfheim** ships `Norse.Primitives.Ingestion` — a new, opt-in project wrapping Sep (delimited) and Sylvan.Data.Excel (single-sheet Excel) behind one canonical forward-only reader contract.
2. **Mimisbrunnr** ships `tools/SeedTool` — a dev-only console app that consumes it, with the UN M49 CSV→TSV conversion as its first concrete mapper.

---

## 1. Decisions in Force

### 1.1 `Ingestion` is a sibling project to `Primitives`, not a dependency of it, in either direction

`Norse.Primitives.Ingestion` takes on Sep and Sylvan.Data.Excel as dependencies; `Norse.Primitives` (the core forge) stays at zero dependencies. Ingestion does not reference Primitives either — it only deals in raw cell spans (`ReadOnlySpan<char>`); turning a span into a typed value via `Result<T>`/`Parser` is composed by the caller, not baked into the reader. This keeps each project "smart about one thing": Ingestion gets rows out of a file; Primitives turns text into typed values. A consumer who only needs scalar parsing never pulls in Sep or Sylvan.Data.Excel transitively.

### 1.2 One canonical contract, two backends

```csharp
namespace Norse.Primitives.Ingestion;

public interface ITabularReader : IDisposable
{
    int FieldCount { get; }
    int Ordinal(string headerName);          // resolved once; cached by the caller for the hot loop
    bool Read();                              // forward-only; false at end of data
    ReadOnlySpan<char> this[int ordinal] { get; }
}
```

- `SepTabularReader` — wraps Sep for delimited files (csv/tsv). Cell access is genuinely zero-alloc; Sep hands spans over a buffer it already owns.
- `ExcelTabularReader` — wraps Sylvan.Data.Excel's `ExcelDataReader` for a single sheet. `DbDataReader`'s typed getters mean a cell's text is materialized as a `string` internally before this contract exposes it as a span (`string.AsSpan()`). This is a documented asymmetry, not a defect: Excel doesn't store cell text as slices of a flat character stream the way delimited text does, so the zero-alloc guarantee Sep gives for free doesn't carry over. Both implementations are `internal sealed`, reachable only through the interface.
- **Single sheet, single source, forward-only only.** No `NextResult()`/multi-sheet navigation, no seeking backward — deliberately minimal, matching the one concrete need (read one sheet, rapid-fire, front to back). A multi-sheet contract waits for an actual consumer that needs it.

### 1.3 Structural failures throw; scalar failures are `Result<T>`

Two different failure classes, two different mechanisms, never conflated:

- **Structural failures** (malformed delimited row, corrupt xlsx, wrong column count) are `ITabularReader`'s own concern and **throw** — they are not a `Result<T>` case. Sep and Sylvan.Data.Excel already throw on structurally invalid input; `Ingestion` does not catch and re-wrap these as `Result<T>`, consistent with platform-wide "no catch-all that swallows."
- **Scalar-value failures** (a cell's text doesn't parse as the declared type) flow through the existing `Parser.ParseRequired<T>`/`ParseOptional<T>` gateway into `Result<T>`, exactly as every other untrusted-input boundary in the platform already works. `Ingestion` introduces no new failure vocabulary — it reuses Svartalfheim's existing one at the point where a caller chooses to parse a cell.

### 1.4 Fail-loud, no partial output, no skip

A mapper (e.g. `UnsdM49Mapper`) that receives a `Failure` from `Parser.ParseRequired<T>` throws immediately, naming the source row, column, and reason — never a silently skipped row, never a default value, never a partially written output file. If any row fails, nothing is written. This mirrors the migrations/seeding framework's own "no partial migration, no partial seed" discipline, applied to the tool that feeds it.

### 1.5 `SeedTool` is dev-only, never AOT-published, never packed

Confirmed against Yggdrasil's `Hosting.Migrations.Service` (the actual runtime consumer of seed data): it runs as a regular worker container, not Native AOT. `SeedTool` is a human-run local utility that regenerates TSVs when a raw source changes — it is not part of any runtime path, is not packed, and is not itself required to publish under Native AOT. `Norse.Primitives.Ingestion` still gets the AOT-clean treatment (§3), because it's a Svartalfheim forge project and every forge project earns that discipline regardless of whether today's one consumer happens to need it.

### 1.6 `SeedTool` lives in Mimisbrunnr, per-realm, not a shared platform tool

The mapping logic (which raw columns become which TSV columns, how the Region tree gets deduplicated) is domain knowledge specific to whichever realm owns the reference data. `tools/SeedTool` lives inside Mimisbrunnr, alongside the data it produces (`seeds/raw/`, `seeds/*.tsv`). A future realm with its own reference-data seed case builds its own tool the same way, on the same `Norse.Primitives.Ingestion` foundation — this is not a shared, pluggable, multi-realm binary.

---

## 2. Svartalfheim — `Norse.Primitives.Ingestion`

New project, sibling to `src/Primitives`:

```
Svartalfheim/
  src/
    Primitives/                  (existing, unchanged — zero dependencies)
    Primitives.Ingestion/        (new)
      ITabularReader.cs
      SepTabularReader.cs
      ExcelTabularReader.cs
      Primitives.Ingestion.csproj   (PackageReference: Sep, Sylvan.Data.Excel)
  tests/
    Primitives.Ingestion.Tests/
    smoke/
      Primitives.Aot.Smoke/            (existing, unchanged)
      Primitives.Ingestion.Aot.Smoke/  (new)
```

**AOT verification, not a Glitnir POC.** Sep declares itself trimmable and AOT/NativeAOT-compatible (`IsTrimmable=true`) — confirmed. Sylvan.Data.Excel makes no such explicit claim; it's a purely managed implementation with no external dependencies (no Open XML SDK), which is a good sign for trimming, but published reports note it can produce AOT warnings. This is exactly the kind of question Svartalfheim already has a cheap, proven mechanism for — `tests/smoke/Primitives.Aot.Smoke` — rather than a heavyweight Glitnir POC (reserved for genuinely open multi-day architecture questions like the pg19-document-store or VoyageEmbeddings spikes). `Primitives.Ingestion.Aot.Smoke` extends that same discipline: `dotnet publish -c Release`, run the native binary, zero AOT warnings and exit 0 required, exercising both `SepTabularReader` and `ExcelTabularReader` for real. If Sylvan.Data.Excel fails this bar, that's a finding for this spec's implementation plan to react to, not a blocking unknown to resolve before writing code.

**Unit tests** per reader against small fixture files: valid CSV/TSV, valid single-sheet `.xlsx`, and malformed variants of each that must throw (proving §1.3's structural-failure contract).

---

## 3. Mimisbrunnr — `tools/SeedTool`

```
Mimisbrunnr/
  tools/
    SeedTool/
      Mappers/
        UnsdM49Mapper.cs
      Program.cs                 (Spectre.Console.Cli entry point)
      SeedTool.csproj             (References: Norse.Primitives, Norse.Primitives.Ingestion)
  tests/
    SeedTool.Tests/
      UnsdM49MapperTests.cs
  seeds/
    raw/
      UNSD — Methodology.csv     (existing, permanent provenance)
    region.tsv                   (generated output)
    country-or-area.tsv          (generated output)
```

**Data flow (`UnsdM49Mapper`):**

```
raw CSV → SepTabularReader → foreach row → cell spans
        → Parser.ParseRequired<T>(span, CultureInfo.InvariantCulture) → Result<T>
        → Failure ⇒ throw immediately, naming row + column + reason; nothing is written
        → Success ⇒ accumulate into the Region tree / CountryOrArea list
        → write region.tsv, country-or-area.tsv (Sep writer)
```

Output columns match exactly what the already-approved M49 spec's §4 locked in:

- `region.tsv` — `M49Code`, `Name`, `Level`, `ParentM49Code` (blank for the 5 top-level Regions).
- `country-or-area.tsv` — `M49Code`, `IsoAlpha2Code`, `IsoAlpha3Code`, `Name`, `ParentM49Code` (blank only for Antarctica), `IsLeastDevelopedCountry`, `IsLandLockedDevelopingCountry`, `IsSmallIslandDevelopingState`.

**Tests:**
- `UnsdM49MapperTests` against a small in-memory fixture mirroring the real CSV's shape, including the two edge cases the M49 spec already calls out: Antarctica (no ancestor at all — straight to leaf) and a three-level-deep case (Region → Subregion → Intermediate Region, e.g. Nigeria).
- One integration-style test running the mapper against the real 248-row `seeds/raw/UNSD — Methodology.csv` and diffing output against checked-in expected TSVs — the actual regression proof that regenerating the TSVs from source is reproducible.

**`tools/` is a new top-level folder in Mimisbrunnr**, alongside `src/` and `tests/` — it needs its own `Directory.Build.props` (not packable; still net11.0, same analyzer tiers as the rest of the repo, for consistency).

---

## 4. Explicitly Out of Scope

- **A real `ExcelTabularReader` consumer.** No concrete reference-data source is Excel-only today (UNSD M49, ISO codes, IANA tzdata are all CSV/text). The reader ships and is AOT-smoke-tested for real, but nothing in Mimisbrunnr's `SeedTool` exercises it yet — it's proven from both sides (§1.2, §2) without a driving feature forcing it.
- **Multi-sheet Excel navigation.** `ITabularReader` reads exactly one sheet, forward-only. A multi-sheet contract is a separate, later design if a real source needs it.
- **A shared, pluggable, cross-realm seed-tooling binary.** Rejected in favor of one tool per realm (§1.6) — each realm's mapping logic is its own domain knowledge.
- **Collect-all failure aggregation for bad rows.** A mapper stops at the first `Failure`, per §1.4. Reporting every bad row in one pass (rather than fixing one at a time) is deferred until a source is messy enough to need it — none seen yet.

---

## 5. Success Criteria

- `Primitives.Ingestion.Aot.Smoke` publishes under Native AOT with zero warnings and exits 0, exercising both `SepTabularReader` and `ExcelTabularReader`.
- `SepTabularReader` and `ExcelTabularReader` both satisfy `ITabularReader` against real fixture files; malformed fixtures throw for both.
- Running `SeedTool` against `seeds/raw/UNSD — Methodology.csv` reproduces `region.tsv` and `country-or-area.tsv` byte-identically on repeated runs (no nondeterministic ordering).
- A deliberately corrupted row in a copy of the source CSV (bad M49 code, missing ISO code) causes `SeedTool` to exit non-zero before writing either output file, naming the offending row/column.

---

## Self-Review

**Placeholder scan:** No TBDs. §4 enumerates every deferred item with its reason, rather than leaving gaps.

**Internal consistency:** §1.1 (Ingestion has no dependency on Primitives) is reflected in §2's project layout (no `ProjectReference` to `Primitives` in `Primitives.Ingestion.csproj`) and in §3's data flow (the mapper, not the reader, calls `Parser.ParseRequired<T>`). §1.3's two-failure-class split is exactly what §3's data flow diagram shows. §1.5's "dev-only, never AOT-published" claim is checked against Yggdrasil's actual `Hosting.Migrations.Service.csproj` (no `PublishAot`), not assumed.

**Scope:** Two projects, one concrete consumer (UNSD M49, whose entity/TSV shape is already locked by a separate approved spec). Deliberately excludes a real Excel consumer, multi-sheet navigation, and a shared cross-realm tool (§4).

**Ambiguity:** §1.2 states explicitly which reader is zero-alloc and which allocates per cell, rather than leaving "canonical span-based interface" to imply both backends behave identically underneath. §1.3 states explicitly that structural and scalar failures use different mechanisms, closing off the reading where `Result<T>` might be expected to cover file-level corruption too.
