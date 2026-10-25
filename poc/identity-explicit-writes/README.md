# Identity explicit-writes mechanics spike

A throwaway proof for the Himinbjörg explicit-write store, temporal split, and hard-delete erasure ([spec](../../docs/Himinbjorg/specs/2026-10-24-identity-explicit-writes-temporal-split-design.md) §9, [plan](../../docs/Himinbjorg/plans/2026-10-24-identity-explicit-writes-temporal-split.md) Phase 0). It answers EF Core and engine mechanics questions only — gates G1–G6 — against both providers. Behavior against the real protector and manager is proven in Himinbjörg, not here.

It is a standalone EF Core 11 RC2 console app (`src/Spike`) with **no Norse packages**: Npgsql EF and EF SqlServer only, SQL Server configured with `UseCompatibilityLevel(170)`.

## What it stands up

- **Model** (`src/Spike/Model.cs`): `SpikeUser` split across `users` (temporal identity record) and `user_credentials` (non-temporal credential and lookup state) per spec §3; `SpikeClaim` with an unbounded `ClaimValue`; `SpikeToken`; `SpikePasskey` with `Data` as a JSON-mapped complex property (`jsonb` / native SQL Server 2025 `json`). One context per provider (`src/Spike/SpikeContexts.cs`): snake_case on PostgreSQL, PascalCase on SQL Server.
- **Hand-written DDL** (`src/Spike/Ddl/`), applied to a fresh `identity_spike` database on each run:
  - PostgreSQL: `users` with a database-owned `system_period` outside the EF model, `users_history`, and a minimal versioning trigger that skips no-op updates — a stand-in for the chassis apparatus, not a copy of it;
  - SQL Server: the spec §6.1 shape — `Users` with `GENERATED ALWAYS AS ROW START/END HIDDEN` periods absent from the EF model, `SYSTEM_VERSIONING = ON` into `UsersHistory`; the `UserCredentials` fragment gets nothing.
  - Both: the §3.1 `NOT NULL` lookup hashes, `CHECK (normalized_email = normalized_user_name)`, and the unique index on `normalized_user_name`.

G5 and G6 bring their own scratch databases (`identity_spike_g5`, `identity_spike_g6`, `identity_spike_g6_ef`), created and dropped by the gate: G5 issues the SQL Server lifecycle DDL inline (`src/Spike/GateG5.cs`); G6 applies `src/Spike/Ddl/g6.*.sql` (temporal claims with a stored claim value hash) and, on SQL Server, EF's own create script for an `IsTemporal()` claims model.

The runner checks the catalog after applying each script and does a full-entity read of every set, proving the EF model binds to the hand-written schema. It also writes EF's own `GenerateCreateScript()` output to `artifacts/` for comparison.

## Running

Docker must be running. From this directory:

```powershell
pwsh ./run.ps1                  # every gate
pwsh ./run.ps1 -OnlyShape G1    # one gate (or several: -OnlyShape G1,G4)
pwsh ./run.ps1 -KeepContainers  # leave the containers up afterward
```

The runner builds the spike, starts `postgres:19rc1` on port `54339` and `mcr.microsoft.com/mssql/server:2025-latest` on port `14333` (`linux/amd64`; emulated on Apple silicon), and **aborts before any gate** unless `server_version_num >= 190000` and `SERVERPROPERTY('ProductMajorVersion') >= 17`. It then applies both schemas and runs the selected gates. Every assertion reports `PASS` or `FAIL` (`PENDING` is tallied for an unimplemented gate; none remain), and untallied `INFO` lines record evidence such as exception types and race controls; raw output lands in `artifacts/` (gitignored), with a tally in `artifacts/summary.txt`. The containers and their volumes are removed on exit.

See [FINDINGS.md](FINDINGS.md) for the gate verdicts.
