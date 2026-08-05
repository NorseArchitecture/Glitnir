# EF temporal-emission seam spike

This standalone EF Core 11-preview/Npgsql 11-preview probe determines how a PostgreSQL migrations SQL generator can identify a `Norse:Temporal` table. It intentionally has no Urðarbrunnr dependency.

Run it from this directory:

```powershell
pwsh ./run.ps1
```

The runner starts PostgreSQL `19beta2` on port `54329`, installs a local `dotnet-ef` preview tool if absent, scaffolds and applies each shape twice, and writes checked-in evidence under `artifacts/`:

- `target-model-only` leaves the relational annotation provider unmodified.
- `annotation-provider` registers `SpikeAnnotationProvider`, which copies the model marker to relational table annotations.

Each mode contains the scaffolded migration, generated SQL, operation report, and the preceding baseline migration source. The custom generator logs each intercepted operation, its annotations, `AlterTableOperation.OldTable` annotations, and whether the target model resolves the table as temporal. The create override additionally emits a small `widgets_temporal_spike_apparatus` table; its successful application proves that the generator interception point emits executable PostgreSQL.

See [FINDINGS.md](FINDINGS.md) for the design-gate verdict.
