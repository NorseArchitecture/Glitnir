# POC — PG19 `FOR PORTION OF` vs. the Norns Storage-Model Split

**Status:** Reconnaissance against PG19 **beta1** (scheduled 2026-06-04). Findings are dated against beta1 and MUST be re-verified at RC1 — two of the five matrix rows correspond to known [PG19 open items](https://wiki.postgresql.org/wiki/PostgreSQL_19_Open_Items) and may change before GA.

**Spec context:** `docs/superpowers/specs/2026-06-04-norns-design.md` §6.4 (storage-model split) and §14 open question #2 (PG19 adoption). The question this POC informs:

> Does `FOR PORTION OF` change the history-table (Model B) vs. single-table (Model A) dynamic for **system-time** versioning?

PG19 demolishes Model B's *mechanical* argument (triggers, timeline views) but does not obviously touch the *structural* ones (plain-FK preservation, history immutability, hot-table bloat). Each script tests one structural claim empirically.

## The matrix

| # | Script | Question | Kills/saves | Known open item? |
|---|---|---|---|---|
| 1 | `01-rls-immutability.sql` | Can RLS forbid editing closed portions while `FOR PORTION OF` still works on current rows? | The immutability objection to single-table | **YES — "FOR PORTION OF incompatible with RLS"** |
| 2 | `02-fk-mechanics.sql` | Plain FK to a temporal parent — confirm impossible; measure the viral cost of `PERIOD` FKs | The FK objection (likely confirms Model B) | no |
| 3 | `03-bloat-perf.sql` | Current-row query perf as versions accumulate inline vs. lean Model B main table | The bloat objection | no |
| 4 | `04-portion-semantics.sql` | Trigger firing, `RETURNING`, rowcounts, unbounded-bound syntax under `FOR PORTION OF` | Model A confidence (committed regardless) | **YES — "trigger behavior inconsistencies between leftovers"; NULL-vs-keyword for unbounded** |
| 5 | `05-bitemporal-single-table.sql` | Can business-effective + system time express in ONE table? | Whether dual-flavor entities could ever go single-table | no |

## Prior (to be confirmed or refuted)

Script 2 confirms the kill: Model B stands for FK-target entities. The POC's real prize is qualifying a **single-table temporal variant for non-referenced (leaf) entities** — the spec's storage-model table grows a third row only if scripts 1 and 3 earn it.

## Running

```powershell
./run.ps1            # starts container, runs all scripts, writes results/
./run.ps1 -Script 04 # run a single matrix row
./run.ps1 -Down      # tear down
```

Requires Docker Desktop. Runs the official `postgres:19beta1-trixie` image (the PGDG-apt shim that bridged release day was retired 2026-06-07 once docker-library published; same beta bits). Output lands in `results/{nn}.out` — record conclusions in `FINDINGS.md`.

This is exploration tooling, not platform code: raw SQL via psql, no .NET, no spec-first implications.
