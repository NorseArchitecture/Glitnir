# Identity Explicit Writes, Temporal Split, and Hard-Delete Erasure — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Pairs with superpowers:test-driven-development on every coding task, per standing law (Glitnir CLAUDE.md §2.8).

**Goal:** Close Himinbjorg#47. `users` is temporal on both providers, and `user_credentials` and `user_logins` are not. A wrong password, a credential change, or a no-op update mints no history. Every Identity write is explicit, atomic and concurrency-safe under the platform's NoTracking law. Erasure is a key-first hard delete. Norse callers see only `Outcome<T>`.

**Architecture:** `NorseUserStore` implements the `IUser*Store` interfaces directly. It holds an as-read baseline per instance (`ConditionalWeakTable`), stages typed write intents, and flushes them as explicit set-based statements in one transaction. The flush has a defined commit-outcome contract (definite vs indeterminate). Urðarbrunnr gains `TemporalOnPrincipalTableOnly()`: chassis-owned SQL Server system-versioning with `HIDDEN` periods on the principal table of a split entity, with a defined migration lifecycle. Midgard's dev key store becomes linearizable per subject under a checked cross-process lock (`flock` on Unix, `LockFileEx` on Windows). An `.Outcome` extension-property view maps every Identity and OpenIddict result to `Outcome<T>`. It lives in a new non-web `Identity.Outcomes` project, except `SignInManager`'s view.

**Tech Stack:** .NET 11 RC2 (`11.0.100-rc.2.26504.105`), C# 15, EF Core 11 RC2, Npgsql EF 11 RC2, Microsoft.Data.SqlClient, ASP.NET Core Identity 11 RC2 (`Version3` schema), OpenIddict 7.7.x, xUnit v3 on MTP v2, Shouldly, NSubstitute, Testcontainers. The images are `postgres:19rc1` and **`mcr.microsoft.com/mssql/server:2025-latest`** (SQL Server 2025 is the engine floor: the provider forces compatibility level 2025, and the identity schema uses native `json`).

**Spec:** `../specs/2026-10-24-identity-explicit-writes-temporal-split-design.md` (r9). Every §-reference below is to that spec unless marked otherwise.

**Plan revision:** r4 (r3 plus the Phase 0 verdict, approved 2026-10-24: Task 3.3 and the new Task 3.5 carry A2, B and the chassis-owned hash function; Task 4.4 carries A1). It answers the plan reviews (PL1–PL8, then PL3's residue and the receipt clarification), recorded at the end.

## Global Constraints

- **No automatic git commits — platform law overrides the skill template's commit steps.** Every task ends by staging the diff and stopping for human review. Never `git commit`, never push.
- **Branch law (Bifröst CLAUDE.md §7):** never branch Bifröst. Each realm gets one feature fork, and an open fork is reused:
  - Himinbjörg: the existing `feature/access-count-breakout`;
  - Asgard: `feature/error-category-indeterminate`;
  - Midgard: `feature/subject-key-linearizable-destroy`;
  - Urðarbrunnr: `feature/temporal-principal-table-only`.
  
  The last three are created from `master` at the start of their phase, since none has an open fork today. Glitnir work lands on Glitnir `master`.
- **House rules:** `../../house-rules.md`, read in full at plan time. Specifically:
  - tabs; BOM-free UTF-8; LF;
  - target-typed `new()`; collection expressions; primary constructors;
  - expression bodies with the arrow on the declaration line;
  - `is null`/`is not null`; dot-leading fluent chains; method-chain LINQ only;
  - `ConfigureAwait(false)` in src (never in tests); every async method takes `CancellationToken cancellationToken = default` last;
  - `LoggerMessage` delegates for logging; `[GeneratedRegex]` for regex;
  - **`[LibraryImport]`** (source-generated) for P/Invoke, never `[DllImport]`;
  - every class `sealed`/`abstract`/`static`; modifiers omitted at the default;
  - tests reach internals via `InternalsVisibleTo`; a new src project gets a 1:1 test project.
- **Brownfield exemptions (spec §14), scoped to Identity- and OpenIddict-derived entities only:**
  - a shadow property where a CLR member would change the drop-in shape (`claim_value_hash`);
  - hand-written `.IsRequired()` on inherited `string?` members, with a comment naming the base type.
- **`Outcome<T>` over throwing (spec §4.12):** exceptions appear only in the store's fixed-signature adapters, at the framework-boundary adapters (which fold them to the framework's own contract), and in startup or infrastructure failure.
- **Tests:**
  - Shouldly, NSubstitute and `Xunit` are global via `Directory.Test.props`.
  - Test classes are `public sealed`; methods are bare `void`/`async Task`, with sentence-shaped underscore names.
  - **VSTest `--filter` does not work:** use `dotnet test tests/<Project> -- --filter-class "*.<ClassName>"`.
- **SQL Server container guard (ruled 2026-10-25):** every SQL Server Testcontainers test skips on **Windows on ARM only** (`OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64`), using Midgard's `[Fact(SkipUnless = …, Skip = …)]` shape with a reason naming the platform. The 2025 image dies under QEMU there; it runs on Apple silicon under Rosetta (`--platform linux/amd64`), and x64 CI always runs it. Midgard's guard was widened to match on 2026-10-25 (test-only, to Midgard `master`): its SQL Server suite now runs on Apple silicon, 37 passed and 0 skipped.
- **Never assert on a migration name.** Container facts assert on the catalog: `pg_class`/`pg_trigger`/`pg_constraint`; `sys.tables.temporal_type`/`sys.periods`/`sys.check_constraints`.
- **Warnings are errors, IDE0055 included.** Repeated-hit protocol per house rules.
- **Every task states its prerequisites.** Only tests whose prerequisites already exist are written in that task. A task's checkpoint is "the whole solution builds, and every test that existed at the start, plus the task's new tests, is green", unless the task names a test it deliberately flips.
- Realm paths are workspace-relative from the Bifröst root.

## Plan boundary and gates

| Phase | Realm | Gate before it starts |
|---|---|---|
| 0 | Glitnir PoC (`poc/identity-explicit-writes`) | Spec r7 accepted |
| 1 | Asgard | none (independent of the PoC) |
| 2 | Midgard | none (independent of the PoC; Task 2.1 is its own V8 gate) |
| 3 | Urðarbrunnr | **The Phase 0 verdict** on G2, G3 and G5 |
| 4 | Himinbjörg | **The Phase 0 verdict** on G1, G4 and G6, plus Phases 1–3 published |
| 5 | Bifröst | Phase 4 shipped |

**Re-plan triggers:**
- G1 fails → raw-SQL statements (Tasks 4.6–4.9 change shape). **Fired in part, ruled 2026-10-24:** every `user_credentials` statement, the erasure `DELETE` and the increment are raw SQL in `ProviderStatements`; only the `users` statement stays `ExecuteUpdate` (spec §12 G1);
- G6 fails → the claims "unresolved" fallback (Task 4.4 drops the hash column; Task 4.8 changes);
- V7 fails → the passkey guard's premise breaks;
- **V8 finds an unsupported or unchecked primitive on a supported platform** → stop. That platform is declared unsupported, or Phase 2 is re-planned. It is never given an unchecked fallback.

`subagent-driven-development` never runs more than one task ahead of review. On a genuine implementation wall it halts and asks.

## Dependency graph after this plan (Himinbjörg)

```
Identity.EntityFramework ──► Identity.Outcomes (NEW, Microsoft.NET.Sdk) ──► Identity.Migrations
          │                          │
          └──────────────► Identity.Web.Server ◄──┘
```

`Identity.Migrations` must never acquire `Identity.Web.Server` or the `Microsoft.AspNetCore.App` framework reference (Task 4.12 pins this).

## File Structure

```
Glitnir/
	poc/identity-explicit-writes/            (Phase 0 — NEW throwaway: README.md, run.ps1, docker-compose.yml, FINDINGS.md, src/Spike/)
Asgard/
	src/Abstractions.Contracts/ErrorCategory.cs                  (1.1)
	src/Abstractions.Backend/Keys/ISubjectKeyStore.cs            (1.2 docs)
Midgard/
	src/Infrastructure.Backend/Keys/DevelopmentSubjectKeyStore.cs (2.2–2.3)
	src/Infrastructure.Backend/Keys/SubjectDestroyLock.cs         (2.2 NEW internal)
	src/Infrastructure.Backend/Keys/UnixFileLock.cs               (2.2 NEW internal: [LibraryImport("libc")] flock)
	tests/Infrastructure.Backend.Tests/Keys/…                     (2.1–2.3, with a child-process lock-holder entry point)
Urdarbrunnr/
	src/Persistence.EntityFramework/TemporalEntityTypeBuilderExtensions.cs, NorseAnnotationNames.cs   (3.1)
	src/Persistence.EntityFramework.SqlServer/NorseSqlServerEfProvider.cs                          (3.1–3.2)
	src/Persistence.EntityFramework.SqlServer/NorseSqlServerAnnotationProvider.cs                  (3.2 NEW: relational seam)
	src/Persistence.EntityFramework.SqlServer/NorseSqlServerMigrationsAnnotationProvider.cs        (3.2 NEW: migrations seam)
	src/Persistence.EntityFramework.SqlServer/NorseSqlServerMigrationsSqlGenerator.cs              (3.2–3.3 NEW)
	tests/Persistence.EntityFramework.SqlServer.Tests/…                                            (3.1–3.3)
	tests/Persistence.EntityFramework.SqlServer.IntegrationTests/                                  (3.4 NEW)
Himinbjorg/
	src/Identity.EntityFramework/NorseUser.cs, NorseUserClaim.cs, NorseUserLogin.cs, NorseIdentityDbContext.cs (4.1, 4.4)
	src/Identity.Outcomes/                                        (4.12 NEW project + tests/Identity.Outcomes.Tests/)
	src/Identity.Web.Server/NorseUserStore.cs                     (4.11 replaced by the explicit store)
	src/Identity.Web.Server/Store/…                               (4.5–4.9 NEW internal: ExplicitUserStore, UserBaseline, StagedIntent, FlushExecutor, ProviderStatements)
	src/Identity.Web.Server/NorseUserManager.cs, NorseSignInManager.cs, IdentityRevalidatingAuthenticationStateProvider.cs (4.10, 4.12)
	src/Identity.Web.Server/ErasureService.cs                     (4.3)
	src/Identity.Web.Server/Components/Pages/ConfirmEmailChange.razor, Manage/DeletePersonalData.razor (4.3, 4.10)
	src/Identity.Web.Server/*Handler.cs, OpenIddictExchangeEndpoint.cs; src/Identity.Migrations/MachineClientSeedContributor.cs (4.12)
	src/Identity.Migrations.PostgreSQL|SqlServer/Migrations/, schema/norse_identity.sql (4.1, 4.4, re-verified 4.13)
	tests/Identity.Web.Server.Tests/…                             (4.2 onward)
	CLAUDE.md, README.md                                          (4.15)
```

---

## Phase 0 — Mechanics PoC (Glitnir)

### Task 0.1: Scaffold `poc/identity-explicit-writes`

**Prerequisites:** none.

- [ ] Mirror `poc/ef-temporal-emission/`'s shape: `run.ps1`, `docker-compose.yml`, `src/Spike`, `FINDINGS.md`, `README.md`.
- [ ] `docker-compose.yml`: `postgres:19rc1` and **`mcr.microsoft.com/mssql/server:2025-latest`** (`platform: linux/amd64` for Apple silicon), on fixed non-default ports. `run.ps1` asserts `SERVERPROPERTY('ProductMajorVersion') >= 17` and `server_version_num >= 190000` before any gate runs, and aborts otherwise.
- [ ] `Spike.csproj`: a console app referencing EF Core RC2, Npgsql and SqlServer, with **no Norse packages**. It configures SQL Server with `UseCompatibilityLevel(170)`, matching the platform floor.
- [ ] `Model.cs`: `SpikeUser`, split into `users`/`user_credentials` per spec §3; `SpikeClaim` with an unbounded `ClaimValue`; `SpikeToken`; `SpikePasskey`, with `Data` as a JSON-mapped column so the native `json` type is exercised.
- [ ] Hand-written DDL per provider:
  - PostgreSQL: a minimal history trigger that skips no-op updates;
  - SQL Server: the spec §6.1 `HIDDEN` period block on `Users`.
- [ ] `run.ps1 -OnlyShape G1` and so on, with PASS/FAIL per assertion and raw output in `artifacts/`.
- [ ] Stage; stop for review.

### Task 0.2: Gates G1–G4 (statement shapes)

**Prerequisites:** Task 0.1.

- [ ] **G1 — targeted statements on a split entity:**
  - `ExecuteUpdateAsync` touching only `users` columns, then only `user_credentials` columns; capture the SQL and confirm one table each;
  - the same statements inside `BeginTransactionAsync` under a **non-retrying** execution strategy;
  - `ExecuteDeleteAsync` on the principal;
  - `+1 RETURNING`/`OUTPUT` (through `ExecuteUpdate` if possible, otherwise raw SQL) **inside an explicit transaction**, as Task 4.7 will issue it.
  
  Record, per statement, whether `ExecuteUpdate`/`ExecuteDelete` works or raw SQL is required.
- [ ] **G2 — hidden periods**, with no period properties in the EF model:
  - insert, targeted update, full-entity read and delete all succeed;
  - one update produces exactly one `UsersHistory` row;
  - **an `UPDATE` that sets equal values also produces a history row** (the engine fact behind §4.3).
- [ ] **G3 — fragment FK cascade:** read the generated fragment FK's `ON DELETE` from the catalog on both providers. If it isn't `CASCADE`, record the fluent configuration that makes it so.
- [ ] **G4 — provider upserts:** two concurrent writers behind a barrier, on separate connections.
  - `ON CONFLICT … DO UPDATE` and `MERGE … WITH (HOLDLOCK)` on one `user_tokens` key: no unhandled violation, and a defined final value.
  - Passkey enrollment `INSERT` with a conflicting `credential_id`: a clean, catchable unique violation.
- [ ] Write the G1–G4 verdicts into `FINDINGS.md`. Stage; stop.

### Task 0.3: Gates G5–G6 (lifecycle and claim hash)

**Prerequisites:** Task 0.2.

- [ ] **G5 — SQL Server 2025 lifecycle (spec §6.2).** Hand-issued DDL on one database, with a history row written before and read after each step:
  - create;
  - `ADD`, `ALTER` (widen) and `DROP COLUMN` on the principal table with versioning on;
  - disable;
  - enable on a populated table;
  - drop.
  
  Record the required statement ordering and batching.
- [ ] **G6 — claim value hash on temporal `user_claims`:**
  - SQL Server: a persisted `HASHBYTES('SHA2_256', claim_value)` under EF-native `IsTemporal()` (the history column is materialized as a regular column);
  - PostgreSQL: `GENERATED ALWAYS AS (sha256(convert_to(claim_value, 'UTF8'))) STORED`, with a stand-in for the chassis trigger's history copy (record the column exclusion the chassis must apply);
  - a unique index on `(user_id, claim_type, claim_value_hash)`;
  - insert-if-absent idempotent under replay and under two concurrent writers.
- [ ] Write the G5–G6 verdicts and a **one-paragraph overall verdict** into `FINDINGS.md`. Draft a spec amendment in the same change if any gate moves an assumption. **Stage; stop: human gate for Phases 3–4.**

---

## Phase 1 — Asgard

### Task 1.1: `ErrorCategory.Indeterminate = 12`

**Prerequisites:** none. **Files:** `Asgard/src/Abstractions.Contracts/ErrorCategory.cs`, its test project.

- [ ] **Failing test:** `Indeterminate_is_twelve_and_every_member_is_explicit`. It asserts the value is 12, the full member list is as expected, and `0..12` is contiguous.
- [ ] Add the member: `/// <summary>The operation may or may not have taken effect; re-read before acting.</summary>`, plus a `<remarks>` naming commit-acknowledgement loss. **Also the `TransportDispositions.For` arm**, `Indeterminate => new(500, 4, true)` (spec ruling). It is in the same assembly and has no default arm by design, so the enum can't compile without it; a failing `TransportDispositionsTests` case pins it first. If `Outcome`'s wire contract enumerates `ErrorCategory` explicitly (`[ProtoEnum]`/`DataContract`), add the member there and extend the existing round-trip test.
- [ ] Green. Stage; stop.

### Task 1.2: `ISubjectKeyStore.DestroyAsync` linearizability contract (docs)

**Prerequisites:** none.

- [ ] Add `<remarks>` to `DestroyAsync`: linearizable per subject; **one authoritative receipt**, whose authority begins at durable publication of the pending receipt, after which every caller and every retry (across restarts) receives it; a failure before publication leaves the key intact, and a retry completes destruction under a fresh candidate; a pending destruction is completed by the next call; providers that can't guarantee this aren't conforming (spec §7.5). Build. Stage; stop.

### Task 1.3: V9 — Asgard-internal switch arms; inventory elsewhere

**Prerequisites:** Task 1.1.

- [ ] Build Asgard and fix every exhaustiveness error inside Asgard. Each fix gets a failing test first that feeds `Indeterminate` through the switch.
- [ ] **Inventory only, no edits** in other realms: build each consuming realm against the local Asgard (the workspace crossing) and list the CS8509/CS8524 sites. Himinbjörg's sites are fixed in Task 4.1; other realms' sites become follow-on chores listed in Phase 5.
- [ ] Stage; stop. **Ship gate (Asgard):** PR merged, CI green, tagged, published.

---

## Phase 2 — Midgard

### Task 2.1: V8 — prove the lock primitives

**Prerequisites:** none.

- [ ] A throwaway probe test project (deleted at the end of the task) that, on **macOS, Linux (CI) and Windows (CI)**:
  - Unix: opens one file through two `FileStream` handles in one process and calls `flock(fd, LOCK_EX | LOCK_NB)` via `[LibraryImport("libc", SetLastError = true)]` on each. Proves the second call returns `-1` with `EWOULDBLOCK`. Repeats across two processes;
  - Windows: proves `FileStream.Lock(0, 1)` on a second handle throws `IOException`, in-process and cross-process;
  - all platforms: records that `FileStream.Lock` on macOS throws `PlatformNotSupportedException`. That's the reason it isn't used on Unix.
- [ ] Record the verdict in the task summary. **If any of macOS, Linux or Windows lacks a checked, cross-handle, cross-process primitive, stop: that's a re-plan trigger.**
- [ ] Stage the summary only; stop.

### Task 2.2: Checked per-subject lock and startup self-test

**Prerequisites:** Task 2.1 verdict green for macOS and Linux (Windows is verified on this task's CI leg). **Descriptor ruling (2026-10-24, from V8):** on Unix the lock descriptor comes from libc `open(path, O_RDWR | O_CLOEXEC)`, never from a `FileStream`, because the runtime takes its own unchecked `flock` on every `FileStream` open. The file is ensured to exist first by a .NET open-or-create disposed at once (an `IOException` there means it exists and is held). `O_CLOEXEC` is `0x1000000` on macOS and `0x80000` on Linux. **Files:** `UnixFileLock.cs` (`[LibraryImport("libc", SetLastError = true)]` for the two-argument `open(string path, int flags)` returning `SafeFileHandle`, and `flock(SafeFileHandle fd, int operation)`), `SubjectDestroyLock.cs`, `DevelopmentSubjectKeyStore.cs`, tests under `Infrastructure.Backend.Tests/Keys/`.

- [ ] **Failing tests:**
  - `Lock_held_by_another_handle_in_process_blocks_until_exhaustion`;
  - `Lock_held_by_another_process_blocks_until_exhaustion` (a child-process lock holder launched from the test assembly's helper entry point, on a shared temp root);
  - `Primitive_failure_is_treated_as_not_held` (an internal seam that substitutes the primitive, test-only);
  - `Constructor_throws_when_exclusion_cannot_be_demonstrated` (the seam forces the self-test to fail);
  - `Constructor_succeeds_on_a_local_filesystem`.
- [ ] Implement `SubjectDestroyLock.AcquireAsync(root, subjectId, cancellationToken)`:
  - a per-subject in-process `SemaphoreSlim` first (ordering only);
  - then ensure `{subjectId}.destroy.lock` exists and take the platform primitive: on `OperatingSystem.IsMacOS() || IsLinux()`, native `open` then `flock` with `LOCK_EX | LOCK_NB`; on Windows, `FileStream` then `FileStream.Lock`. Check the result on every call;
  - bounded retry with backoff; `TimeoutException` on exhaustion;
  - any primitive failure means "not held";
  - return an `IAsyncDisposable` that releases the file lock, then the semaphore;
  - unsupported operating systems throw `PlatformNotSupportedException` from the constructor.
- [ ] Implement the constructor self-test: a probe file, locked through handle A, must refuse handle B. On failure, throw `InvalidOperationException` naming the root and the supported filesystems.
- [ ] Green on macOS locally and on Linux (the `mcr.microsoft.com/dotnet/sdk:11.0.100-rc.2` container locally, then the CI leg). Windows is proven on its CI leg, which also runs the retained V8 probe's Windows half; until it's green, Windows support is unverified. Stage; stop.

### Task 2.3: Linearizable `DestroyAsync`

**Prerequisites:** Task 2.2.

- [ ] **Failing tests:**
  - `Concurrent_destroys_in_process_return_the_same_receipt_as_stored` (16 tasks, barrier);
  - `Concurrent_destroys_across_processes_return_the_same_receipt_as_stored`;
  - `Failure_before_publication_leaves_the_key_intact_and_no_receipt` (the injected failure is between minting and the pending write; asserts the key file, no pending file, no promoted file);
  - `Retry_after_failure_before_publication_completes_destruction` (asserts a published, promoted receipt and a deleted key; deliberately doesn't compare it with the lost candidate);
  - `Retry_after_failure_after_pending_write_returns_the_published_receipt`;
  - `Retry_after_failure_after_key_delete_returns_the_published_receipt`;
  - `Retry_after_publication_survives_a_new_store_instance` (a fresh `DevelopmentSubjectKeyStore` over the same root stands in for a process restart; it returns the published receipt byte-for-byte);
  - `Destroy_without_the_lock_leaves_the_key_and_writes_no_receipt`.
- [ ] Rewrite `DestroyAsync` per spec §7.5 under `await using` the lock:
  - a promoted receipt → return it;
  - a pending receipt → delete the key if present, promote, return;
  - the key → mint, write the pending file atomically (temp file, flush, `File.Move(temp, pending, overwrite: false)`), delete the key, promote, return;
  - otherwise → `KeyMissingException`.
- [ ] **The lock-free readers (found in review, ruled into this task 2026-10-24):**
  - `GetAsync`/`GetOrCreateAsync` check **key → pending → receipt**, never receipt before pending. Promotion makes the receipt exist before the pending file disappears, so a reader can't fall between them; the old order let `GetOrCreateAsync` re-key an erased subject. A deterministic reader-checkpoint seam promotes between the checks, and the tests assert `KeyDestroyedException`, no new key file, and `Destroyed`;
  - `GetOrCreateAsync`'s first mint runs **under the subject's lock** (re-checking key, pending and receipt inside it), then publishes through a write-through temp file and a move. A lock-free `File.Move(overwrite: false)` was tried first and failed its own concurrency test: on Unix, .NET's no-overwrite move is an existence check followed by `rename(2)`, not an atomic no-overwrite, so it can't decide a race. The existing-key read stays lock-free, and `GetOrCreateAsync` can now throw `TimeoutException` on lock exhaustion (an infrastructure failure, nothing minted). This bug predated the plan: `File.WriteAllBytesAsync` let a second concurrent mint overwrite the first. Tests: `Concurrent_first_GetOrCreate_in_process_returns_one_key` and `…_across_processes_returns_one_key`.
- [ ] The existing `DevelopmentSubjectKeyStore` tests stay green unchanged. Stage; stop.

### Task 2.4: `Indeterminate` at Midgard's transport and telemetry edges

**Prerequisites:** Asgard published with `ErrorCategory.Indeterminate` (Phase 1). These are the two Midgard gaps the Task 1.3 inventory found (ruled into this train 2026-10-24).

- [ ] **Failing tests:**
  - `Indeterminate_problem_rich_status_carries_DeadlineExceeded` (`ProblemExtensions.ToRpcException`: today `MapToGoogleRpcCode` has no `DeadlineExceeded` arm, so the `google.rpc.Status` trailer says `Unknown` (2) while the outer status says 4);
  - `Indeterminate_failure_logs_at_warning_with_correlation_id` (`TelemetryBehavior`: today only `Fault` gets the Warning-level, correlation-id log).
- [ ] Add `StatusCode.DeadlineExceeded => Code.DeadlineExceeded` to `MapToGoogleRpcCode`, and route `Indeterminate` through `TelemetryBehavior`'s Fault-level logging.
- [ ] Green. Stage; stop. **Ship gate (Midgard):** PR merged, CI green on all three operating systems, tagged, published.

---

## Phase 3 — Urðarbrunnr (gated on G2, G3, G5)

### Task 3.1: `TemporalOnPrincipalTableOnly()` replaces the park

**Prerequisites:** Phase 0 verdict.

- [ ] **Failing tests:**
  - `Split_temporal_entity_without_opt_in_throws_naming_TemporalOnPrincipalTableOnly`;
  - `Opted_in_split_entity_is_not_EF_temporal_on_SQL_Server`;
  - `Opted_in_split_entity_stamps_the_principal_table_mapping_only`;
  - `Non_split_temporal_entity_remains_EF_native`;
  - `Opt_in_is_a_no_op_on_PostgreSQL`.
- [ ] Add the opt-in (annotation `Norse:TemporalPrincipalTableOnly`). **Delete** `TemporalParkedOnSqlServer()`, its annotation name, and the tests pinning them, replacing each with its opt-in equivalent. Rewrite the realization hook per spec §6.1.
- [ ] Green. Stage; stop.

### Task 3.2: The three SQL Server seams; create and drop through the real differ

**Prerequisites:** Task 3.1. **Files:** `NorseSqlServerAnnotationProvider.cs` (relational, `IRelationalAnnotationProvider`), `NorseSqlServerMigrationsAnnotationProvider.cs` (`IMigrationsAnnotationProvider`, removal annotations), `NorseSqlServerMigrationsSqlGenerator.cs`. Wire **all three** in `NorseSqlServerEfProvider.Configure` with `ReplaceService`, matching `NorsePostgresEfProvider` (`Urdarbrunnr/src/Persistence.EntityFramework.PostgreSQL/NorsePostgresEfProvider.cs:51-53`).

- [ ] Read `NorseNpgsqlAnnotationProvider`'s rationale comment first (`NorseNpgsqlAnnotationProvider.cs:14`) and mirror its reasoning. The relational seam is what lets an annotation-only change (enable/disable) surface as a migration operation.
- [ ] **Failing tests**, every one generated by EF's **real** `IMigrationsModelDiffer` between two built models, with the SQL produced by the real generator and no hand-annotated operations:
  - `Diff_from_empty_to_opted_in_model_emits_hidden_periods_and_versioning`;
  - `Diff_emits_no_temporal_DDL_for_the_fragment_table`;
  - `Diff_from_opted_in_model_to_empty_disables_versioning_then_drops_history_then_table`.
- [ ] Implement the create path (spec §6.1, inline in `CREATE TABLE`) and the drop path, in the ordering G5 confirmed.
- [ ] Green. Stage; stop.

### Task 3.3: Evolution, enable/disable, rejections — through the real differ

**Prerequisites:** Task 3.2.

- [ ] **Failing tests** (real differ between model pairs):
  - add, drop and alter a principal column (emitted with versioning left on);
  - `Adding_the_opt_in_to_an_existing_model_emits_enable` (an annotation-only change);
  - `Removing_the_opt_in_emits_disable`;
  - fragment column changes emit plain DDL;
  - one named-diagnostic test per rejected operation: PK change, rename, schema move, computed or identity column on the principal, split or unsplit within one migration.
- [ ] **G5's confirmed ordering** (spec §6.2, r9): enable names the period defaults `DF_{Table}_SystemPeriodStart`/`End`; disable drops them with `DROP CONSTRAINT IF EXISTS` before the period columns, in one batch; the computed-column rejection names the manual `OFF`-in-its-own-batch sequence. Tests: `Disable_after_enable_drops_the_period_defaults` (an enabled, not created, table disables cleanly), plus the diagnostic text.
- [ ] Implement, following the PostgreSQL generator's structure and diagnostics (chassis spec §3.3–3.4). Read the PostgreSQL disable path and mirror its history handling; don't re-derive it.
- [ ] Green. Stage; stop.

### Task 3.4: SQL Server 2025 container fixture (the platform's first)

**Prerequisites:** Task 3.3. **Files:** `tests/Persistence.EntityFramework.SqlServer.IntegrationTests/` (new project and `slnx` entry), on `Testcontainers.MsSql` with **`mssql/server:2025-latest`**.

- [ ] **Failing tests:**
  - `Engine_is_SQL_Server_2025_or_later` (`ProductMajorVersion >= 17`);
  - `Fragment_only_update_writes_no_history_row`;
  - `Principal_update_writes_one_history_row`;
  - `Same_value_update_writes_a_history_row`;
  - one test per §6.2 supported operation, applied through generated migrations with history written before and read after;
  - `Enable_on_populated_table_opens_existing_rows_at_enable_time`;
  - `Disable_then_drop_tears_down_cleanly`.
- [ ] Implement only what the failures demand. Update the Urðarbrunnr README.md/CLAUDE.md pair (the opt-in, the lifecycle, the fixture). Stage; stop.

### Task 3.5: PostgreSQL generated columns in the chassis (A2) and `norse_utf8_sha256`

**Prerequisites:** Task 3.4. **Files:** the PostgreSQL chassis's trigger emitter (`TemporalSqlEmitter`) and `NorseNpgsqlMigrationsSqlGenerator`, with their tests (unit and container).

- [ ] **Failing tests:**
  - `No_op_comparison_excludes_stored_generated_columns` (emitted trigger SQL);
  - `History_insert_copies_generated_columns_from_OLD`;
  - container: `No_op_update_on_a_table_with_a_generated_column_writes_no_history_row`, plus `Generated_column_value_reaches_history_on_a_real_update`;
  - `Computed_column_naming_norse_utf8_sha256_emits_the_function_first` (idempotent `CREATE OR REPLACE FUNCTION`, ahead of the table); container: the function exists and is `IMMUTABLE`.
- [ ] Implement per chassis spec §3.2/§3.4 (amended) and spec §4.6 (r9). The function is emitted only when a computed column's SQL names it.
- [ ] Green. Update the Urðarbrunnr README.md/CLAUDE.md pair. Stage; stop. **Ship gate (Urðarbrunnr):** PR merged, CI green, tagged, published.

---

## Phase 4 — Himinbjörg (gated on G1, G4, G6 + Phases 1–3 published)

Phase 4 runs as executable slices: **adopt the packages → fixtures → erasure (it works on the current model) → email-is-username manager behavior, then the model and migrations that enforce it → build the explicit store alongside the old one → swap → Outcome surface → proofs.** The old `NorseUserStore` stays registered until Task 4.11, so every checkpoint before then runs real seeding and existing tests against a working store.

### Task 4.1: Package adoption (compile-level only)

**Prerequisites:** Asgard, Midgard and Urðarbrunnr published.

- [ ] Bump the Asgard, Midgard and Urðarbrunnr references on `feature/access-count-breakout`. **In the same task:**
  - replace `builder.TemporalParkedOnSqlServer()` in `NorseUser.cs` with `builder.TemporalOnPrincipalTableOnly()`, and update the comment;
  - add the Himinbjörg `ErrorCategory.Indeterminate` arms the Task 1.3 inventory listed, each with a failing test first.
- [ ] The SQL Server model now realizes system-versioning, so **regenerate both providers' `InitialCreate`** (Himinbjörg CLAUDE.md procedure) to clear EF's pending-model-changes check. Postgres should produce an unchanged diff; review both.
- [ ] `NorseIdentityTemporalModelTests.Sql_server_realizes_the_stamp_as_engine_native_system_versioning` is **rewritten here** to assert the opt-in realization: hidden periods in the migration SQL, and `IsTemporal()` false.
- [ ] Checkpoint: `dotnet build Himinbjorg.slnx` succeeds and `dotnet test` is **fully green**, including the rewritten SQL Server temporal test (it was the fourth known failure, red only because of the park). The three Configure-test failures were triaged ahead of this task (see Task 4.2). Stage; stop.

### Task 4.2: Triage and fixtures

**Prerequisites:** Task 4.1.

- [x] **Triage of the three pre-existing failures** (`NorseUserRoleConfigureTests`, `NorseRoleConfigureTests`, `NorseUserConfigureTests`): **done ahead of Task 4.1** (2026-10-24, staged on `feature/access-count-breakout`). Root cause: commit `6eb9ade` removed hand-written `.IsRequired()` per house rules, and those tests built the model with a bare `ModelBuilder()`, which runs no conventions. The model was right. The three relationship tests moved into `NorseIdentityModelTests` against the real per-provider model, with no assertion weakened; no ruling needed.
- [ ] **`SqlServerIdentityFixture`**: the twin of `PostgresIdentityFixture` (same API, its own dev-keys temp root) on `mssql/server:2025-latest`. On startup it asserts `ProductMajorVersion >= 17` and that the **complete identity migration** applies, including the passkey table's native `json` column.
- [ ] **Provider-parameterized suites:** an abstract base suite with two `public sealed` derived classes bound to each fixture's collection. Promote `TemporalIdentityVersioningTests` to that shape first; its pinned `A_failed_password_attempt_currently_versions_users` must pass on **both** providers with the current store, which proves the fixture.
- [ ] **Fixture options:**
  - `UseExplicitStore` (default `false`): registers `Store.ExplicitUserStore` in place of `NorseUserStore`. Used from Task 4.5 onward;
  - `CommitFailure` seams (Task 4.9).
- [ ] Checkpoint green. Stage; stop.

### Task 4.3: Erasure — hard delete, key first

**Prerequisites:** Task 4.2; Midgard's linearizable `DestroyAsync` (Phase 2). This runs on the **current** model: a hard delete needs nothing from the new store, and it removes the existing ceremony's write of `NULL` into the lookup hashes before Task 4.4 makes them `NOT NULL`.

- [ ] **Failing tests** (both providers):
  - `Failure_during_destroy_then_retry_completes`;
  - `Failure_after_destroy_before_transaction_then_retry_completes`;
  - `Failure_after_commit_then_retry_returns_same_receipt`;
  - `Absent_row_live_key_leftover_OpenIddict_rows_cleanup_failure_then_retry_cleans_up`;
  - `Pending_receipt_then_retry_promotes_it`;
  - `Missing_key_and_absent_row_is_NotFound_with_no_writes`;
  - `Missing_key_with_present_row_is_Fault`;
  - `Concurrent_shreds_return_the_same_receipt`;
  - `Re_registration_of_erased_email_succeeds`;
  - `Lock_exhaustion_maps_to_Fault_not_an_exception`.
  
  The indeterminate-commit erasure case waits for Task 4.9's seam and is added there.
- [ ] Rewrite `ErasureService.ShredAsync` per §7.1: probe; an unconditional `DestroyAsync` unless `Missing`; an unconditional idempotent transaction (OpenIddict tokens and authorizations by subject, then the `users` row, through a **raw `DELETE`**: G1 found `ExecuteDelete` is refused for split entities, and the raw delete cascades to every child table); `Outcome<ErasureReceipt>`. Rewrite the doc comment. Re-point `DeletePersonalData.razor` at `ShredAsync`. Delete the obsolete darken-ceremony tests, replacing each with its hard-delete counterpart.
- [ ] Checkpoint green. Stage; stop.

### Task 3.6: Fragment-scoped CHECK constraints (found in Task 4.4; ruled 2026-10-25)

**Prerequisites:** Phase 3 published (v0.0.17). Lands on Urðarbrunnr's `feature/fragment-check-constraints` as its own patch release (the nullable-to-required guard had already shipped in v0.0.18 via PR #68).

- [ ] **Failing tests**, through EF's real differ and generator, on **both providers**:
  - `Fragment_check_constraint_is_emitted_on_the_fragment_table_not_the_principal`;
  - `Adding_a_fragment_check_constraint_emits_ADD_CONSTRAINT_on_the_fragment`, and removing it emits `DROP CONSTRAINT`;
  - the model snapshot carries the annotation, so a regenerated migration reproduces it;
  - container: the constraint exists in the catalog on the fragment table and rejects a violating direct write.
- [ ] Implement the seam (a fluent extension on the split-table builder that stamps the fragment's mapping; relational annotation projection onto the fragment table; both generators emit it), following the `TemporalOnPrincipalTableOnly` pattern. Update the Urðarbrunnr README.md/CLAUDE.md pair. File an upstream EF issue draft in the task summary (not filed).
- [ ] Green. Stage; stop. **Ship gate:** with the nullable guard, one patch release.

### Task 4.4: Email is the username, then model law and migrations (one slice)

**Prerequisites:** Task 4.3; Urðarbrunnr Task 3.6 (local workspace crossing until published); and Urðarbrunnr Task 3.5 (local workspace crossing; the published package only gates the PR). The manager behavior lands **before** the `CHECK` that requires it, in the same slice, against the **old** store. The stock manager's `SetEmailAsync` doesn't touch the username, so `TemporalIdentityVersioningTests.An_email_change_through_the_user_manager_writes_a_closed_version_into_users_history` (which calls `SetEmailAsync` alone) would violate the constraint without it.

- [ ] **Failing manager tests** (old store, both providers; store-agnostic by construction, because they override at the manager and finish with one `UpdateUserAsync`):
  - `SetEmail_sets_username_and_both_hashes_in_one_operation` (one `users_history` version, equal hashes);
  - `ChangeEmail_through_the_confirmation_flow_writes_one_version_with_equal_hashes`;
  - `SetUserName_is_refused`.
- [ ] Implement the `NorseUserManager` overrides for `SetEmailAsync`, `ChangeEmailAsync` and `SetUserNameAsync` (spec §3.1). Drop `ConfirmEmailChange.razor`'s `SetUserNameAsync` block (`:53`). `ExternalLogin.razor:141-142` sets both through the *store* setters before `CreateAsync`, so it already produces equal values; leave it alone, and cover it with `External_login_registration_writes_equal_hashes`.
- [ ] **Intermediate checkpoint, before any model change:** the full suite is green, explicitly including `An_email_change_through_the_user_manager_writes_a_closed_version_into_users_history` (still exactly 1 version).
- [ ] **Failing model tests:**
  - the `user_credentials` fragment's six columns;
  - both lookup hashes required;
  - the unique index on `NormalizedUserName` on the fragment, unfiltered;
  - the `CHECK (normalized_email = normalized_user_name)`;
  - `NorseUserLogin` non-temporal, and the temporal set is exactly 7 tables;
  - the `claim_value_hash` shadow property (`byte[]`, `IsRequired()`), with provider-specific computed SQL (PostgreSQL `norse_utf8_sha256(claim_value)` stored; SQL Server `CAST(HASHBYTES('SHA2_256', [ClaimValue]) AS binary(32))` persisted) and its unfiltered unique index (A1, spec §4.6 r9);
  - `ClaimValue` required (§14 exemption b);
  - the fragment FK cascading (per G3).
- [ ] Implement the model changes (spec §3, §3.1, §4.6, with the §14 exemption comments). Remove the filtered index and its comment block from `NorseIdentityDbContext`.
- [ ] **Regenerate both providers' `InitialCreate` in this same task** and review the DDL diffs against spec §8. The fixture applies checked-in migrations, so every later task runs against the real target schema.
- [ ] **Failing container tests** (both providers):
  - `Direct_write_of_unequal_hashes_is_rejected_by_CHECK`;
  - `Direct_write_of_null_email_hash_is_rejected`;
  - `Direct_write_of_both_hashes_null_is_rejected`;
  - the catalog shows exactly 7 temporal tables, and `user_credentials`/`user_logins` non-temporal.
- [ ] Checkpoint: the existing suite is green against the new schema with the **old** store, **re-running `An_email_change_through_the_user_manager_writes_a_closed_version_into_users_history` and this task's three manager tests under the `CHECK`**. The old store's blanket writes still route through EF's table splitting, and from this task on the manager writes equal hashes on every path. Stage; stop.

### Task 4.5: Explicit store — skeleton, reads, baseline, create

**Prerequisites:** Task 4.4. **Files:** `Store/ExplicitUserStore.cs` (internal sealed, built alongside the old store), `Store/UserBaseline.cs`.

- [ ] **V1:** decompile RC2 `UserManager<TUser>` and list every `Supports*` probe. Write `Explicit_store_supports_every_capability_the_manager_probes`.
- [ ] **Failing tests** (fixture `UseExplicitStore = true`, both providers):
  - `Create_inserts_both_fragments_in_one_transaction_and_registers_a_baseline`;
  - `FindById_returns_the_whole_entity_including_credentials`;
  - `Every_store_read_registers_a_baseline`;
  - `Baseline_survives_a_scope_change`;
  - `Update_of_an_instance_without_a_baseline_is_refused`;
  - `GetUsersInRole_and_GetUsersForClaim_are_refused`;
  - `DeleteAsync_is_refused_naming_ShredAsync`;
  - `QueryableUserStore_is_not_implemented`.
- [ ] Implement:
  - the interface list (spec §4.1);
  - the full-entity untracked reads, with baseline registration including the passkey as-read state;
  - the in-memory accessors;
  - **`CreateAsync`** (an explicit transaction inserting both fragments plus any staged child intents), so fixture seeding works on the explicit store from this task onward;
  - the refused methods.
  
  `UpdateAsync` and the child and immediate methods throw `NotImplementedException`. No test in this task reaches them, and the old store remains the default registration.
- [ ] Checkpoint green, both store modes. Stage; stop.

### Task 4.6: Explicit store — the flush for users and credentials

**Prerequisites:** Task 4.5; the G1 verdict. **Files:** `Store/StagedIntent.cs`, `Store/FlushExecutor.cs`, `Store/ProviderStatements.cs`.

- [ ] **Failing tests** (explicit store, through the real `NorseUserManager`, `NorsePersonalDataProtector` and `NorseLookupProtector`, both providers). Only paths that need no increment, child rows or manager change:
  - `No_op_update_issues_no_users_statement_and_writes_no_history`;
  - `Password_change_writes_no_users_history`;
  - `Stamp_rotation_writes_no_users_history`;
  - `Phone_change_writes_one_version_and_preserves_other_ciphertext_bytes`;
  - `Stale_concurrency_stamp_returns_ConcurrencyFailure_and_restores_the_instance`;
  - `Concurrent_password_changes_one_succeeds_one_ConcurrencyFailure`;
  - `Credential_CAS_failure_after_users_statement_rolls_back_everything`;
  - `Unique_index_race_rolls_back_and_maps_to_DuplicateUserName`;
  - `Write_through_GetUserAsync_preserves_PasswordHash_EmailConfirmed_LockoutEnabled`;
  - `Repeated_updates_on_one_instance_diff_against_the_last_commit`.
- [ ] Implement:
  - the plaintext diff (§4.2);
  - the credential semantics (§4.4);
  - the flush (§4.3): one explicit transaction under the store's provider-neutral zero-retry `ExecutionStrategy` subclass from Task 4.5 (ruled 2026-10-25; G1: EF's `NonRetryingExecutionStrategy` refuses user transactions on a retry-configured context), the `users` statement (`ExecuteUpdate`) only if a column changed, the credentials statement (**raw SQL** in `ProviderStatements`, ruled), commit, then the new stamp and a refreshed baseline;
  - the definite-failure restore (§4.8).
- [ ] **V2:** `Flush_outside_a_crypto_scope_throws_from_the_protector`. Widen `NorseUserManager`'s scope if any write path test fails for lack of it.
- [ ] Checkpoint green. Stage; stop.

### Task 4.7: Explicit store — immediate operations, in explicit transactions

**Prerequisites:** Task 4.6.

- [ ] **Failing tests** (both providers):
  - `Failed_password_writes_no_users_history`;
  - `Lockout_writes_no_users_history`;
  - `Concurrent_failures_below_threshold_lose_no_increment`;
  - `Concurrent_failures_across_threshold_lock_the_account`;
  - `Concurrent_redemption_of_one_code_exactly_one_succeeds`;
  - `Concurrent_redemption_of_different_codes_neither_resurrects`;
  - `Redemption_retry_exhaustion_throws`.
- [ ] Implement `IncrementAccessFailedCountAsync` and each `RedeemCodeAsync` CAS attempt as **one raw statement inside its own explicit transaction** (the increment via `RETURNING`/`OUTPUT` through an uncomposed `SqlQuery<int>(…).ToListAsync()`: G1 found composition such as `Single()` breaks it) (`BeginTransactionAsync` → statement → `CommitAsync`). That gives both operations an observable commit boundary for Task 4.9's seam. The returned count updates the instance and its baseline.
- [ ] Checkpoint green. Stage; stop.

### Task 4.8: Explicit store — child rows, upserts, passkeys

**Prerequisites:** Task 4.7; the G4 and G6 verdicts.

- [ ] **V7 first:** on the RC2 binaries, confirm that `PasskeyHandler`/`SignInManager.PasskeySignInAsync` read the passkey through the store before `AddOrUpdatePasskeyAsync`, and fail on a failed result. If not, stop and raise it.
- [ ] **Failing tests** (both providers):
  - `ReplaceClaim_persists_under_NoTracking`;
  - `SetToken_on_existing_token_persists_under_NoTracking`;
  - `Passkey_rename_persists_under_NoTracking`;
  - `Claims_are_a_set_identical_add_is_a_no_op`;
  - `Add_login_conflicting_with_another_user_fails_cleanly`;
  - `Concurrent_same_key_token_creation_converges`;
  - `Concurrent_equivalent_passkey_enrollments_both_succeed_one_row`;
  - `Concurrent_conflicting_passkey_enrollments_one_succeeds_one_fails_cleanly`;
  - `Out_of_order_passkey_assertion_fails_and_count_stays_newer`;
  - `Passkey_removal_racing_an_assertion_fails_sign_in_and_does_not_reinsert`;
  - `Child_intent_then_validation_failure_commits_nothing`.
  
  The last one requires Task 4.10's abandon hook, so it is written in 4.10, not here.
- [ ] Implement the §4.6 intents and statements, using G4's proven upsert SQL. On PostgreSQL a unique violation aborts the transaction (G4), so on an enrollment conflict the store **rolls back first**, then reads the existing row to tell an identical replay from a foreign credential.
- [ ] Checkpoint green. Stage; stop.

### Task 4.9: Commit outcome contract

**Prerequisites:** Task 4.8. **Seams (test-only, in the fixture):**
- `CommitFailureInterceptor : DbTransactionInterceptor`, with mode "throw before commit" (`TransactionCommittingAsync`) and mode "throw after commit" (`TransactionCommittedAsync`). It covers the flush and, because Task 4.7 put them in explicit transactions, the increment and the redemption;
- a **firing counter** on the interceptor, so that every test asserts the injection actually happened.

- [ ] **Failing tests** (both providers), for each statement class in the §4.10 policy table:
  - `…_failure_before_commit_is_definite_and_database_unchanged`;
  - `…_ack_loss_resolves_committed_when_a_stamp_marker_exists`;
  - `…_ack_loss_without_marker_returns_IndeterminateOutcome_and_poisons_the_instance`;
  - `…_ack_loss_then_documented_caller_retry_leaves_exactly_the_intended_state`.
  
  Also:
  - `Increment_ack_loss_throws_IndeterminateWriteException_committed_exactly_once` (the count rose by exactly 1; the interceptor fired);
  - `Redemption_ack_loss_throws_and_the_code_is_consumed_exactly_once`;
  - `Passkey_assertion_ack_loss_retry_fails_closed`;
  - `Erasure_indeterminate_commit_then_retry_completes` (deferred from 4.3).
- [ ] Implement the indeterminate path (§4.10): a stamp verification on a fresh connection where a marker exists; otherwise `IndeterminateOutcome` plus poisoning; and `IndeterminateWriteException` from the immediate operations. `IndeterminateWriteException` is created here in `Identity.Web.Server` and moves to `Identity.Outcomes` in Task 4.12.
- [ ] Checkpoint green. Stage; stop.

### Task 4.10: Manager — the abandon hook and email concurrency on the explicit store

**Prerequisites:** Task 4.9. The email-is-username overrides already exist (Task 4.4); this task proves them on the explicit store and adds the abandon hook.

- [ ] Task 4.4's three manager tests run under `UseExplicitStore = true` too (both store modes, both providers).
- [ ] **Failing tests** (explicit store, both providers):
  - `Concurrent_change_to_same_email_one_succeeds_one_DuplicateEmail`;
  - `Validation_failure_abandons_staged_intents_and_restores_the_instance`;
  - `Child_intent_then_validation_failure_commits_nothing` (deferred from 4.8).
- [ ] Implement the abandon hook in the `NorseUserManager.UpdateUserAsync` override.
- [ ] **V5:** audit `UserManager`'s mutating methods on RC2 for staging followed by an early failure return; add an abandon override or a written ruling for each. **V6:** drive every scaffold-reachable write path from store-obtained instances and assert that none hits the no-baseline refusal.
- [ ] Checkpoint green. Stage; stop.

### Task 4.11: The swap

**Prerequisites:** Task 4.10.

- [ ] Delete the old `NorseUserStore`. Rename `Store.ExplicitUserStore` to `NorseUserStore` (public surface and registration unchanged for consumers), and remove the fixture's `UseExplicitStore` option. Every explicit-store test now runs by default.
- [ ] **Flip** `A_failed_password_attempt_currently_versions_users`, renaming it to the target behavior (it is now the same fact as Task 4.7's `Failed_password_writes_no_users_history`; merge them rather than keep a duplicate).
- [ ] Checkpoint: the full `dotnet test Himinbjorg.slnx` is green on both providers. Stage; stop.

### Task 4.12: The Outcome surface and the `Identity.Outcomes` project

**Prerequisites:** Task 4.11.

- [ ] **Create `src/Identity.Outcomes/`** (`Microsoft.NET.Sdk`, `slnx` entry) and **`tests/Identity.Outcomes.Tests/`**. References: `Identity.EntityFramework` (which brings Identity Stores/Core and OpenIddict Core transitively); Asgard `Abstractions.Contracts` for `Outcome`/`ErrorCategory` and `Abstractions.Backend` for `KeyDestroyedException`, as NorseRefs, unless they already flow transitively (transitive-first). `Identity.Web.Server` and `Identity.Migrations` gain a `ProjectReference` to it.
- [ ] **Move** `IndeterminateWriteException` into `Identity.Outcomes`.
- [ ] **Failing tests in `Identity.Outcomes.Tests`:**
  - `Every_IdentityErrorDescriber_code_has_an_explicit_mapping` (reflection over the pinned version);
  - `Unmapped_code_maps_to_Fault`;
  - `IndeterminateOutcome_maps_to_Indeterminate`;
  - `KeyDestroyedException_is_classified_as_Erased`;
  - `OpenIddict_concurrency_maps_to_Conflict`;
  - one test per `.Outcome` view method, against a substituted manager.
- [ ] Implement `IdentityOutcomeMap` (including `IsErased(Exception)`, the shared classifier) and the `.Outcome` views over `UserManager<NorseUser>` and the three OpenIddict managers.
- [ ] **In `Identity.Web.Server`:**
  - the `SignInManager<NorseUser>` `.Outcome` view, with tests (`SignInResult_states_map_to_LockedOut_NotAllowed_InvalidCredentials`);
  - **framework-boundary adapters kept, not moved:** `NorseSignInManager.ValidateSecurityStampAsync` keeps its fold to `null` and classifies through `IdentityOutcomeMap.IsErased`; `IdentityRevalidatingAuthenticationStateProvider` does the same, folding to "invalid";
  - **failing framework-path tests** (the key-destroyed window, never touching `.Outcome`):
    - `Real_SecurityStampValidator_rejects_the_cookie_without_a_500` (`SecurityStampValidator<NorseUser>.ValidateAsync` with a real `CookieValidatePrincipalContext` over a test `HttpContext`; asserts the principal is rejected);
    - `Real_revalidating_provider_reports_invalid` (calls `ValidateAuthenticationStateAsync` through the provider's own protected entry, via its existing test door).
- [ ] **Migrate** every handler and `OpenIddictExchangeEndpoint` onto the views (in `Identity.Web.Server`), and `MachineClientSeedContributor` onto the OpenIddict view (from `Identity.Outcomes`). Delete each hand-rolled translation. Existing handler tests stay green; `LoginHandler`'s reference-identity `Failed` collapse holds.
- [ ] **Enforcement and graph tests:**
  - `No_handler_service_or_endpoint_calls_raw_Identity_or_OpenIddict_manager_methods`: an IL scan with `System.Reflection.Metadata` over the `Identity.Web.Server` and `Identity.Migrations` assemblies, excluding the `Outcomes` views, the framework-boundary adapters and `Components/`;
  - `Migration_assembly_does_not_reference_Identity_Web_Server_or_AspNetCore`: asserts `Norse.Identity.Migrations`' referenced assemblies exclude `Norse.Identity.Web.Server` and `Microsoft.AspNetCore.*`;
  - plus `dotnet build src/Identity.Migrations` alone succeeds.
- [ ] Checkpoint green. Stage; stop.

### Task 4.13: Final catalog proof

**Prerequisites:** Task 4.12.

- [ ] If anything mapped has changed since Task 4.4 (it shouldn't have), regenerate `InitialCreate` on both providers and review.
- [ ] Extend `NorseIdentityTemporalApparatusContainerTests` (PostgreSQL) and add its SQL Server twin: the 7 temporal tables, the non-temporal set, the `CHECK`, `NOT NULL`, the indexes, and the claim hash.
- [ ] `History_after_erasure_holds_only_ciphertext_and_non_PII_flags` (a column scan of `users_history`/`UsersHistory`).
- [ ] Full suite green on both providers. Stage; stop.

### Task 4.14: End-to-end through Bifröst (local only)

**Prerequisites:** Task 4.13.

- [ ] `dotnet run --project src/Orchestration.AppHost`. Then, recording the `users_history` row count for the account before and after each step, assert these deltas:

  | Operation | `users_history` delta | `user_credentials` effect |
  |---|---|---|
  | register | 0 (an insert opens a version; it doesn't close one) | row created |
  | wrong password | **0** | `access_failed_count` +1 |
  | password change | **0** | hash and stamp rotated |
  | email change (confirmation flow) | **+1** | hashes rewritten, equal |
  | enable 2FA (`EnableAuthenticator.razor` writes `TwoFactorEnabled`) | **+1** | authenticator key token upserted |
  | redeem a recovery code | **0** | token value shrinks by one code |
  | no-op save | **0** | none |
- [x] Record the observed table in the task summary. Nothing is staged in Bifröst here.

**Observed (2026-10-25, Postgres, against volumes wiped beforehand; accepted by Buvy as passing):** `InitialCreate` built `norse_identity` from scratch in about 21 s, and the catalog matches §3.1. The steps driven:

| Step | Delta | Result |
|---|---|---|
| register | 0 | pass |
| wrong password | 0, `access_failed_count` +1 through the raw increment | pass |
| password change | 0, the credentials CAS rotated the hash and stamp | pass |
| enable 2FA | +1, only the changed column rewritten | pass |
| no-op save | 0 | pass, but the stock page skips the save, so the store's empty diff is proven by the container suites |

Two steps were **not driven**, because the server-rendered UI blocked them:
- **email change:** `IdentityNoOpEmailSender` discards the confirmation link;
- **recovery-code redemption:** a password login for a 2FA user faults with "OnStarting cannot be set because the response has already started" in `SignInManager.IsTwoFactorClientRememberedAsync`, inside an interactive-server circuit.

Both are covered by the container suites on both providers.

UI findings handed to the Heimdall gRPC port (#48 onward), not fixed here:
1. the 2FA login fault above;
2. the no-op sender has no development link sink;
3. the WebAssembly client fails to boot on Chrome 156 ("module uses a mix of legacy and new exception handling instructions", .NET 11 RC2); this is a platform issue, worth its own ticket. As a knock-on, statically rendered Heimdall Fluent inputs carry no `name`, so the forms post empty;
4. logout from `Manage/EnableAuthenticator` lands on `/Account/InvalidUser`.

### Task 4.15: Docs and ship

**Prerequisites:** Task 4.14.

- [ ] **Himinbjörg CLAUDE.md/README.md pair:**
  - the realm project table (add `Identity.Outcomes`);
  - replace "Accepted pre-split rough edge";
  - the temporal (7) and non-temporal lists;
  - "Crypto-shred, not tombstone" → hard delete, key first;
  - the store, Outcome-surface and framework-adapter facts;
  - the `InitialCreate` memorialization sentence: the squash cycle **continues** past this train, and V1 is memorialized only once the Heimdall gRPC conversion of the server components is complete, proven end to end with MAUI-renderable components, and Buvy is happy with the schema (ruled 2026-10-25; it replaces the stale preview-7 trigger). Each squash means wiping the local `norse-pg-primary`/replica volumes before the next Bifröst standup.
- [ ] Close #47's checklist in the PR description against spec §10.
- [ ] Stage; stop. **Ship gate (Himinbjörg):** PR merged, CI green, tagged, published.

---

## Phase 5 — Bifröst

- [x] Pointer bumps for Asgard, Midgard, Urðarbrunnr and Himinbjörg: not a task. Ginnungagap's `update-bifrost.yml` bumps each pointer on GitHub as each realm releases.
- [x] The Bifröst CLAUDE.md/README.md pair: a state-of-the-union entry for this train (staged 2026-10-25; commit with the Himinbjörg ship).
- [x] Midgard's `SqlServerContainerFixture.IsSupportedArchitecture` widened to the Windows-on-ARM-only skip (done 2026-10-25, ahead of this phase). The Task 1.3 inventory left no other-realm `ErrorCategory` follow-ons.
- [ ] Stage; stop.

## Self-review

- **Spec coverage:** every spec §10 checkbox maps to a named test:
  - History → 4.6, 4.7, 4.10, 4.14;
  - Atomicity → 4.4, 4.6, 4.9, 4.10;
  - Concurrency → 4.6–4.8, 4.10;
  - Correctness → 4.5, 4.6, 4.8;
  - Erasure → 4.3, 4.5, 4.9, 4.12, 4.13;
  - Schema → 4.4, 4.13;
  - Ship → 1.3, 2.3, 3.4, 4.15, Phase 5.
  
  Each of V1–V9 has an owner: V1 4.5, V2 4.6, V3 2.3 and 4.3, V4 4.4, V5/V6 4.10, V7 4.8, V8 2.1, V9 1.3 and 4.1. Each of G1–G6 has an owner in Phase 0.
- **Executable order:** each Phase 4 task lists its prerequisites; no test depends on a later task; the old store carries every checkpoint until the swap.
- **House-law conflicts:** only the two §14 exemptions, named where they're used (Task 4.4).

## Plan review record

| # | Objection | Answer |
|---|---|---|
| PL1 | `FileStream.Lock` is unsupported on macOS | `flock` through `[LibraryImport]` on macOS and Linux, `LockFileEx` on Windows; in-process `SemaphoreSlim` kept for ordering only; proven on all three operating systems in Task 2.1; an unsupported primitive is a re-plan trigger; spec §7.5 amended (r7) |
| PL2 | SQL Server 2022 can't host the schema | `mssql/server:2025-latest` everywhere; every fixture and the PoC assert `ProductMajorVersion >= 17` and apply the full identity migration, including the native `json`; spec §6.2 amended (r7) |
| PL3 | Phase 4 dependencies ran backwards | Reordered into slices with stated prerequisites: adoption (with the park replaced in the same task, migrations regenerated) → fixtures → erasure on the current model → model plus migrations together → the explicit store built alongside the old one (create first) → flush → immediate operations → child rows → commit outcome → manager → swap → Outcome surface → proofs |
| PL4 | Moving the destroyed-key catch broke framework revalidation | The framework-boundary adapters stay in `NorseSignInManager` and the revalidating provider, classifying through the shared `IsErased`; real-`SecurityStampValidator` and revalidating-provider tests (Task 4.12); spec §4.12 amended (r7) |
| PL5 | Outcome views were outside the seeder's dependency graph | A new non-web `Identity.Outcomes` project holds the map, the `UserManager` and OpenIddict views, and `IndeterminateWriteException`; the `SignInManager` view stays in web; graph shown above; assembly-reference and standalone-build checks (Task 4.12) |
| PL6 | The relational annotation seam was omitted | All three `ReplaceService` seams, mirroring PostgreSQL; every DDL test generated through the real `IMigrationsModelDiffer`, enable and disable included (Tasks 3.2–3.3) |
| PL7 | The interceptor misses implicit autocommits | The increment and each redemption attempt run in explicit transactions (Task 4.7), so `TransactionCommittedAsync` observes them; the interceptor carries a firing counter; tests assert exactly-once commit (Task 4.9) |
| PL8 | The end-to-end history assertion contradicted the split | A per-operation delta table: 0 for credential-only operations, +1 for the email change, +1 for enabling 2FA (Task 4.14) |
| PL3 (r3) | The equality `CHECK` preceded the manager behavior it requires; Task 4.1's green checkpoint contradicted the three known failures | The email-is-username overrides move into Task 4.4, ahead of the `CHECK` in the same slice, against the old store, with an intermediate checkpoint that runs the existing `SetEmailAsync` versioning test; Task 4.10 keeps only the abandon hook and explicit-store concurrency. Task 4.1 carries exactly the three named failures red into Task 4.2 |
| Receipt (r3) | Task 2.3 implied an unpublished candidate receipt was recoverable | Authority begins at durable publication of the pending file: before it, tests assert the key is intact and a retry completes destruction (no comparison with the lost candidate); after it, every caller and retry, across a fresh store instance, returns the published receipt. Task 1.2's contract text and spec §7.5 (r8) say "one authoritative receipt" |
