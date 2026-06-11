# Findings — PG19 `FOR PORTION OF` Reconnaissance

**PG version tested:** PostgreSQL 19beta1 — official image `postgres:19beta1-trixie` (`19~beta1-1.pgdg13+1`, gcc 14). Original run 2026-06-04 (beta1 release day) used the PGDG-apt bookworm shim (`19~beta1-1.pgdg12+1`, gcc 12); re-run 2026-06-07 on the official image with **full parity** — every error/notice/trigger/rowcount line identical across all five scripts, only timings and toolchain line moved. `results/` reflects the official-image run.
**Date:** 2026-06-04 (beta1 release day); re-verified 2026-06-07 (official image)
**Re-verify at:** RC1 — verdicts #1 and #4 sit on documented open items and may change.

## Matrix verdicts

| # | Question | Verdict | Detail |
|---|---|---|---|
| 1 | RLS immutability mitigation viable? | **WORKS in beta1 — but built on sand** | Closed rows untouchable (`UPDATE 0` under policy); `FOR PORTION OF` on current rows succeeds; **leftover flank inserts bypass RLS `WITH CHECK`** (flank with closed period inserted despite a current-only INSERT policy); forged-history INSERT correctly rejected. The bypass is almost certainly the subject of the "FOR PORTION OF incompatible with RLS" open item — GA could legitimize it or "fix" it, and a fix breaks the mitigation. Do not build on this before GA. |
| 2 | Plain FK to single-table temporal parent impossible? | **CONFIRMED — Model B stands for FK targets** | `ERROR: there is no unique constraint matching given keys`. Temporal `PERIOD` FK declarable but viral (child forced to carry a period). **Bonus: aggregated coverage CONFIRMED** — a child span straddling two contiguous parent versions inserts cleanly, refuting the Neon doc's single-row-containment claim; SQL:2011 semantics hold, so parent version splits do NOT strand children. `ON DELETE CASCADE` on a PERIOD FK: `ERROR: unsupported ON DELETE action` — RESTRICT/NO ACTION only in beta1. |
| 3 | Inline-version bloat penalty on current-row reads | **Real but modest with partial-index mitigation** | 510k-row single table (88 MB) vs 10k-row Model B main (1.2 MB) + 86 MB history. Point lookup: 0.058 ms vs 0.019 ms. Current-row scan: 2.45 ms vs 1.31 ms (~2×). As-of point query: single-table temporal-PK GIST scan 0.30 ms vs Model B two-scan `UNION ALL` 0.07 ms — **the timeline-view shape is cheap; the UNION was never the cost.** Write-side note: bulk insert into a temporal-PK (GIST) table ran ~33k rows/s — GIST maintenance is the write tax. Synthetic, warm-cache, directional only. |
| 4 | Trigger/RETURNING/rowcount semantics match docs? | **CONFIRMED exactly** | Q3 correction → 3-way split; triggers fired 2× INSERT (flanks, old values) + 1× UPDATE (portion, old full-span → new portion). `ROW_COUNT` = 1 and `RETURNING` exclude leftovers. DELETE carves a hole (gap legal, flanks preserved, 2× INSERT + 1× DELETE). `FROM x TO NULL` and `FROM NULL TO x` both accepted in beta1 (syntax under an open item). **4e — the trigger-killer demo works:** `FOR PORTION OF system_period FROM now() TO NULL` is a complete single-statement system-versioned update (closed flank with old value, current row with new). Also: `FOR PORTION OF` does NOT require a temporal PK (worked on an EXCLUDE-only table). |
| 5 | Bitemporal single-table expressible without hoops? | **PUZZLE CONFIRMED — two-table dual-flavor split stands** | Two `WITHOUT OVERLAPS` in one PK: syntax error (one allowed). EXCLUDE-gist three-way constraint works as the second dimension. **The smoking gun:** splitting the business period copies `system_period` VERBATIM onto the flanks — engine-made rows claim a system lower bound from before they existed. Defensible as knowledge-level semantics, a lie as row-level audit — either way it demands a paragraph of explanation per query, which is the definition of a hoop. |

## Implication for the Norns spec (§6.4 / §14 #2)

1. **The §6.4 storage-model split STANDS unchanged for V1.** The FK objection is confirmed structural (verdict 2); the RLS-based immutability answer exists but rests on behavior an open item may reverse (verdict 1). Model B remains universal for system time.
2. **The "single-table temporal for non-referenced leaf entities" third row is REAL but PARKED.** Verdicts 1, 3, and 4e show it viable in beta1 — mechanically beautiful (one statement, no triggers, no views), modest read penalty, immutability achievable. Re-entry trigger: PG19 GA ships with leftover-RLS behavior stabilized in a usable form AND a leaf-entity workload that measurably suffers under trigger overhead.
3. **Temporal FKs upgrade from "practically dead" to "selectively usable":** aggregated coverage means parent version splits don't strand children. Still viral, still no CASCADE — fine for genuinely effective-dated domain graphs (Product-context rate structures), never for system-time spines.
4. **Model A (business-effective) adoption of `FOR PORTION OF` is fully de-risked** (verdict 4): semantics match the spec's §14 #2 text exactly, including Urd composition (flank INSERTs will take fresh system periods from the insert path; the portion UPDATE versions normally).
5. **`btree_gist` prerequisite empirically confirmed** (first run failed without it) — already folded into spec §7.3 step 0.

## Reproduction

`./run.ps1` (official `postgres:19beta1-trixie`; the release-day PGDG-apt shim was retired 2026-06-07 once docker-library published). Raw outputs in `results/*.out`.
