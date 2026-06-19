# POC — PostgreSQL as the Operational Document Store (Culling Mongo)

**Status:** Reconnaissance against PG19 **beta1** (`postgres:19beta1-trixie`). Findings are dated against beta1 and MUST be re-verified at RC1. Sibling to `pg19-temporal`; this POC proves the system-time temporal model (proven there) coexists with a jsonb document store on the same primary.

**Spec context:** `docs/Platform/specs/2026-06-16-postgres-document-store-decision-inputs.md`. The question this POC informs:

> Can PostgreSQL serve the operational read store directly — jsonb documents the HTTP tier fetches from a **streaming read replica** — well enough to cull MongoDB as a runtime dependency entirely, with the failure-domain isolation of the Mongo design preserved?

The direction is ruled; the **formal verdict is gated on this POC**. The verdict turns on Q1/Q2 (does the `IDocumentRepository<T>` contract survive a jsonb impl), Q3 (replica lag → which write-tier), and Q6 (Transactional Session atomicity). Q4/Q5 inform but do not gate.

## Two deviations from `pg19-temporal`

This POC is not pure-psql like its sibling, for two structural reasons the decision record forces:

1. **Primary + streaming read replica**, not a single node — the failure-domain commitment is preserved by physical isolation (decision record §2), and the lag between them (Q3) is the variable that picks the write-tier (W1/W2/W3).
2. **A thin `harness/` .NET sliver** — the `.Server` wall is redefined as "no EF," so the load-bearing proof is that **Npgsql deserializes jsonb projections into POCOs with no EF in sight**. Only .NET can prove that; psql cannot. The harness carries Q1's consume-side and Q3's insert→visible timing.

## The matrix

| # | Where | Question | Gates |
|---|---|---|---|
| 1 | `01-projection-parity.sql` + harness | Filter (`@>`/jsonpath), sort, `skip`/`take`, and projection to a narrower shape (`jsonb_build_object`/`JSON_QUERY`) — server-side, then consumed by raw Npgsql (no EF). | Contract stability + wall redefinition |
| 2 | `02-array-subset-projection.sql` | Filtered-array-subset (`$elemMatch`/positional analog) via `jsonpath`/`JSON_TABLE`; GIN-indexable? | Whether the contract grows an escape hatch |
| 3 | `03-replication-lag.sql` + harness | Streaming replica lag; **shim INSERT → visible on replica**, idle and under write load. | W1 vs. W2/W3 |
| 4 | `04-temporal-jsonb-coexistence.sql` | Model B temporal tables + jsonb document table on one primary, both streaming. | The two storage models compose |
| 5 | `05-document-build.sql` | Build the doc Postgres-side from source-of-truth tables (`JSON_TABLE`/aggregation) vs. app-side. | "In memory vs. in DB" |
| 6 | deferred — see FINDINGS | NSB Transactional Session: shim INSERT + `Send` atomic; failed INSERT rolls back dispatch. | Atomicity / no-command-on-replay |

**Q6 is staged, not built in the first cut.** It needs NServiceBus + RabbitMQ + SQL persistence — its own harness. The feature is documented to work against Postgres; we confirm it in a focused follow-on spike rather than balloon this POC. Tracked in FINDINGS so it is not silently dropped.

## Running

```powershell
./run.ps1            # starts primary + replica, runs all SQL scripts on the primary, runs the harness, writes results/
./run.ps1 -Script 02 # run a single matrix SQL script
./run.ps1 -SkipHarness
./run.ps1 -Down      # tear down (removes volumes)
```

Requires Docker Desktop and the .NET 10 SDK (for the harness). Runs the official `postgres:19beta1-trixie` image — if the tag is gone, compose fails loudly; no silent fallback to PG18, these scripts test PG19 behavior. Output lands in `results/{nn}.out` and `results/harness.out` — record conclusions in `FINDINGS.md`.

**The replica bring-up is the one environment-sensitive piece.** It runs `pg_basebackup` against the primary on first start (see `docker-compose.yml`). If it fails on your machine, that is the expected first place to need a tweak — fail loudly there and fix it, do not paper over it. Everything downstream depends on a healthy standby.

## The AOT-clean translator spike (`harness-aot`)

`./run.ps1` runs `harness-aot` under JIT (proves the expression-walker translates predicate + projection lambdas to jsonb SQL). The **NativeAOT publish** is the separate confirmation that the path has no reflection/emit hazards:

```powershell
dotnet publish harness-aot/harness-aot.csproj -c Release -r win-x64 -p:PublishAot=true
```

The managed/ILC half is already proven clean (no IL2xxx/IL3xxx trim/AOT warnings, ILC reached "Generating native code"). The **native link** needs the MSVC C++ toolchain wired into the shell — that's the one prerequisite:

- **Install:** Visual Studio (or standalone *Build Tools for Visual Studio*) with the **"Desktop development with C++"** workload — which pulls the **MSVC v143 x64/x86 build tools** and a **Windows 11 SDK**. (The link step also calls `vswhere.exe`, normally at `${ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe`.)
- **Run from the right shell:** launch the publish from a **"Developer PowerShell for VS"** (or "x64 Native Tools Command Prompt for VS"), not a bare Git Bash / PowerShell — the Developer shell puts `vswhere` and the MSVC environment (`vcvars`) on PATH, which is what the `'vswhere.exe' is not recognized` failure was about. MSVC itself is present (the linker was found); it just wasn't on the shell's PATH.

A clean native publish drops `Norse.harness-aot.exe` under `harness-aot/bin/Release/net10.0/win-x64/publish/` — run it with `--primary "Host=localhost;Port=5455;Username=postgres;Database=norse_poc"` to confirm the native binary behaves identically to the JIT run.

This is exploration tooling, not platform code: raw SQL via psql plus single-file Npgsql/translator harnesses, no spec-first implications.
