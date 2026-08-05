# Temporal Chassis — Codex PR #53 Remandments

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development paired with superpowers:test-driven-development on every coding task, per standing law (Glitnir CLAUDE.md §2.8).

Remediation plan for the Codex review findings on Urðarbrunnr PR #53 (`feature/temporal-tables-persistence-chassis`), verified against the code 2026-08-05. Five confirmed defects enter as fix tasks; the sixth (SQL Server realization timing) enters as a verification task because the reviewer's mechanism claim is dubious but the coverage hole it points at is real. Parent design: `../specs/2026-08-04-temporal-tables-persistence-chassis-design.md`.

## Global Constraints

- **Branch:** all work lands in the working tree of `Urdarbrunnr` on the already-checked-out `feature/temporal-tables-persistence-chassis` branch (HEAD `c2366f3`). **Never commit, never branch, never push** — stage (`git add`) and stop; the human commits. This overrides any skill instruction to commit.
- **Do not touch** `src/Persistence.EntityFramework/NorseDbContext.cs` or `src/Persistence.EntityFramework/NorseDbContextOptionsExtensions.cs` — they carry unrelated staged work.
- TDD, red first, every task. Shouldly assertions, xUnit v3 facts with snake_case sentence names matching the existing suites (`A_no_op_update_writes_no_history_and_leaves_the_period_untouched` style). Tabs. Warnings are errors. US English.
- Live tests ride the existing `PostgresContainerFixture` (`postgres:19beta2`, Testcontainers) — Docker must be up. Snapshot tests never touch a database.
- Match the surrounding code's comment density and XML-doc style; spec-section references (§3.x) in doc comments follow the existing pattern.
- Test commands: `dotnet test tests/Persistence.EntityFramework.Tests`, `dotnet test tests/Persistence.EntityFramework.PostgreSQL.Tests`, `dotnet test tests/Persistence.EntityFramework.SqlServer.Tests` from the `Urdarbrunnr` repo root.

## Plan boundary

Fixes and verification for the six Codex findings only. No refactors beyond what a fix demands, no new capabilities, no doc updates outside the code's own doc comments (README/CLAUDE.md catch-up rides the ship train, not this plan).

---

### Task 1: Trigger rejects client-supplied `system_period` mutation (Codex P1)

**Finding:** `TemporalSqlEmitter.TriggerFunction`'s no-op guard compares only application columns and returns `NEW` untouched — so raw SQL issuing `UPDATE … SET system_period = …` with no application-column change persists an attacker-chosen period: no history row, corrupted timeline continuity, despite the column being database-owned by design. The spec's own claim ("leaves `system_period` untouched") is currently false for that input.

**Fix (fail-loud, per platform law):** at the top of the trigger function body, before the no-op guard, reject any incoming period change:

```sql
IF TG_OP = 'UPDATE' AND NEW.system_period IS DISTINCT FROM OLD.system_period THEN
	RAISE EXCEPTION 'system_period on "%.%" is database-owned; it cannot be written by clients.', TG_TABLE_SCHEMA, TG_TABLE_NAME;
END IF;
```

Nothing legitimate ever supplies the column in an UPDATE: EF never maps it, the enable-transition backfill runs before the triggers exist, and the trigger's own later `NEW.system_period :=` assignment happens after this check. A silent reset (`NEW.system_period := OLD.system_period`) was considered and rejected — silent fallbacks are banned; the write attempt is a defect at the caller and must surface.

**Files:**
- Modify: `src/Persistence.EntityFramework.PostgreSQL/TemporalSqlEmitter.cs` (`TriggerFunction` — raw string template; update its doc comment to name the guard)
- Test: `tests/Persistence.EntityFramework.PostgreSQL.Tests/TemporalApparatusIntegrationTests.cs` (live facts), plus whatever snapshot facts in `TemporalCreateTableSqlTests.cs` assert the function text (update expectations, same shape)

**Steps (TDD):**
- [ ] **Step 1 — failing live tests.** Two facts in `TemporalApparatusIntegrationTests`: (a) a raw `UPDATE` setting only `system_period` on a live temporal row throws (assert the Postgres error message names `system_period` as database-owned) and the row's period is unchanged afterwards; (b) a raw `UPDATE` changing an application column *and* `system_period` together also throws. Run — both RED against the current trigger.
- [ ] **Step 2 — implement** the guard in `TriggerFunction`. Run the two new facts — GREEN.
- [ ] **Step 3 —** run the full PostgreSQL test project (snapshot facts asserting the function text will need their expected blocks updated to match — expectation updates only, never behavior accommodations). All green.
- [ ] **Step 4 —** stage the diff and stop.

---

### Task 2: Reserve `system_period` as a column name (Codex finding 5)

**Finding:** `TemporalEntityConvention` reserves derived *table* names but not the database-owned *column*. A mapped property whose column resolves to `system_period` (e.g. a `SystemPeriod` property under the lower-snake rewriter) passes model finalize; the base `CREATE TABLE` then emits it as an application column and the emitter's `ADD COLUMN system_period` fails with a duplicate column — late, at migration time, instead of loudly at startup.

**Fix:** during each marked entity's validation in `TemporalEntityConvention.ProcessModelFinalizing`, if any mapped property's column name is `system_period` (ordinal-ignore-case), throw an `InvalidOperationException` naming the entity, the property, and the fact that the column is database-owned (same message register as the existing reservation throw). Column name resolution: `property.GetColumnName()` against the entity's root table store object — mirror how the existing code resolves table names, not a new mechanism.

**Files:**
- Modify: `src/Persistence.EntityFramework/TemporalEntityConvention.cs` (validation + class doc comment)
- Test: `tests/Persistence.EntityFramework.Tests/TemporalEntityConventionTests.cs`

**Steps (TDD):**
- [ ] **Step 1 — failing test.** A marked entity with a property explicitly mapped `HasColumnName("system_period")` (arrange like the existing collision-pair fact) throws at model finalize with a message naming the property. Second fact: an *unmarked* entity mapping `system_period` builds fine (the reservation is temporal-only). RED.
- [ ] **Step 2 — implement.** GREEN, and the full `Persistence.EntityFramework.Tests` project stays green.
- [ ] **Step 3 —** stage the diff and stop.

---

### Task 3: Split fragments enter the reserved-name set (Codex finding 3)

**Finding:** the convention's `tableNames` set is built from `GetTableName()` only; `SplitToTable` fragments are mapping fragments and never enter it, so a fragment named `{root}_history` or `{root}_timeline` passes model finalize and dies at migration time — the exact late failure the reservation exists to prevent.

**Fix:** add every entity's mapping-fragment store-object names to the set (`entityType.GetMappingFragments()` → `fragment.StoreObject.Name`), same case-insensitive comparer.

**Files:**
- Modify: `src/Persistence.EntityFramework/TemporalEntityConvention.cs`
- Test: `tests/Persistence.EntityFramework.Tests/TemporalEntityConventionTests.cs`

**Steps (TDD):**
- [ ] **Step 1 — failing test.** A marked entity with `SplitToTable("{itsTable}_history", …)` moving one column throws the reservation message at model finalize. RED (currently builds).
- [ ] **Step 2 — implement.** GREEN; project green.
- [ ] **Step 3 —** stage the diff and stop.

---

### Task 4: Reject marker transitions combined with table renames (Codex finding 2)

**Finding:** `GuardCombinedTransitions` only collides marker transitions with *column* operations. A batch that renames a table **and** flips its marker slips through, and `Generate(RenameTableOperation…)` keys temporality off the *target* model — so rename+enable fires the rename choreography (`DROP VIEW` on the old name, history-table rename, trigger retirement) against apparatus that has never existed; rename+disable strands teardown against a moved name. Same class of defect the guard already rejects for columns.

**Fix:** extend the combined-transition guard so a marker transition (`AlterTableOperation` where `IsMarkedTemporal(op) != IsMarkedTemporal(op.OldTable)`) on a table the batch also renames throws the same named diagnostic, pointing at the sanctioned path (scaffold the transition as its own migration). The transition operation carries the table's *target* name; collide it against the batch's rename map (`_renames` values), which means the guard needs access to instance state — restructure minimally (instance method or pass the map).

**Empiricism first, per house style:** the failing test drives EF's *real* differ (`TransitionOperations(fromModel, toModel)` arrange from `TemporalTransitionSqlTests`) over an unmarked `foo` → marked-and-renamed `bar` pair, and asserts on whatever the differ actually emits. If the differ produces rename+alter as reasoned, the guard test is straightforward; if it produces drop+create instead, record the actual shape in the task report and write the guard/assertions against reality, not the finding's prose.

**Files:**
- Modify: `src/Persistence.EntityFramework.PostgreSQL/NorseNpgsqlMigrationsSqlGenerator.cs` (`GuardCombinedTransitions` + its doc comment)
- Test: `tests/Persistence.EntityFramework.PostgreSQL.Tests/TemporalTransitionSqlTests.cs` (differ-driven, snapshot-level — no container needed)

**Steps (TDD):**
- [ ] **Step 1 — failing test.** Differ-driven: unmarked entity at table `foo` → marked entity at table `bar` (rename + enable in one diff). Assert generation throws the combined-transition diagnostic naming the table and the sanctioned path. Add the disable direction (marked `foo` → unmarked `bar`). RED — today this either emits broken SQL or throws the wrong error; capture what it actually does in the task report.
- [ ] **Step 2 — implement** the guard extension. GREEN.
- [ ] **Step 3 —** full PostgreSQL test project green (the existing rename-with-column-changes choreography facts must not regress — a rename *without* a transition stays fully supported).
- [ ] **Step 4 —** stage the diff and stop.

---

### Task 5: Prelude precedes the temporal `CREATE TABLE` (Codex finding 4)

**Finding:** `Generate(CreateTableOperation…)` calls `base.Generate` before `AppendPrelude`, so the emitted script places the floor/`current_schema` asserts *after* the first temporal table's `CREATE TABLE`. Inside EF's transactional apply this is harmless; in `GenerateCreateScript()`/psql-without-a-transaction workflows, a wrong `search_path` lands the unqualified main table in the wrong schema *before* the assert fires — the exact split-schema side effect the assert exists to prevent.

**Fix:** in the create override, when the table is temporal, emit the prelude *before* `base.Generate`, then the apparatus after, order otherwise unchanged. (The enable-transition path already asserts before its apparatus and alters no unqualified table beforehand — in scope only if its ordering test proves otherwise.)

**Files:**
- Modify: `src/Persistence.EntityFramework.PostgreSQL/NorseNpgsqlMigrationsSqlGenerator.cs` (create override; update the `AppendPrelude` doc comment's ordering language)
- Test: `tests/Persistence.EntityFramework.PostgreSQL.Tests/TemporalCreateTableSqlTests.cs`

**Steps (TDD):**
- [ ] **Step 1 — failing test.** A relative-position fact in the existing style: in `GenerateCreateScript()` output for a temporal model, the floor assert's index precedes the `CREATE TABLE` statement's index. RED today.
- [ ] **Step 2 — implement** the reorder. GREEN, and every existing ordering fact in the create/transition/evolution snapshot suites stays green (two-temporal-tables prelude-once fact included).
- [ ] **Step 3 —** stage the diff and stop.

---

### Task 6: SQL Server realization — prove the model shape, don't trust annotations (Codex finding 6, verification)

**Finding (contested):** Codex claims the realization hook runs too late for SQL Server's own temporal convention to react, leaving the model without period shadow properties. The mechanism claim is dubious — `SqlServerTemporalConvention` reacts to the `IsTemporal` annotation *change* immediately, not only at its finalizing pass — but the current suite genuinely cannot refute it: `SqlServerTemporalRealizationTests` asserts annotations only, never the realized model shape or the DDL.

**Verification, no container needed:**
- Assert the design-time model's realized shape: `FindProperty("SystemPeriodStart")`/`("SystemPeriodEnd")` exist as shadow properties on the marked entity.
- Assert the scaffolded DDL: drive the model through `context.Database.GenerateCreateScript()` (same pattern as the PostgreSQL snapshot suites, placeholder connection string, no database) and assert the output contains `SYSTEM_VERSIONING = ON` and the `TemporalOrderHistory` history-table name.

**Outcome routing:** if both facts are GREEN, the finding is refuted with evidence — record that in the task report (the human carries the pushback to GitHub). If either is RED, Codex caught a real defect: STOP, report BLOCKED with the failing output — the fix is a design conversation (hook timing), not an improvisation.

**Files:**
- Test: `tests/Persistence.EntityFramework.SqlServer.Tests/SqlServerTemporalRealizationTests.cs` (additive facts only; no `src/` changes expected)

**Steps:**
- [ ] **Step 1 —** write the two facts above. Run the SqlServer test project.
- [ ] **Step 2 —** GREEN → record refutation evidence in the task report; RED → BLOCKED per outcome routing. Stage and stop either way.

---

### Task 7: BEFORE INSERT guard — `system_period` is database-owned on every verb (ruled 2026-08-05)

**Ruling:** the final whole-branch review of Tasks 1–6 surfaced the INSERT residual: the Task 1 guard covers UPDATE only, and a raw `INSERT … (…, system_period) VALUES (…, tstzrange('1990-01-01','infinity'))` overrides the column default and fabricates a backdated open bound — the next legitimate UPDATE then mints a history row covering an era the row never existed. Ruled in: close the verb.

**Mechanism (the only sound fail-loud shape):** with a column `DEFAULT`, a BEFORE INSERT trigger cannot distinguish the default-applied value from a client-supplied one (the default is applied before BEFORE triggers fire), and a lower-bound freshness check against `clock_timestamp()` is a tolerance game that flakes under load. So period assignment moves *into* the versioning function:

- The trigger function gains an INSERT branch, first in the body: `IF TG_OP = 'INSERT' THEN` — RAISE (same database-owned message register as the UPDATE guard) when `NEW.system_period IS NOT NULL`; otherwise `NEW.system_period := pg_catalog.tstzrange(pg_catalog.clock_timestamp(), 'infinity'); RETURN NEW;`.
- A third trigger `{table}_versioning_insert` (BEFORE INSERT, same function) joins the pair in `Triggers`; `DropTriggersAndFunction` drops it by name alongside the other two.
- `SystemPeriodColumn` loses its `DEFAULT` clause (the column arrives `NOT NULL`; the BEFORE INSERT assignment satisfies the constraint — triggers run before constraint checks). `EnableTransition` drops its `SET DEFAULT` statement for the same reason (backfill still runs before the triggers exist, so brownfield enable cannot trip the guard).
- The clock does not change: `clock_timestamp()` everywhere, `now()` nowhere — only the assignment mechanism moves from column default to trigger.

**Known accepted residual (record, don't fix):** a data-only `pg_restore`/`COPY` into a live temporal table carries explicit periods and will now RAISE; the DBA path is `--disable-triggers`, which is that act's honest name. Full dump/restore is unaffected (pg_dump orders trigger creation after data load).

**Docs in the same change (boy-scout law):** amend spec §3.2 in `../specs/2026-08-04-temporal-tables-persistence-chassis-design.md` (one "Amendment (2026-08-05, INSERT guard)" paragraph: ownership on every verb, default removed, mechanism rationale, restore residual) and correct the Urðarbrunnr `CLAUDE.md` temporal section's "insert default and trigger alike" clause to the trigger-assigned reality (plus `README.md` if it repeats the claim).

**Files:**
- Modify: `src/Persistence.EntityFramework.PostgreSQL/TemporalSqlEmitter.cs` (`TriggerFunction`, `Triggers`, `DropTriggersAndFunction`, `SystemPeriodColumn`, `EnableTransition` + doc comments)
- Modify: `Urdarbrunnr/CLAUDE.md` (and `README.md` only if it states the default), `../Glitnir/docs/Platform/specs/2026-08-04-temporal-tables-persistence-chassis-design.md`
- Test: `tests/Persistence.EntityFramework.PostgreSQL.Tests/` — `TemporalApparatusIntegrationTests.cs` (live facts), snapshot suites wherever they assert trigger counts, the default clause, or function text (expectation updates, never weakened assertions)

**Steps (TDD):**
- [ ] **Step 1 — failing live tests.** Three facts in `TemporalApparatusIntegrationTests`: (a) a raw `INSERT` supplying an explicit `system_period` throws (message names the column as database-owned) and no row lands; (b) a normal insert (no `system_period` in the column list) succeeds with period `[~now, infinity)` — assert upper is infinity and lower is recent; (c) existing facts still pass — the enable-transition backfill fact is the canary that brownfield enable does not trip the guard. RED on (a) (currently the insert succeeds).
- [ ] **Step 2 — implement** the emitter changes. GREEN on the new facts.
- [ ] **Step 3 —** full PostgreSQL test project green — snapshot expectation updates for the third trigger, the removed `DEFAULT`, and the new function branch ride along; the live evolution/rename suites prove the third trigger recreates correctly through `CREATE OR REPLACE` and the rename choreography.
- [ ] **Step 4 —** docs per above, both repos.
- [ ] **Step 5 —** stage both repos' diffs and stop.
