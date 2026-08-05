# Temporal (System-Versioned) Tables in the Persistence Chassis — Design

**Status:** DRAFT for review (brainstorm output, 2026-08-04; revised same day after first review pass — rulings 8–12). No plan, no implementation, until this passes the gate — and no implementation of §3 until the §3.0 spike renders its verdict.
**Realms touched:** Urðarbrunnr (chassis: marker, convention, PostgreSQL emission, SQL Server realization), Himinbjörg (identity enablement, gated), Bifröst (proving ground, primary+replica assertion), Glitnir (this verdict, POC re-run).
**Issues:** NorseArchitecture/Urdarbrunnr#52 (this design) · NorseArchitecture/Himinbjorg#47 (identity enablement, downstream) · NorseArchitecture/Bifrost#14 (custody proving ground, sibling).
**Inherits without re-litigation:** the Norns storage-model split (`docs/Platform/specs/2026-06-04-norns-design.md` §6.4 — Model B current+history universal for system time; the parked single-table leaf variant stays parked per its stated re-entry trigger); the §7.3 history-table apparatus shape; the PG19 `FOR PORTION OF` reconnaissance verdicts (`poc/pg19-temporal/FINDINGS.md`, beta1); crypto-shredding over tombstoning and the envelope law (`docs/Platform/specs/2026-08-03-pii-primitives-identity-erasure-seam-design.md`); hard-delete erasure superseding darken-in-place (Himinbjorg#47 direction change).

---

## 0. Scope

This design makes Model B system-time history a **capability of `Norse.Persistence.EntityFramework.*`** instead of hand-rolled SQL: mark an entity, scaffold a migration, and the full history apparatus exists and evolves with the schema. History being *written* is the exit; reading it richly (as-of/timeline LINQ, §7.4 of the Norns design) is its own future story. The crypto-shredding/erasure implementation is not here either — this puts the temporal log on the proving-ground floor so the erasure proof, when it lands, proves the hard half (history rows going dark, not just current rows).

## 1. Rulings absorbed from the brainstorm and spec review (2026-08-04)

1. **POC re-run on beta2 — yes, folded in.** Bifröst's containers already run `19beta2`; the POC bumps its image and re-runs as a task in this train, updating `poc/pg19-temporal/FINDINGS.md`. The POC's own RC1 gate remains the binding re-verify for the parked single-table variant. Analytical note for the record: neither open verdict gates Model B — verdict 1 (RLS immutability) belongs to the parked variant, verdict 4 (`FOR PORTION OF`) to Model A business-effective DML.
2. **PG version floor: PG19.** The mechanics are PG18 features (`WITHOUT OVERLAPS`, `btree_gist`), but the platform's trajectory pins one number and the POC exists to measure the front edge. The floor is asserted fail-fast in-migration (§3.1 step 0), never documented-and-hoped.
3. **Opt-in surface: marker interface**, per the Accelerator prior art (`IReadOnlyEntity`/`IInsertOnlyEntity`/`ILegacyEntity` family). Fluent and attribute shapes rejected: both leave something to forget or to validate separately.
4. **Replica: history must reach the streaming replica**, and the Bifröst proof asserts it there — completeness is a thing. No read surface ships anywhere this iteration.
5. **SQL Server: park split entities.** EF-native `IsTemporal()` for non-split entities; temporal on split entities parks behind an explicit declaration (§4) until upstream ships per-fragment control. Re-entry trigger: [dotnet/efcore#26457](https://github.com/dotnet/efcore/issues/26457) — the asymmetric-temporality case is on the record upstream ([comment](https://github.com/dotnet/efcore/issues/26457#issuecomment-5186526882)); the migration-generation NRE is [dotnet/efcore#30366](https://github.com/dotnet/efcore/issues/30366).
6. **Access story: deferred per Norns §7.3.** Trigger functions are `SECURITY DEFINER` owned by the migration role (harmless under Bifröst's single local role, load-bearing when roles split); no runtime SELECT grant on history or timeline ships here. Who reads post-shred history — and whether the custody seam gates it — stays parked on Himinbjorg#47 open question 3, decided in the erasure train.
7. **Emission approach: annotation → provider migrations SQL generator** (§3), over `MigrationBuilder` extension methods (manual, forgettable, and every future column change on a temporal entity would need a remembered mirror call) and over a post-migration reconciler (exits the migration record; column renames unsolvable outside the migration timeline). This supersedes the Norns §7.6 sketch's hand-called `AddSystemVersioning<T>()` shape — same apparatus, automatic emission.

Review fold-ins (first review pass, same day — priority order: clock, seam spike, extension provisioning, definer hardening):

8. **The temporal clock is `clock_timestamp()`, with a monotonicity clamp and no-op suppression** (§3.2). `now()` is transaction-start time and cannot close versions safely: same-transaction updates would mint `empty` ranges the `WITHOUT OVERLAPS` PK silently admits in duplicate, and a lock-waiting writer can attempt a backwards range that aborts its transaction.
9. **The EF emission seam is a design gate, not a plumbing detail** (§3.0). A minimal scaffolding spike names the exact supported seam across all five operation shapes before the implementation plan is written; if none supports reliable temporal-table identification, the emission approach returns to the court.
10. **`btree_gist` is an operational privilege boundary** (§3.1 step 1). Migrations attempt idempotent creation and fail early with a named diagnostic declaring it a provisioning prerequisite where the migration role may not create extensions.
11. **`SECURITY DEFINER` ships hardened or not at all** (§3.1 step 4): pinned empty `search_path`, schema-qualified references throughout, `REVOKE EXECUTE … FROM PUBLIC`, migration-role ownership of function and history table; split-role execution tested when the grants story lands.
12. **History-column projection is a defined rule, and unsupported evolutions are rejected loudly** (§3.3): name + store type only, nullable except key components, no defaults/identity/generation/constraints/indexes; PK changes and schema moves on temporal tables are named migration-time diagnostics in v1, never silently wrong history constraints.

## 2. Opt-in contract — provider-neutral, in the EF foundation

### 2.1 `ITemporalEntity` is an empty marker

```csharp
namespace Norse.Persistence.EntityFramework;

/// <summary>
/// System-time temporality (Norns §6.4 Model B): the entity's main table gets a
/// database-owned system_period, a history table, versioning triggers, and a
/// timeline view. Split-table fragments are deliberately NOT temporal (§2.3).
/// </summary>
public interface ITemporalEntity;
```

- **No members.** `system_period` is database-owned: emitted, defaulted, and maintained entirely by the apparatus. It never enters the EF model, so entities, wire `[DataContract]` shapes, and WASM payloads stay lean, and the provider divergence on period representation (`tstzrange` vs `datetime2` pairs) never touches CLR.
- Lives beside `INorseEntity<TSelf>` in `Norse.Persistence.EntityFramework` — the issue's "provider-neutral surface in the EF foundation." If the future read surface wants the marker in Asgard (to constrain `ITemporalRepository<T>`), graduation is that story's decision; the marker may also grow a `SystemPeriod` property then. Not now.

### 2.2 `TemporalEntityConvention`

A finalizing convention registered in `NorseModelConventions.Apply`, beside `RequireExplicitLengthConvention` and `RequireEntityConfigurationConvention`. For every entity type implementing `ITemporalEntity` it:

1. **Validates**: the entity has a primary key; it is not JSON-mapped, owned, or complex-type-mapped. Violations throw at model finalize with a named diagnostic — fail at startup, not at scaffold.
2. **Stamps** a `Norse:Temporal` annotation on the entity's **main table mapping only** (`StoreObjectIdentifier` of the root table). Split-table fragments are never stamped.

### 2.3 Split-table asymmetry is structural on PostgreSQL

Because the apparatus attaches to the main table only: a `SplitToTable` fragment gets no `system_period`, no triggers, and its columns never enter the history table. A fragment-only UPDATE (the lockout counter case) is SQL-wise an UPDATE on the fragment table alone — the main-table trigger never fires, so **operational churn cannot mint a history row, by construction**. No fluent escape hatch is needed for the noise case; the Himinbjorg#47 shape (temporal `users`, non-temporal `user_lockout`) falls out of marking the entity and splitting the fragment.

## 3. PostgreSQL emission — `.PostgreSQL` package

`NorseNpgsqlMigrationsSqlGenerator` derives from Npgsql's generator, registered via `ReplaceService<IMigrationsSqlGenerator, …>` inside `NorsePostgresEfProvider.Configure` — the existing single choreography point. An annotation-provider companion surfaces `Norse:Temporal` onto migration operations so the generator sees it on creates, drops, and column operations.

### 3.0 Design gate — the emission-seam spike

The EF interception point is this design's load-bearing unknown: if migration operations cannot reliably identify their temporal table mapping, automatic evolution — the entire "no remembered mirror call" promise — collapses back to Approach A. **Before the implementation plan is written**, a minimal spike (Glitnir `poc/ef-temporal-emission`, sibling to `poc/pg19-temporal`) scaffolds create/add-column/rename-column/drop-column/alter-column migrations from a marked entity, inspects the generated operations and their annotations, applies them to real PostgreSQL through a derived generator, and names the exact supported seam (`IRelationalAnnotationProvider`, target-model consultation inside `Generate`, or both — including how drop-side operations, where the entity is absent from the target model, identify themselves). The spike's verdict amends this spec. If no seam supports reliable identification across all five shapes, the emission approach returns to the court rather than shipping degraded.

### 3.1 On `CreateTable` of a temporal table

Emitted in order, per Norns §7.3, all generated names riding the existing snake-case rewriter:

0. **PG19 floor assert** — a `DO` block failing the migration loudly if `current_setting('server_version_num')` < 190000. Once per migration that contains temporal DDL.
1. `btree_gist` — hard prerequisite for `WITHOUT OVERLAPS` (POC-confirmed; first beta1 run failed without it), and **an operational privilege boundary**: managed deployments often deny `CREATE EXTENSION` to the migration role. The emitted guard is idempotent and fails early: extension present → proceed; absent → attempt creation; creation denied → raise a named diagnostic declaring `btree_gist` a platform provisioning prerequisite in this environment (install out-of-band, rerun). Loud at step 1, never a mid-apparatus failure. (Neon permits it; the diagnostic is for everywhere else.)
2. `system_period tstzrange NOT NULL DEFAULT tstzrange(clock_timestamp(), 'infinity')` on the main table — the temporal clock per §3.2, uniformly; `now()` appears nowhere in the apparatus.
3. `{schema}.{table}_history` — the main table's columns plus `system_period`; `PRIMARY KEY ({pk columns}, system_period WITHOUT OVERLAPS)`. Composite keys (pure links) carry their composite columns plus the period — version-overlap corruption structurally impossible where versions accumulate.
4. Trigger function plus UPDATE/DELETE triggers on the main table: UPDATE closes the old version into history and resets the current row's period; DELETE inserts the closed old version — clock and closure semantics per §3.2. Explicit column lists, never `SELECT *` or positional inserts. The function is `SECURITY DEFINER`, **hardened per the definer checklist**: `SET search_path = pg_catalog` (pinned, empty of user schemas), every object reference schema-qualified, `REVOKE EXECUTE ON FUNCTION … FROM PUBLIC`, function and history table owned by the migration role. Execution under genuinely distinct migration/runtime roles is exercised when the grants story lands (§8).
5. `{schema}.{table}_timeline` — `CREATE VIEW … AS SELECT … FROM main UNION ALL SELECT … FROM history`. No runtime grants on history or timeline (§1 ruling 6).

### 3.2 The temporal clock and version closure

`now()` is transaction-start time in PostgreSQL and cannot close versions safely. Two updates in one transaction would close a version with `tstzrange(t, t)` — which normalizes to `empty`, and empty ranges never overlap anything, so the `WITHOUT OVERLAPS` PK would **silently admit duplicate versions**. Worse, a transaction that began before a competing writer committed can wait out the row lock and then attempt closure with an earlier timestamp — a backwards range that raises and aborts the writer with a nonsense error. The rulings:

- **One clock: `clock_timestamp()`** everywhere the apparatus reads time — the insert default (§3.1 step 2) and the trigger alike, never mixed with `now()`. Row-level `BEFORE` triggers fire after the row lock is acquired, so the trigger's reading is post-lock wall clock.
- **Monotonicity clamp:** closure computes `ts := greatest(clock_timestamp(), lower(OLD.system_period) + interval '1 microsecond')` — strictly after the version's open bound regardless of wall-clock behavior (NTP regression, same-microsecond updates). Every history period has strictly positive length; the closed version's upper bound equals the successor's lower bound, so the timeline stays gapless and overlap-free by arithmetic, with the temporal PK as the structural backstop.
- **No-op suppression (version-churn policy):** an UPDATE that changes nothing (`OLD` vs `NEW` compared over the explicit application column list — `system_period` is DB-owned and outside the EF model, so app statements never touch it) writes no history row and leaves `system_period` untouched. History records knowledge changes, not statement traffic. Documented trade: "who touched this row idempotently, and when" is audit-log territory, deliberately not system-time versioning's job.
- **Same-transaction churn is kept, not collapsed:** repeated genuine updates inside one transaction each mint a real, positive-length version — intermediate states are visible history. (SQL Server's native temporal stamps transaction-start time and can produce zero-duration history rows; PG Model B deliberately does neither.)

### 3.3 Evolution — history mirrors main, forever

**History-column projection rule:** a history column copies **name and store type only**. Nullability: the temporal PK components (`{pk columns}`, `system_period`) are `NOT NULL`; every other history column is nullable regardless of what the main column declares. Never projected: defaults, identity, `GENERATED` expressions (a generated column projects as a plain column holding the materialized value from `OLD`), foreign keys, CHECK and unique constraints, and secondary indexes — history integrity is the temporal PK, full stop.

Column operations against a temporal table are the generator's job for the life of the schema:

- **`AddColumn`** → mirrored onto history per the projection rule — history rows predating the column honestly say NULL. Trigger function regenerated with the new column list; view re-emitted via `CREATE OR REPLACE VIEW`.
- **`DropColumn`** → mirrored: the column drops from history too. **History is a version log, not an archive of dead columns** — the ruling is mirror-always, and a dropped column's historical values go with it. (A realm needing to preserve a retiring column's history renames or snapshots before dropping — a deliberate act, not a chassis default.)
- **`AlterColumn`** (type/nullability changes) → mirrored onto history (nullability in history stays nullable); trigger function and view regenerated.
- **`RenameColumn` / `RenameTable`** → renamed in history (and the history/timeline/trigger object names re-derived); function and view regenerated. Rename, not drop+add — history data mapping is preserved.
- **Dropping the entity** (or removing the marker) → triggers, function, view, and history table dropped. Removing the marker from a live entity is a destructive act the migration makes visible as explicit `Drop…` operations in the scaffolded diff.
- **Rejected in v1, with named migration-time diagnostics:** primary-key changes and schema moves on a temporal table. Both would leave the history PK or the apparatus's derived names invalid or stale; failing loudly beats silently-wrong history constraints. The documented workaround is deliberate: explicitly drop temporality (visible destruction, previous bullet), perform the change, re-mark — or a hand-authored migration for the rare case that must preserve history across the change.

### 3.4 Free rider: the DBA schema dump

`Database.GenerateCreateScript()` runs through the same SQL generator, so `DdlEmittingMigrationsScaffolder`'s `schema/{db}.sql` dump picks up the full apparatus with zero additional work (plan-time verify item, expected free).

## 4. SQL Server posture — `.SqlServer` package

- **Non-split temporal entities:** the provider realization translates `Norse:Temporal` to EF-native `IsTemporal()` — engine-enforced system-versioning, nearly free. The existing (currently inert) `RenameTemporalHistoryTable` hook already covers history-table naming if a rewriter ever appears on SQL Server.
- **Split + temporal entities:** **model-finalize error by default**, replacing the upstream scaffold-time NRE with a loud, named diagnostic. The error is dismissible only by an explicit fluent declaration in the entity's static `Configure` — working name `TemporalParkedOnSqlServer()` (final name at review) — which acknowledges the divergence and skips temporality **on SQL Server only**. Rationale: post-#47, Himinbjörg's user entity is both split and temporal and one model builds for both providers — a hard throw would brick the SQL Server migrations assembly, a silent skip would violate fail-loud law. Declared divergence is the pit-of-success middle: greppable, self-documenting, deleted the day upstream ships per-fragment control (re-entry trigger, §1 ruling 5).
- **Model B triggers on SQL Server: rejected.** SQL Server has no exclusion constraints and no `WITHOUT OVERLAPS`, so hand-rolled Model B there cannot make version overlap structurally impossible — semantically weaker than both PG Model B and SQL Server's own native temporal, while also reimplementing what the engine gives free.
- **Honest coverage note:** no SQL Server container fixture exists anywhere on the platform; SQL Server coverage this iteration is model/unit-level (§6). A real fixture is its own future chore, deliberately not smuggled into this train.

## 5. Adoption flow

Realm workflow is two acts: add `: ITemporalEntity` to the entity, run `dotnet ef migrations add`. The `IMigrationContributor` flow, `MigrationRunnerService`, and the generated `AddNorseMigrations()` wiring behave unchanged — the apparatus rides inside ordinary scaffolded migrations. No hand-written SQL, no `MigrationBuilder` extension calls, in any adopting realm.

**Himinbjörg enablement** (the final exit criterion, gated on .NET 11 preview 7 ~2026-08-11 and the #47 operational-noise split landing): the post-split, PII-bearing identity tables take the marker; the split-off `user_lockout` fragment deliberately does not (and needs no marker mechanics — §2.3); the SQL Server side declares `TemporalParkedOnSqlServer()` on the split user entity. Which identity side tables (claims, logins, tokens) go temporal is Himinbjorg#47's open question 4 — that issue's call, not this spec's.

## 6. Testing

Chassis-out, per the standing law (no mocked-DB tests for database-semantics behavior):

1. **POC beta2 re-run** (`poc/pg19-temporal`): bump `run.ps1` to the official 19beta2 image, re-run all five scripts, update `FINDINGS.md` (header + any verdict movement). RC1 remains the binding gate for the parked variant.
2. **Urðarbrunnr unit:** `TemporalEntityConvention` stamping and validation diagnostics (SQLite/model-level, matching the existing convention-test pattern); SQL Server realization (annotation → `IsTemporal`, split-guard error, `TemporalParkedOnSqlServer` skip); generator DDL snapshot tests over the emitted SQL for create and each evolution operation.
3. **Urðarbrunnr integration (new project):** a Testcontainers PostgreSQL fixture on the same `postgres:19beta2` image Bifröst runs — pattern copied from `Himinbjorg/tests/Identity.Migrations.Tests/PostgresContainerFixture.cs`. A sample temporal entity **with a split fragment** migrates, then against real Postgres: UPDATE writes a history row with the closed period; DELETE writes a closed row; a second UPDATE produces disjoint periods under the temporal PK; **a fragment-only update writes no history row**; `schema` dump contains the apparatus. Clock-semantics coverage per §3.2: **repeated updates inside one transaction** yield strictly-positive-length, contiguous versions; **a lock-waiting concurrent update** (transaction begun before a competing writer commits) closes monotonically instead of raising a backwards range; **a no-op UPDATE** writes no history row and leaves `system_period` untouched. Evolution coverage per §3.3: each supported operation against a live history table, plus the **named diagnostics for rejected operations** (PK change, schema move). The chassis proves itself without waiting on preview 7.
4. **Himinbjörg** (gated): identity flows through `UserManager`/`SignInManager` produce history on `users`; lockout churn produces none; erasure `DELETE` writes the final closed version (the row #47's hard-delete story then darkens by shredding).
5. **Bifröst proof:** clean `dotnet run --project src/Orchestration.AppHost` stands up `norse_identity` with the apparatus; the verification test writes through a temporal entity and asserts the history row is visible on **both the primary and the streaming replica** — under an explicit consistency contract, not sleep-and-hope: capture the primary's flushed WAL LSN after the write commits (`pg_current_wal_flush_lsn()`), poll the replica until `pg_last_wal_replay_lsn()` reaches it within a bounded timeout, then assert; timeout failure reports replication lag as the diagnostic, distinct from a missing history row.

## 7. Exit-criteria mapping (Urdarbrunnr#52)

| Issue checkbox | Where satisfied |
|---|---|
| Chassis-level opt-in, provider-neutral surface, PG mechanics in `.PostgreSQL`, Model B per Norns | §2 (marker + convention), §3 (emission) |
| Full history apparatus through the standard `IMigrationContributor` flow, no hand-written SQL in adopting realms | §3, §5 |
| Himinbjörg first: post-split PII tables temporal, operational-noise tables not | §5 (gated on preview 7 + #47) |
| Clean Bifröst run stands up `norse_identity` with history running, verified by test | §6 item 5 |

## 8. Open items recorded for future stories

- **Timeline/as-of read surface** (Norns §7.4): including the question raised in this brainstorm — whether history presents reasonably through the jsonb `View` document shape, or whether time travel needs a row-level-entity/not-aggregate exception. Owns the marker's possible graduation to Asgard and any `SystemPeriod` CLR surface.
- **History read access and custody-seam gating:** Himinbjorg#47 open question 3, decided in the erasure train.
- **SQL Server container fixture:** its own chore; unlocks applying the SQL Server `InitialCreate` (and future temporal DDL) in CI.
- **Parked single-table leaf variant:** unchanged; re-verify at RC1 per the POC gate.
- **Role separation (Norns §10 grants matrix):** the hardened `SECURITY DEFINER` shape ships now (§3.1 step 4); the actual migration-role/runtime-role split, grant emission, and exercising the definer apparatus under genuinely distinct roles land with the grants story.
