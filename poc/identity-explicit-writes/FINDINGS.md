# Identity explicit-writes mechanics findings

**Run:** G1–G6 2026-10-24, `pwsh ./run.ps1` end to end (~25 s, containers and volumes removed afterward): **89 PASS / 7 FAIL / 0 PENDING**. By gate: G1 16/6, G2 12/0, G3 10/0, G4 10/0, G5 8/0, G6 15/1; the other 18 PASS are the version floors and schema checks. Scaffold verified 2026-10-24 on .NET SDK `11.0.100-rc.2.26504.105`, EF Core `11.0.0-rc.2.26504.105`, Npgsql EF `11.0.0-rc.2`, PostgreSQL `19rc1` (`server_version_num` 190000), SQL Server 2025 `17.0.5035.5` under `linux/amd64` emulation on Apple silicon. Both hand-written schemas applied cleanly and the EF model bound to them on both providers. G5 and G6 run on their own scratch databases (`identity_spike_g5`, `identity_spike_g6`, `identity_spike_g6_ef`), created and dropped by the gate.

## Verdict

**Phase 0 verdict (human gate for Phases 3–4): the design stands, with G1's statement shapes moved to raw SQL and four engine facts folded into the spec (below).** The gates Phase 3 waits on all pass: SQL Server accepts `HIDDEN` periods EF knows nothing about (G2), the fragment FK cascades by default on both providers (G3), and the whole §6.2 lifecycle — create, add/widen/drop column under versioning, disable, enable on a populated table, drop — runs on SQL Server 2025 with history preserved at every step (G5). G4's upserts and G6's claim set hold under replay and 200 rounds of two concurrent writers on both providers, with zero violations where the naive controls conflict. **G1 partially failed, and that stands:** `ExecuteUpdate` is fine for `users`-only statements; fragment updates always join the principal (correct and history-free, but never one table); `ExecuteDelete` is refused outright for split entities; and the `+1` increment returning the count needs raw SQL (`RETURNING`/`OUTPUT`). So every credentials statement, the principal delete and the increment go through the store's provider-statement helper as raw SQL, unless the human rules the read-only principal join acceptable for the credentials CAS. G6 passes only after two corrections to spec §4.6: the PostgreSQL expression it names is rejected by the engine, and the SQL Server column needs a cast and a required shadow property. Both are drafted under *Proposed spec amendments*, along with a chassis rule (generated columns are excluded from the no-op comparison) and §6.2 refinements from G5. None of them moves a ruling.

## G1 — targeted statements on a split entity

**Verdict: FAIL (partial) — the re-plan trigger fires for three of the five statement shapes.** EF can't issue the credentials statements as one-table statements, can't `ExecuteDelete` a split entity at all, and can't return a value from `ExecuteUpdate`. The `users` statement and the transaction mechanics pass on both providers. Evidence: `artifacts/gate-G1.log`, captured SQL in `artifacts/G1.<provider>.sql`.

| Statement (spec §) | `ExecuteUpdate`/`ExecuteDelete`? | Raw SQL needed? | Evidence |
|---|---|---|---|
| `users` update with stamp CAS (§4.3 step 1) | **Works**: one statement, `users` only | No | `UPDATE users AS u SET phone_number = @p, concurrency_stamp = @p3 WHERE u.id = @id AND u.concurrency_stamp = @baselineStamp` |
| credentials CAS: `PasswordHash`+`SecurityStamp` (§4.4); and by the same mechanism the lookup-hash CAS, `AccessFailedCount = 0` and `LockoutEnd` | **Runs correctly, but not one table**: EF always joins the principal | **Yes**, for a one-table statement | PostgreSQL `UPDATE user_credentials AS u0 SET … FROM users AS u WHERE u.id = u0.id AND u.id = @id AND u0.password_hash IS NULL AND …`; SQL Server `UPDATE [u0] SET … FROM [Users] AS [u] INNER JOIN [UserCredentials] AS [u0] ON …` |
| hard delete of the principal (§4.7, §7.2) | **Refused**: `InvalidOperationException: The operation 'ExecuteDelete' is being applied on entity type 'SpikeUser', which uses entity splitting. 'ExecuteDelete'/'ExecuteUpdate' operations on entity types using entity splitting are not supported.` | **Yes** | Raw `DELETE FROM users WHERE id = @p0` in an explicit transaction removes every row in all five tables by cascade (G3) |
| `access_failed_count + 1` returning the count (§4.5) | **Not expressible**: `ExecuteUpdateAsync` returns only `Task<int>` (rows affected) | **Yes** | Raw `… RETURNING access_failed_count AS "Value"` / `… OUTPUT inserted.[AccessFailedCount] AS [Value] …` through `Database.SqlQuery<int>(…).ToListAsync()`: returns 1 then 2 in one transaction, one table. 16 concurrent transactions get exactly the counts 1..16. |
| both statements in one `BeginTransactionAsync` | **Works** | — | Commit persists both; rollback undoes both, on both providers |

Notes:

- **The join is read-only.** The credentials `ExecuteUpdate` affects only fragment rows, and its CAS semantics are right: a `null` baseline hash translates to `password_hash IS NULL`, a stale baseline matches 0 rows, and G2 shows a credentials-only `ExecuteUpdate` writes no `UsersHistory` row on either provider. It fails G1 because the gate requires one table. The join comes from the shared key, which binds to the principal's `id`. A probe with no `Id` in the predicate (`WHERE normalized_user_name = …`, setting only `SecurityStamp`) still joins `users`. On SQL Server the join also reads (and briefly share-locks) the `Users` row in every credential write.
- **The `ExecuteUpdate` +1 alternative** (`SetProperty(u => u.AccessFailedCount, u => u.AccessFailedCount + 1)` and then a re-read in the same transaction) returns the right count, since the row lock is held. But it is two statements, both of which join `users`, so it FAILs too.
- **`SqlQuery` must stay uncomposed** for the `RETURNING`/`OUTPUT` statement. `Single()`/`First()` would wrap the `UPDATE` in a subquery. Materialize with `ToListAsync()`, then take the single value.
- **Forcing a non-retrying strategy** (spec §4.3, Task 4.6). A context configured with `EnableRetryOnFailure()` rejects `BeginTransactionAsync` + `ExecuteUpdate`: `InvalidOperationException: The configured execution strategy 'NpgsqlRetryingExecutionStrategy' does not support user-initiated transactions.` (`SqlServerRetryingExecutionStrategy` behaves the same).
  - Wrapping the flush in `new NonRetryingExecutionStrategy(context)` **does not help**: it throws the same exception, because it does not set `ExecutionStrategy.Current`.
  - Wrapping it in the **provider's own retrying strategy constructed with `maxRetryCount: 0`** (`new NpgsqlRetryingExecutionStrategy(context, 0)` / `new SqlServerRetryingExecutionStrategy(context, 0)`, so `RetriesOnFailure = false`) **works** on both providers. The nested configured strategy passes through.
  - On a context with the default strategy, the store's own transaction works without a wrapper.

**Implies for Tasks 4.6–4.8:**
- *Task 4.6.* The `users` statement can stay `ExecuteUpdateAsync`. Every credentials statement either goes through `ProviderStatements` as interpolated raw SQL against `user_credentials` alone, or the human accepts the read-only principal join, which is functionally correct and history-free, as a ruling. The forced non-retrying strategy is the provider retrying strategy at `maxRetryCount: 0`, never `NonRetryingExecutionStrategy`.
- *Task 4.7.* The increment is raw SQL in its own explicit transaction, as planned. `RedeemCodeAsync`'s CAS is on `user_tokens`, which is not split, so `ExecuteUpdate` stays available for it. G1 did not exercise that statement.
- *Task 4.3 (erasure).* Erasure is necessarily a raw `DELETE` on the principal.
- *Task 4.8.* No change: its upserts and enrollment inserts are raw by construction (G4).

## G2 — hidden periods

**Verdict: PASS, on both providers.** EF knows nothing of the SQL Server `HIDDEN` `SystemPeriodStart`/`SystemPeriodEnd`, and it is unaffected by them. Evidence: `artifacts/gate-G2.log`.

| Step (in sequence, one user) | SQL Server `UsersHistory` rows | PostgreSQL `users_history` rows (stand-in trigger) |
|---|---|---|
| insert through `SaveChangesAsync` (both fragments) | 0 | 0 |
| targeted `users` `ExecuteUpdate` | **1** | 1 |
| full-entity read (both fragments) | succeeds, values correct | succeeds |
| `UPDATE` that sets `PhoneNumber` to its current value | **2: the engine versions an equal-value update** | 1: the trigger skips the no-op |
| credentials-only `ExecuteUpdate` | 2 (no new row) | 1 (no new row) |
| raw `DELETE` of the principal | 3: the deleted version is retained, and all five tables are empty | 2 |

**The engine fact behind §4.3 is confirmed:** SQL Server writes a history row for every `UPDATE` that touches a row of a system-versioned table, whether or not a value changed. The store's "a no-op never issues the statement" rule is the only no-op guard on SQL Server. The PostgreSQL apparatus's no-op skip has no SQL Server counterpart.

**Implies for Tasks 4.6–4.8:**
- *Task 4.6.* `No_op_update_issues_no_users_statement_and_writes_no_history` has to be proven by the statement never being issued, on SQL Server especially. Any path that issues a `users` `UPDATE` without a real diff mints history there.
- *Task 4.6.* Credentials writes are history-free on both providers, whichever G1 shape is chosen.
- *Phase 3.* It may emit `HIDDEN` periods with no period properties in the model.

## G3 — fragment FK cascade

**Verdict: PASS, on both providers, with no fluent configuration needed.** Evidence: `artifacts/gate-G3.log`.

EF's required table-split relationship (`SplitToTable`) generates the fragment FK as `ON DELETE CASCADE` by default. The catalog was read for both the hand DDL and EF's own create script, applied to a scratch database (`identity_spike_ef`, dropped afterward):
- PostgreSQL `pg_constraint.confdeltype`: `user_claims c; user_credentials c; user_passkeys c; user_tokens c`.
- SQL Server `sys.foreign_keys.delete_referential_action_desc`: `UserClaims CASCADE; UserCredentials CASCADE; UserPasskeys CASCADE; UserTokens CASCADE`.

The relational model agrees (`FK_user_credentials_users_id→users OnDelete=Cascade`, `FK_UserCredentials_Users_Id→Users OnDelete=Cascade`). G1's raw principal `DELETE` exercises the cascade end to end.

**Implies for Tasks 4.6–4.8:** a single principal `DELETE` (Task 4.3, and the §4.7 single door) removes the credentials fragment and every child row in one statement. The migration needs no extra `OnDelete` configuration for the fragment. Task 4.13's catalog proof should still pin `confdeltype = 'c'` / `CASCADE`.

## G4 — provider upserts

**Verdict: PASS, on both providers.** Every writer ran on its own connection, inside its own explicit transaction, released together by a barrier, for 200 iterations. Evidence: `artifacts/gate-G4.log`.

- **Token upsert on one `(user_id, login_provider, name)` key**, with the row deleted before each round:
  - PostgreSQL `INSERT … ON CONFLICT (user_id, login_provider, name) DO UPDATE SET value = EXCLUDED.value`: `iterations=200 exceptions=0 undefined-final=0 final=A 93/B 107`.
  - SQL Server `MERGE [UserTokens] WITH (HOLDLOCK) …`: `exceptions=0 undefined-final=0 final=A 120/B 80`.
  
  Every round ended with exactly one row holding one writer's value. The winner varied, so the last committed value wins.
- **The race is real.** The control, a naive insert-if-absent, conflicted:
  - PostgreSQL `INSERT … SELECT … WHERE NOT EXISTS`: 200/200 rounds raised `23505`;
  - SQL Server `MERGE` without `HOLDLOCK`: 1/200 rounds raised `2627`. The emulated engine makes that window narrow, but it exists.
- **Passkey enrollment with a conflicting `credential_id`** (raw `INSERT`, explicit transaction) is a clean, catchable `DbException`. The transaction rolls back, the same context is usable afterward, and the original row is untouched.
  - PostgreSQL: **`Npgsql.PostgresException`, `SqlState = 23505` (`PostgresErrorCodes.UniqueViolation`), `ConstraintName = pk_user_passkeys`**.
  - SQL Server: **`Microsoft.Data.SqlClient.SqlException`, `Number = 2627`** (State 1, Class 14).
  - Through EF's `SaveChangesAsync`, the same conflict arrives as `DbUpdateException` wrapping those exceptions.
- **Concurrent conflicting enrollments:** 200/200 rounds had exactly one winner and one unique violation on each provider, and 201 passkey rows remained.
- A raw-SQL-inserted passkey `Data` document (`CAST(@p AS jsonb)` / `CAST(@p AS json)`) reads back through EF's JSON complex-property mapping on both providers.

**Implies for Task 4.8:**
- The token upsert statements can be used as written.
- Enrollment conflict detection keys on `PostgresException.SqlState == "23505"` / `SqlException.Number is 2627` (2601 for a unique index rather than a key), caught as `DbException` from the raw statement. On a conflict, the store rolls back and then reads the existing row to decide whether it was an identical replay (success) or a foreign credential (failure). That read must come **after** rollback: the PostgreSQL transaction is aborted by the error.

## G5 — SQL Server 2025 lifecycle

**Verdict: PASS, 8/0. Every §6.2 row is appliable as written, with history preserved at every step. Three refinements are needed (amendment B).** It ran on hand-issued DDL against one database (`identity_spike_g5`) and one opted-in table, `Principals`, with `HIDDEN` periods. A versioned `UPDATE` was written before each step and history was read after it. Composite batches and wrong orderings were probed inside a transaction under `XACT_ABORT`, then rolled back. PostgreSQL is not in scope: its lifecycle is the chassis apparatus, proven by `poc/ef-temporal-emission`. Evidence: `artifacts/gate-G5.log`.

| Step | Statements, in order | Result | Batching |
|---|---|---|---|
| create | `CREATE TABLE … [SystemPeriodStart] datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL, … PERIOD FOR SYSTEM_TIME (…) … WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[PrincipalsHistory]))` | `temporal_type` 2/1, one period, 2 hidden columns. The engine-created history table carries the period columns as plain, **not hidden**, columns. | one batch |
| `ADD` nullable column, versioning on | `ALTER TABLE … ADD [Extra] nvarchar(32) NULL` | Propagated to history (`Extra nvarchar(32) NULL`). History rows kept 2→2, and old versions read `NULL`. | own batch (below) |
| `ADD` `NOT NULL` column with default, versioning on | `ALTER TABLE … ADD [Flag] bit NOT NULL CONSTRAINT [DF_…] DEFAULT 0` | Propagated, and **existing history rows get the default too** (4/4). The history column is `NOT NULL`. | own batch |
| `ALTER COLUMN` (widen), versioning on | `ALTER TABLE … ALTER COLUMN [Name] nvarchar(128) NOT NULL` (from 64) | **It propagates to the history table automatically on a `HIDDEN`-period table**: history `Name` goes from 128 to 256 bytes, with no statement against history. Old values are intact (5/5), and a 100-character value later versions into history. | own batch |
| `DROP COLUMN`, versioning on | `ALTER TABLE … DROP COLUMN [Note]` | The column is dropped from history too. History rows kept 8→8; then +1 on the next update. | own batch |
| disable | `SET (SYSTEM_VERSIONING = OFF)` → `DROP PERIOD FOR SYSTEM_TIME` → `DROP COLUMN [SystemPeriodStart], [SystemPeriodEnd]` → `DROP TABLE [PrincipalsHistory]` | See the notes below. Final state: non-temporal, no period, no hidden columns, no history table. | **all four in one batch: OK** |
| enable on a populated table | `ALTER TABLE … ADD [SystemPeriodStart] … HIDDEN NOT NULL CONSTRAINT [DF_Principals_SystemPeriodStart] DEFAULT SYSUTCDATETIME(), [SystemPeriodEnd] … HIDDEN NOT NULL CONSTRAINT [DF_Principals_SystemPeriodEnd] DEFAULT CONVERT(datetime2, '9999-12-31 23:59:59.9999999'), PERIOD FOR SYSTEM_TIME (…)` → `SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = …))` | **All 3 existing rows share one period start**, equal to the enable timestamp: the default is evaluated once per statement, not per row, which is the chassis's single-enable-timestamp semantics. Period end is `9999-12-31 23:59:59.9999999`. The engine creates history empty. The first versioned update closes a history row opened at the enable timestamp. | **both in one batch: OK** |
| drop | `SET (SYSTEM_VERSIONING = OFF)` → `DROP TABLE [PrincipalsHistory]` → `DROP TABLE [Principals]` | History rows are still readable after `OFF` (2/2); both tables are gone. Dropping the table before the history table also works once versioning is off. | **all three in one batch: OK** |

**What the engine refuses (the required orderings):**
- Either table dropped while versioning is on: `Msg 13552` (*not a supported operation on system-versioned temporal tables*). Versioning off comes first; after that, the two drops can go in either order.
- `DROP PERIOD` while versioning is on: `Msg 13592`.
- Dropping the period columns while the period exists: `Msg 13588` (*part of period definition*).
- **Disabling a table that was *enabled*, not created**: `DROP COLUMN` on the period columns fails with `Msg 5074`, because the enable step's `DF_…` default constraints depend on them. `DROP CONSTRAINT` for both defaults must come before `DROP COLUMN`. The same batch then succeeds.
- Enable without defaults on a populated table: `Msg 4901`. A `GENERATED ALWAYS` period column can't be backfilled by `UPDATE`, so the defaults are the only enable mechanism.
- A column computed or added while versioning is on: `Msg 13724` (G6). This is the engine's own reason for the §6.2 rejection.

**Batching.** None of the §6.2 lifecycle sequences needs a batch split. Two rules do apply:
- A column added and referenced in the same batch fails at compile time (`Msg 207`, *Invalid column name*). Any DML that touches a new column belongs in a later batch. EF already issues each migration command separately.
- A statement that requires versioning to be off can't follow `SET (SYSTEM_VERSIONING = OFF)` in the same batch: the batch compiles against the pre-batch state (`Msg 13724`, G6). In that case `OFF` and the change have to be separate batches.

**Between `OFF` and `DROP PERIOD`:**
- History is retained as an ordinary table (`temporal_type` 0, 10/10 rows).
- An `UPDATE` writes no history, but **the period columns are still maintained**: row start moves on update.
- The columns stay `HIDDEN`.

After `DROP PERIOD`, the two columns become **ordinary visible `datetime2` columns** (hidden=0, no `GENERATED ALWAYS`). A disable that stopped there would leave them in `SELECT *`. The disable has to run as a single unit.

**Implies for Phase 3:** `NorseSqlServerMigrationsSqlGenerator` can emit each §6.2 row as a contiguous statement sequence. It doesn't need per-row `GO`-equivalents, and no operation needs to touch the history table except drop. Its enable and disable paths have to agree on default-constraint names (amendment B).

## G6 — claim value hash on temporal `user_claims`

**Verdict: PASS on both providers, after correcting spec §4.6's expressions (amendments A1–A2). The literal PostgreSQL expression FAILs (15/1).** Each provider ran a scratch database with hand-issued DDL (`src/Spike/Ddl/g6.*.sql`):
- SQL Server: `UserClaims` in the EF-native `IsTemporal()` shape, with history created by the engine;
- PostgreSQL: `user_claims` with a stand-in for the chassis apparatus. That stand-in is a `BEFORE INSERT/UPDATE/DELETE` trigger with explicit column lists, `clock_timestamp()` with the clamp, a `WITHOUT OVERLAPS` history key, and the §3.4 projection rule.

Evidence: `artifacts/gate-G6.log`; EF's own scripts in `artifacts/G6.ef-create.{required,nullable}.sqlserver.sql`.

**PostgreSQL**
- **The spec expression is rejected:** `GENERATED ALWAYS AS (sha256(convert_to(claim_value, 'UTF8'))) STORED` gives `42P17: generation expression is not immutable`. `convert_to(text, name)` is `STABLE` (`provolatile = 's'`); `sha256(bytea)` is immutable.
- **Two alternatives work:**
  - an `IMMUTABLE` wrapper, `utf8_sha256(value text) RETURNS bytea LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE RETURN pg_catalog.sha256(pg_catalog.convert_to(value, 'UTF8'))`. The marking is honest: the same characters give the same UTF-8 bytes whatever the server encoding;
  - pgcrypto's `digest(claim_value, 'sha256')` (immutable). It gives the same bytes on a UTF8 database, but it hashes the *database* encoding and needs `CREATE EXTENSION`.
  
  G6 runs on the wrapper.
- **Main table:** `claim_value_hash bytea GENERATED ALWAYS AS (utf8_sha256(claim_value)) STORED NOT NULL`. An explicit value in an `INSERT` is refused, `428C9` (*cannot insert a non-DEFAULT value into column*). So nothing may ever name the column in a write to `user_claims`: not the store, and not a data-only restore.
- **What the chassis trigger must do (stand-in proven):**
  1. **Copy the hash into history from `OLD`.** `OLD.claim_value_hash` is materialized in the `BEFORE UPDATE`/`DELETE` trigger. The history column is a **plain** `bytea` (`attgenerated = ''`), per the §3.4 projection rule, so the trigger's explicit `INSERT` column list *includes* it. Never `LIKE … INCLUDING GENERATED`: that would recreate it as generated in history and make the copy fail with `428C9`.
  2. **Exclude the hash from the no-op comparison.** In a `BEFORE UPDATE` trigger, `NEW.claim_value_hash` is **NULL**; stored generated columns are computed after `BEFORE` triggers. It is set in `AFTER` (probe: *BEFORE UPDATE: NEW … is NULL, OLD … is set; AFTER UPDATE: NEW … is set*). With the hash excluded, a no-op `UPDATE` writes 0 history rows. **Control:** with the hash included, the same no-op `UPDATE` writes 1, because `OLD.hash IS DISTINCT FROM NULL` defeats no-op suppression on every update.
- History after a replace and a delete holds both versions. Each version's hash equals SHA-256 over its value's UTF-8 bytes, recomputed client-side.

**SQL Server**
- **History column is materialized as a regular column**, as the spec assumed: `UserClaims.ClaimValueHash binary(32)` is `computed-persisted not-null`, and `UserClaimsHistory.ClaimValueHash binary(32)` is `plain not-null`. The engine copies the persisted value.
- **History must not declare it computed:** a pre-created history table with the computed column fails `SYSTEM_VERSIONING = ON` with `Msg 13519` (*history table … has computed column specification*). EF-native lets the engine create history, so this is only a hazard for hand-written history DDL.
- **Bare `HASHBYTES('SHA2_256', ClaimValue)` types the column `varbinary(8000)`.** The unique index then warns *maximum key length … 1700 bytes … 8528 bytes*. It works, since the actual value is 32 bytes, but it is a declared-size hazard. **`CAST(HASHBYTES('SHA2_256', [ClaimValue]) AS binary(32)) PERSISTED`** gives an exact 32-byte key.
- **EF-native, through EF's own `GenerateCreateScript()`:** `IsTemporal()` plus a `byte[]` shadow property with `HasComputedColumnSql(…, stored: true)` and `HasIndex(…).IsUnique()`.
  - **With the shadow property nullable (EF's default for `byte[]`), the script fails:** EF's SQL Server convention filters the unique index (`WHERE [ClaimValueHash] IS NOT NULL`), and the engine refuses a filter on a computed column (`Msg 10609`).
  - **With `.IsRequired()`** the index is unfiltered, the script applies, `SaveChanges` inserts, `ExecuteUpdate` versions, and `TemporalAll()` returns both versions with the right hashes.
  - EF 11 RC2's `IsTemporal()` script also emits the period columns `HIDDEN`.
- **Adding the hash to an existing temporal table:**
  - adding a computed column with versioning on is refused (`Msg 13724`), and not only for the principal opt-in;
  - the manual sequence works in **separate batches**: `OFF` → `ADD` the computed column → `ADD` a plain `binary(32) NULL` on history → `ON (…, DATA_CONSISTENCY_CHECK = ON)`. As one batch, it still fails `13724`.
  
  Himinbjörg re-adds `InitialCreate` (spec §8), so the create path is the one taken.
- **Hash input is the column's bytes, so the type matters:** `nvarchar` hashes UTF-16LE. The statements cast the parameter to `nvarchar(max)` before hashing, because a `varchar` literal of the same text hashes differently.
- **The set is binary-exact:** `'Admin'` and `'admin'` are two claims under the hash index, while `ClaimValue = N'ADMIN'` matches both under `SQL_Latin1_General_CP1_CI_AS`. Claim identity is exact-value, which is consistent with PostgreSQL.
- **`QUOTED_IDENTIFIER` must be `ON`** to write a table with an index on a computed column: with it off, `INSERT` fails with `Msg 1934`. SqlClient and EF set it on. `sqlcmd` defaults it off, so a DBA applying `schema/norse_identity.sql` that way needs `sqlcmd -I`.

**Both providers**

| Probe | PostgreSQL `INSERT … ON CONFLICT (user_id, claim_type, claim_value_hash) DO NOTHING` | SQL Server `MERGE … WITH (HOLDLOCK) … ON … t.[ClaimValueHash] = CAST(HASHBYTES('SHA2_256', s.[ClaimValue]) AS binary(32)) WHEN NOT MATCHED THEN INSERT` |
|---|---|---|
| unique index | `CREATE UNIQUE INDEX … (user_id, claim_type, claim_value_hash)`; `ON CONFLICT` infers it through the generated column | `IX_UserClaims_UserId_ClaimType_ClaimValueHash` unique, on the system-versioned table |
| replay ×3 | rows affected `[1, 0, 0]`, one row | `[1, 0, 0]`, one row |
| two concurrent writers, 200 rounds (barrier, separate connections, explicit transactions) | `exceptions=0`, every round exactly one row | `exceptions=0`, every round exactly one row |
| control: naive insert-if-absent | `INSERT … WHERE NOT EXISTS`: 200/200 rounds raised `23505` on the hash index | `MERGE` without `HOLDLOCK`: 57/200 raised `2601` |
| replace claim by `hash(@old)` | 1 row; the replayed replace matches 0 with the new value present (success per §4.6) | same |
| hash = client-side SHA-256 | over UTF-8 bytes | over UTF-16LE bytes |

**Hashes are provider-local.** `"é-ü-😀"` hashes to `5150…837F` on PostgreSQL and `73A3…68FC` on SQL Server. Nothing may compare `claim_value_hash` across providers, and nothing may compute it client-side without the provider's encoding.

**Not exercised:** a `NULL` `claim_value`. The spike's column is `NOT NULL`. Stock `IdentityUserClaim.ClaimValue` is `string?`. A nullable column would hash to `NULL`, and then the providers disagree: SQL Server's unique index admits one `NULL` per key, PostgreSQL's admits any number unless `NULLS NOT DISTINCT`. Himinbjörg should keep the column required, or hash `COALESCE`d input.

**Implies for Tasks 4.x:** the add-claim and replace-claim statements can be used as written above. The shadow property is `byte[]`, `IsRequired()`, `binary(32)` / `bytea`, with provider-specific `HasComputedColumnSql(…, stored: true)`. PostgreSQL needs the wrapper function to exist before `user_claims`.

## Proposed spec amendments

Drafted for the human gate; the spec itself is not edited here.

**A1 — spec §4.6, add-claim row (PostgreSQL expression).** Replace "`claim_value_hash` is a provider-computed, stored SHA-256 of `claim_value`" with:

> `claim_value_hash` is a provider-computed, stored SHA-256 of `claim_value`: PostgreSQL `GENERATED ALWAYS AS (utf8_sha256(claim_value)) STORED`, where `utf8_sha256(text) RETURNS bytea LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE RETURN pg_catalog.sha256(pg_catalog.convert_to($1, 'UTF8'))` is created by the migration ahead of `user_claims`. `sha256(convert_to(…))` cannot be used inline: `convert_to` is `STABLE`, and the engine refuses it in a generation expression. SQL Server `CAST(HASHBYTES('SHA2_256', [ClaimValue]) AS binary(32)) PERSISTED`. The shadow property is `byte[]` and `IsRequired()`; without that, EF filters the unique index and SQL Server refuses a filtered index on a computed column. The hash is provider-local (UTF-8 vs UTF-16LE input) and is never compared across providers. Claim identity is exact-value, regardless of collation. PoC G6.

Open for ruling: who owns `utf8_sha256`, a Himinbjörg migration step or a chassis helper. pgcrypto's `digest(claim_value, 'sha256')` is the alternative; it adds an extension prerequisite like `btree_gist`.

**A2 — chassis spec §3.2 (no-op suppression) and §3.4 (projection rule).** Append to §3.2's no-op bullet:

> The comparison list excludes stored generated columns: in a `BEFORE UPDATE` trigger `NEW.<generated>` is `NULL` (generated values are computed after `BEFORE` triggers), so comparing it marks every update a change.

Append to §3.4's projection rule:

> The trigger's history `INSERT` copies a generated column from `OLD` like any other column; no write to the main table ever names it (`428C9`).

**B — spec §6.2 lifecycle table.**

- **`DropTable`:** add "the drops may run in either order once versioning is off; one batch".
- **Column operations:** append "`AlterColumn` propagation includes `HIDDEN`-period tables; an added `NOT NULL` column's default backfills existing history rows".
- **Enable:** append "the period defaults are named `DF_{Table}_SystemPeriodStart`/`DF_{Table}_SystemPeriodEnd`; `SYSUTCDATETIME()` is evaluated once per statement, so every existing row opens at one enable timestamp; one batch".
- **Disable:** replace the emitted column with:

  > `SET (SYSTEM_VERSIONING = OFF)`, `DROP PERIOD FOR SYSTEM_TIME`, `DROP CONSTRAINT IF EXISTS` both period defaults (present only when the table was enabled rather than created), drop the period columns, drop the history table. One batch, as one unit: after `DROP PERIOD` the formerly hidden columns are visible.

- **Rejections, computed column:** append to the diagnostic text:

  > The engine itself refuses a computed column while versioning is on (`13724`). The manual sequence is `OFF` → add the column → add a plain column of the same type on history → `ON (…, DATA_CONSISTENCY_CHECK = ON)`, with `OFF` in a batch of its own.
