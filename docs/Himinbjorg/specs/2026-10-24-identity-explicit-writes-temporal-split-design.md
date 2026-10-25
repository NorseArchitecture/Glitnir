# Himinbjörg: Explicit-Write Identity Store, Temporal Split, and Hard-Delete Erasure — Design

**Realms touched:** Himinbjörg (store, entity shape, erasure, migrations), Urðarbrunnr (SQL Server split-temporal realization and lifecycle), Midgard (`DevelopmentSubjectKeyStore` concurrent-destruction protocol, §7.5), Asgard (`ISubjectKeyStore.DestroyAsync` contract documentation, §7.5; `ErrorCategory.Indeterminate`, §4.12), Glitnir (this verdict, a mechanics proof of concept, two spec amendments).
**Closes:** [Himinbjorg#47](https://github.com/NorseArchitecture/Himinbjorg/issues/47).
**Branch:** `feature/access-count-breakout` (Himinbjörg's one open fork — follow-on commits land there).
**Absorbs:** `docs/himinbjorg-lockout-split-temporal-redesign-brainstorm.md` (2026-08-13 notes — deleted with this spec, not stubbed).
**Amends:** `docs/Platform/specs/2026-08-03-pii-primitives-identity-erasure-seam-design.md` §4.2, §4.3; `docs/Platform/specs/2026-08-04-temporal-tables-persistence-chassis-design.md` §1 ruling 5, §4, §5.
**Revision:** r7, 2026-10-24 — answers four remand passes (R1–R12), recorded in §13. The per-instance baseline (§4.2) was explicitly approved at the r4 review. r6 adds Buvy's rulings: the Outcome surface (§4.12, ruling 12) and the brownfield house-rules exemptions (§14). r7 folds in the plan review: the macOS-capable lock primitive (§7.5), the SQL Server 2025 floor (§6.2), and the Outcome placement and framework-boundary adapters (§4.12). r9 folds in the Phase 0 verdict (approved 2026-10-24): G1's raw-SQL ruling (§12), the claim-hash expressions (§4.6) and the confirmed SQL Server lifecycle ordering (§6.2). r8 restates the §7.5 receipt contract as one *authoritative* receipt: authority begins at durable publication of the pending file, and an unpublished in-memory candidate carries none.

## 1. Context

Splitting lockout churn off `users` was supposed to stop a wrong password from minting a `users_history` row. On the branch it doesn't: `TemporalIdentityVersioningTests.A_failed_password_attempt_currently_versions_users` still passes with the old expectation, and SQL Server had to give up temporality on `users` entirely (`TemporalParkedOnSqlServer()`).

The 2026-08-13 brainstorm traced the noise to stock `UserStore<TUser>.UpdateAsync` (`Attach` → regenerate `ConcurrencyStamp` → `Context.Update`) and proposed skipping the forced `Update` "because the entity is almost always already tracked." **That premise is false on this platform.** `ApplyNorseTrackingBehavior()` (Urðarbrunnr, `NorseDbContextOptionsExtensions.cs`) forces `QueryTrackingBehavior.NoTracking` as platform law, and Himinbjörg inherits it through `AddNorseContext`. Every user `UserManager` loads is detached, so every `UpdateAsync` blanket-writes every column of both fragments. Verified 2026-10-24:

1. **History noise is structural.** Nothing is ever diffed; every update rewrites `users`, `ConcurrencyStamp` always differs, and the ciphertext columns are re-encrypted under a fresh nonce (`NorsePersonalDataProtector.Protect`) so even their bytes always differ.
2. **Silent lost writes in stock `UserStore`.** The RC2 `Microsoft.AspNetCore.Identity.EntityFrameworkCore.dll` contains zero `AsTracking()` references. Load-mutate-save methods lose the mutation under NoTracking: `ReplaceClaimAsync`, `SetTokenAsync` on an existing token (authenticator key, recovery codes), `AddOrUpdatePasskeyAsync` on an existing passkey.
3. **A latent data-loss bug.** `NorseUserStore.FindByIdAsync` returns a seven-column *projection*. `UserManager.GetUserAsync(principal)` routes through it, and 13 scaffold pages use it. When any of them writes, the blanket write persists the projection's C# defaults: `PasswordHash = null`, `EmailConfirmed = false`, `LockoutEnabled = false`, `PhoneNumber = null`, `TwoFactorEnabled = false`. `ChangePasswordAsync` can never succeed.

OpenIddict is unaffected: its EF stores call `AsTracking()` explicitly.

**Outcome sought:** a store that owns every write explicitly, so history records only genuine identity-content changes on both providers, every manager operation is atomic, credential state is concurrency-safe, and erasure leaves nothing linkable behind.

## 2. Rulings (brainstorm 2026-10-24)

1. **Tracking stays off.** EF's change tracker is not reintroduced. The store keeps exactly one piece of per-instance state — the as-read baseline (§4.2) — explicitly, narrowly, and for the reasons stated there.
2. **`NorseUserStore` implements the `IUser*Store` interfaces directly.** No `UserStore<…>` base class, no inherited code that assumes tracking.
3. **Writes are explicit**: every manager operation stages typed write intents and flushes them as one transaction (§4.3); the few operations whose return value drives a security decision are immediate atomic statements (§4.5).
4. **Columns split by meaning** — identity record on temporal `users`, credential/lookup/rate-limiter state on non-temporal `user_credentials` (§3).
5. **New history law:** *anything computed under a platform key never enters history.* Extends the 2026-08-05 law *secret material never archives to history.*
6. **`user_logins` goes non-temporal** (amends the 2026-08-05 #47 Q4 ruling). Its `provider_key` is a plaintext third-party subject id forming the primary key; it cannot be encrypted or blind-indexed out of history. Link/unlink history moves to receipts (#55), which never carry the raw provider key. Entity-splitting `user_logins` was rejected — a second split entity for four columns.
7. **`user_claims` stays temporal**, under the rule that claim values never carry PII.
8. **SQL Server keeps EF-native temporal by default** so every other realm keeps `TemporalAsOf`/`TemporalAll`. Only a split entity opts into chassis-owned DDL, explicitly (§6).
9. **Erasure is a hard delete, key first** (§7). Supersedes darken-in-place. It is the **only** path that removes a user row (§4.7).
10. **Prove the EF mechanics first** in `Glitnir/poc/identity-explicit-writes` (§9); prove the behavioral contract in Himinbjörg's own container suites against the real protector and manager (§10).
11. **Upstream [dotnet/efcore#26457](https://github.com/dotnet/efcore/issues/26457) is still pursued** for .NET 12, but nothing here depends on it.
12. **Norse callers see `Outcome<T>`, never `IdentityResult` or OpenIddict exceptions** (ruled 2026-10-24). This is how the whole platform behaves, and Identity is not an exception. `UserManager`/`SignInManager` are **not** replaced (§4.12).

## 3. Entity shape

`NorseUser` stays one entity (drop-in `IdentityUser<Guid>` compatibility is why `SplitToTable` beats a separate entity — ruled 2026-08-13, unchanged). The fragment is renamed `UserLockout` → **`user_credentials`**.

| `users` — temporal, the identity record | `user_credentials` — non-temporal |
|---|---|
| `Id` | `Id` (shared PK, FK → `users` `ON DELETE CASCADE`) |
| `UserName` (ciphertext) | `PasswordHash` |
| `Email` (ciphertext), `EmailConfirmed` | `SecurityStamp` |
| `PhoneNumber` (ciphertext), `PhoneNumberConfirmed` | `AccessFailedCount`, `LockoutEnd` |
| `TwoFactorEnabled` | `NormalizedEmail` (HMAC blind index) |
| `LockoutEnabled` (admin policy — the 2026-08-04 ruling stands) | `NormalizedUserName` (HMAC blind index) — **required, plain unique index, both providers** |
| `ConcurrencyStamp` — versions the identity record only | |

Consequences, accepted:

- A password change, stamp rotation, failed attempt, or lockout leaves **no trace** in `users_history`. The record reads strictly "identity content changed." Credential events are audited through receipts (#55), not row history.
- An email change still versions `users` — and that version is crypto-shreddable.
- After erasure `user_credentials` has no history, so the password hash, stamp, and lookup hashes are genuinely **gone**. This retires PII spec §4.2's "honest residue" addendum.
- `NormalizedUserName` returns to `IsRequired()`. The SQL Server filtered index is deleted; the unique index lives on `user_credentials`.

### 3.1 Email is the username — enforced, and changed atomically (answers R10)

The *email is the username* law (PII spec §4.1) gains database enforcement and a single write path:

- **Both lookup hashes are `NOT NULL`** (`NormalizedEmail` joins `NormalizedUserName` as `IsRequired()`), **and `CHECK (normalized_email = normalized_user_name)`** on `user_credentials` (database self-defense, house rules §2.2). The `NOT NULL` is load-bearing: a `CHECK` whose expression evaluates to unknown passes on both providers, so equality alone would admit `normalized_email = NULL`. Together they make the unique index on `normalized_user_name` *be* email uniqueness. No second unique index is needed, which resolves V4.
- **`UserName` always equals `Email`** in plaintext. This can't be a database constraint (the ciphertexts differ by nonce), so it is enforced in the manager: `NorseUserManager` overrides `SetEmailAsync` and `ChangeEmailAsync` to set `Email`, `UserName`, both normalized hashes and the security stamp **in one operation**. That means one flush, one transaction, and one `users` history version.
- **`SetUserNameAsync` is refused** with `NotSupportedException` (fail loud — the username is derived, never set on its own). `ConfirmEmailChange.razor` drops its second call (`SetUserNameAsync`, line 53). The window in which the email and username could diverge no longer exists.
- **New users** follow the same rule. `RegisterHandler` already sets both from one value. `ExternalLogin.razor`'s `SetUserNameAsync`/`SetEmailAsync` calls on the store, made before `CreateAsync`, are plain property assignment on a new instance and stay legal. The check constraint backstops all of these paths.
- **An email change therefore re-encrypts both `Email` and `UserName`** (both values changed). The history proof asserts that the *unchanged* ciphertext — `PhoneNumber` — keeps identical bytes, not `UserName`.
- **A duplicate-email race** — two accounts changing to the same address at once — is decided by the unique index. The loser rolls back and gets `DuplicateEmail`.

Temporal set after this spec: `users`, `roles`, `user_claims`, `role_claims`, `user_roles`, `applications`, `scopes` (7). Non-temporal: `user_credentials`, `user_logins`, `user_tokens`, `user_passkeys`, `authorizations`, `tokens`, `subject_keys`. `NorseIdentityTemporalModelTests` pins both sides and is amended in the same change.

## 4. The store — `NorseUserStore`

### 4.1 Interfaces

`NorseUserStore` implements, directly: `IUserStore<NorseUser>`, `IUserPasswordStore`, `IUserSecurityStampStore`, `IUserEmailStore`, `IUserPhoneNumberStore`, `IUserLockoutStore`, `IUserTwoFactorStore`, `IUserAuthenticatorKeyStore`, `IUserTwoFactorRecoveryCodeStore`, `IUserAuthenticationTokenStore`, `IUserLoginStore`, `IUserClaimStore`, `IUserRoleStore`, `IUserPasskeyStore`, and `IProtectedUserStore<NorseUser>` (mandatory when `Stores.ProtectPersonalData = true`). `IQueryableUserStore` is **not** implemented: no caller needs it, and an `IQueryable<NorseUser>` door invites list-shaped decrypted reads (PII spec §4.1 subject-singular law).

Lookup protection stays where it is: `UserManager` applies `ILookupProtector` before `FindBy*` and before writing normalized values. The store receives already-protected values.

### 4.2 Reads and the as-read baseline (answers R1, R2)

Every `FindBy*` materializes the **whole** entity (both fragments, untracked). The projection override is deleted.

**The baseline.** Ciphertext can't detect change (fresh nonce per encryption), and the manager mutates the instance before `UpdateAsync`, destroying the original values a guarded write needs. So the store records, for every instance it hands out, an **as-read baseline**: the plaintext value of every mapped scalar column as it came out of the database (after the EF converter decrypted it), plus the `ConcurrencyStamp` it was read with.

- **Storage:** a store-owned `ConditionalWeakTable<NorseUser, UserBaseline>`, static, so the baseline lives exactly as long as the instance does and follows the instance across DI scopes. It is not an identity map: no lookup by key, no graph, no navigation, no automatic change detection, no resolution of a second read of the same row to the same instance.
- **Populated by:** every store read that returns a `NorseUser` — `FindByIdAsync`, `FindByNameAsync`, `FindByEmailAsync`, `FindByLoginAsync`, `FindByPasskeyIdAsync`. All of these return exactly one subject. A successful `CreateAsync` also populates it (baseline = the values just inserted). The baseline also records the **as-read passkey state** — credential id and sign count — for every passkey the store returns for that user (`GetPasskeysAsync`, `FindPasskeyAsync`). The guarded assertion update in §4.6 needs it.
- **Refreshed:** after every successful flush, the baseline becomes the instance's current values (and the new stamp). Repeated updates on one instance therefore each diff against the last committed state.
- **Never refreshed by a failure** (§4.8).
- **An instance with no baseline** — constructed by hand, deserialized, or produced by code other than this store — is **refused**: `UpdateAsync` throws `InvalidOperationException` naming the cause. There is no silent fallback to a blanket write. (A newly constructed user is only legal as an argument to `CreateAsync`.)

**Change detection is plaintext, in memory.** A column is changed iff its current value differs from its baseline value (ordinal for strings, value equality otherwise). Unchanged ciphertext columns never appear in a `SET` clause, so their stored bytes are preserved and the temporal apparatus sees no change. If no `users` column changed, **no statement touches `users`** — on either provider.

### 4.3 The write model: stage, then flush in one transaction (answers R4)

`UserManager`'s shape is the same for every mutating operation: call `Set*`/`Add*`/`Remove*`/`Replace*` on the store, then `UpdateUserAsync` → validate → `Store.UpdateAsync`. The store mirrors that shape explicitly:

- **Mutating store methods stage typed intents** against the instance (held alongside the baseline). They do not touch the database. Scalar setters just assign the property; the diff in §4.2 picks them up. Child-row methods (claims, logins, roles, tokens, passkeys) append an intent: `AddClaim(…)`, `ReplaceClaim(old, new)`, `RemoveLogin(provider, key)`, `UpsertToken(provider, name, value)`, `UpsertPasskey(…)`, …
- **`UpdateAsync` flushes**, inside one database transaction (under the context's execution strategy):
  1. the `users` statement, if any `users` column changed: `UPDATE users SET <changed columns>, concurrency_stamp = @new WHERE id = @id AND concurrency_stamp = @baselineStamp`;
  2. the `user_credentials` statement, if any credential column changed, with per-column semantics (§4.4);
  3. each staged child intent, in staging order, one statement each (§4.6).

  Commit, then set the instance's new stamp and refresh its baseline. A failed guard, affected-row mismatch, constraint violation or connection failure **before commit is attempted** rolls back everything, so a second-stage failure leaves no partial data and no committed history row. A failure **during or after commit** is a different case, with its own contract (§4.10).
- **No automatic retry, ever.** The flush runs under a non-retrying execution strategy, regardless of how the context's strategy is configured. G1 found that EF's `NonRetryingExecutionStrategy` still refuses a user-initiated transaction on a context configured with `EnableRetryOnFailure`; the working form is the provider's own retrying strategy with `maxRetryCount: 0` (`NpgsqlRetryingExecutionStrategy`/`SqlServerRetryingExecutionStrategy`). Blind replay is exactly what duplicates child inserts and increments (§4.10).
- **`CreateAsync`** inserts both fragments plus any staged child intents in one transaction.
- **Zero-row `users` update** under a changed diff means the stamp moved: `IdentityResult.Failed(ConcurrencyFailure)` after rollback. (No probe is needed — a no-op never issues the statement.)
- A unique-index violation on `normalized_user_name` (a race past `UserValidator`) rolls back and maps to `DuplicateUserName`.

### 4.4 Credential column semantics (answers R2, R3)

| Column | Write semantics | Why |
|---|---|---|
| `PasswordHash`, `SecurityStamp` | Compare-and-swap against the baseline, together: `… WHERE password_hash IS NOT DISTINCT FROM @baselineHash AND security_stamp = @baselineStamp`. A mismatch → `ConcurrencyFailure`. | Restores stock Identity's protection for concurrent credential changes, which the identity stamp no longer provides — the baseline is the original value source R2 asked for. |
| `NormalizedEmail`, `NormalizedUserName` | Compare-and-swap against the baseline. | The same guard covers lookup rewrites that happen without a `users` change (for example a re-normalization). |
| `AccessFailedCount` — increment | **Immediate atomic statement** (§4.5), never staged. | The manager's lockout decision uses the returned count. |
| `AccessFailedCount` — reset | Staged, unconditional `= 0`. | A reset follows either a proven password or a lockout; clobbering a concurrent increment is correct in both cases. |
| `LockoutEnd` | Staged, unconditional set. | Latest decision wins. Concurrent lockouts write the same end time to within the request skew. |

### 4.5 Immediate atomic operations (answers R3, R5)

Two store methods return values that the manager acts on before `UpdateAsync`, so they cannot be staged. Each is one self-contained, individually committed statement (or a bounded CAS loop), and is correct regardless of what happens afterwards.

- **`IncrementAccessFailedCountAsync`** — `UPDATE user_credentials SET access_failed_count = access_failed_count + 1 WHERE id = @id RETURNING access_failed_count` (SQL Server: `OUTPUT inserted.access_failed_count`). The instance's property and baseline take the returned value. Concurrent failures each get a distinct, monotonically increasing count, so exactly the request that crosses `MaxFailedAccessAttempts` (and any after it) takes the lockout branch. The increment stays committed even if the rest of the operation fails — a failed attempt really happened.
- **`RedeemCodeAsync`** — atomic consumption, distinct from generic replacement. Read the recovery-code token value; if the code is absent, return `false`; otherwise `UPDATE user_tokens SET value = @without WHERE user_id = @id AND login_provider = @p AND name = @n AND value = @read`. On zero rows, re-read and retry, at most 5 times; exhaustion throws. Two concurrent redemptions of the same code: exactly one CAS succeeds, and the other re-reads, finds the code gone and returns `false`. Concurrent redemptions of different codes: the loser re-reads the winner's result, so neither code is resurrected. Committed immediately: a consumed code stays consumed.
- **Neither operation is ever retried after an indeterminate commit** (§4.10). An indeterminate increment or redemption throws `IndeterminateWriteException`, and sign-in fails closed: the login path answers `LoginHandler`'s single static `Failed`. The one possible cost is that the attempt or the code may have counted, which fails safe in both cases.
- **`ReplaceCodesAsync`** (new code set) is a staged upsert. A replacement racing a redemption ends with the new set, which is the intended result.
- **Reads that drive decisions** (`CountCodesAsync`, `GetAccessFailedCountAsync`, `GetTokenAsync`, `GetAuthenticatorKeyAsync`) read the database, not the instance.

### 4.6 Child-row statements and upserts (answers R8)

The rule is **one statement per staged intent**. A provider-native upsert counts as one statement.

| Intent | Statement |
|---|---|
| add claim | Insert-if-absent against a unique key on `(user_id, claim_type, claim_value_hash)`: PostgreSQL `INSERT … ON CONFLICT DO NOTHING`, SQL Server `MERGE … WITH (HOLDLOCK)`. `claim_value` is unbounded (`text`/`nvarchar(max)`) and can't be indexed directly, so `claim_value_hash` is a provider-computed, stored SHA-256 of `claim_value` (a shadow property in the model, never on the CLR type; `byte[]`, `IsRequired()`). **Expressions (G6):** PostgreSQL `GENERATED ALWAYS AS (norse_utf8_sha256(claim_value)) STORED`; SQL Server `CAST(HASHBYTES('SHA2_256', [ClaimValue]) AS binary(32)) PERSISTED`. `sha256(convert_to(…))` can't be used inline on PostgreSQL: `convert_to` is `STABLE`, and the engine refuses it in a generation expression. **`norse_utf8_sha256(text) RETURNS bytea LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE RETURN pg_catalog.sha256(pg_catalog.convert_to($1, 'UTF8'))` is chassis-owned** (ruled 2026-10-24): Urðarbrunnr's Npgsql migrations generator emits it idempotently ahead of any table whose computed column names it, so regenerating `InitialCreate` can't lose it. Bare `HASHBYTES` types the column `varbinary(8000)`, past the index-key limit, hence the cast. The shadow property must be required: left nullable, EF filters the unique index, and SQL Server refuses a filtered index on a computed column. `ClaimValue` itself is required too (§14 exemption b), since the providers treat `NULL` in a unique index differently. The hash is provider-local (UTF-8 vs UTF-16LE input) and is never compared across providers. Claim identity is exact-value, whatever the collation (`Admin` and `admin` are two claims). **Claims become a set:** an identical claim is never stored twice. That is a deliberate tightening of stock Identity, which permits duplicate rows that have no meaning. PoC gate G6. |
| add role | `INSERT … ON CONFLICT DO NOTHING` / `MERGE` on the primary key `(user_id, role_id)` |
| add login | `INSERT` on the primary key `(login_provider, provider_key)`. A conflict with a row owned by the **same** user is success; one owned by another user rolls back and fails. |
| replace claim | `UPDATE … WHERE user_id AND claim_type AND claim_value_hash = hash(@old)`. Zero rows with the new claim already present is success (a replay); zero rows otherwise rolls back with `ConcurrencyFailure`. |
| remove claim / login / role / token / passkey | `DELETE` by key |
| upsert token (authenticator key, recovery-code set, external tokens) | PostgreSQL `INSERT … ON CONFLICT (user_id, login_provider, name) DO UPDATE SET value = EXCLUDED.value`; SQL Server `MERGE … WITH (HOLDLOCK)` on the same key. Concurrent creators of the same key both succeed, and the last committed value wins. |
| passkey **enrollment** | `INSERT` only, never an upsert. A conflict on `credential_id` with an identical row (same user, same public key) is success, because that is a replay. Any other conflict rolls back and fails. |
| passkey **assertion update** (sign count, flags, rename) | `UPDATE user_passkeys SET data = @new WHERE credential_id = @cid AND user_id = @id AND <sign count> = @baselineCount`, never an insert. |

**Passkeys: enrollment and assertion are distinct intents (answers R8).** `IUserPasskeyStore.AddOrUpdatePasskeyAsync` is one method serving two purposes, and an unconditional upsert lets a delayed assertion overwrite a newer counter or re-insert a removed credential. The store separates them by the baseline:
- **Assertion update:** the passkey's credential id is in the instance's as-read passkey state (§4.2), meaning the caller read it through this store, as `PasskeyHandler` does before validating an assertion. The update is guarded by the as-read sign count. A delayed assertion that read count 10 and proposes 11, after another wrote 12, matches zero rows. A passkey removed in the meantime also matches zero rows. **Zero rows rolls back and fails the operation**, so `SignInManager`'s passkey sign-in fails closed. A removed credential is **never** re-inserted. (Authenticators that always report 0 pass the guard 0 = 0; counter-less authenticators are a WebAuthn-sanctioned case.)
- **Enrollment:** any credential id not in the as-read state is an insert.

The removal's security-stamp rotation is not relied on; the per-row guard is the barrier.

The upsert SQL is issued through the chassis's provider seam (`NorseDbContextOptionsExtensions` exposes the bound provider) so Himinbjörg remains provider-agnostic in shape. Concurrent same-key creation tests are part of the suites in §10.

### 4.7 Deletion has one door (answers R7)

`IUserStore.DeleteAsync` **throws** `NotSupportedException`, with a message naming `ErasureService.ShredAsync`. This means `UserManager.DeleteAsync` cannot remove a user while leaving the subject key alive or OpenIddict subject rows behind. `ErasureService` performs the physical delete directly through `NorseIdentityDbContext` (§7), not through the store. `DeletePersonalData.razor` is re-pointed at the erasure ceremony. A regression test asserts that `UserManager.DeleteAsync` fails and leaves the row, the key, and the OpenIddict rows intact.

### 4.8 Failure handling

There is one rule for a **definite** failure: **a failed operation leaves the instance exactly as read**. A definite failure is a guard, constraint or connection failure before commit, or `NorseUserManager.UpdateUserAsync` returning failure (validation, which happens before `Store.UpdateAsync`). The store then:
- drops the instance's staged intents;
- restores its mapped properties and stamp from the baseline;
- leaves the baseline unchanged.

The caller re-reads or re-applies. This is stricter than stock, which leaves a failed instance dirty and lets the next save carry the leftovers.

Restoring the instance is an in-memory statement only. It says nothing about the database beyond what the definite failure already established. An **indeterminate** outcome follows §4.10 instead.

`NorseUserManager.UpdateUserAsync` already exists, so it gains the abandon call on a failed result. Verify item V5 audits every `UserManager` mutating method for staging followed by an early failure return that never reaches `UpdateUserAsync`.

### 4.9 Multi-subject reads are refused (answers R11)

`IUserClaimStore.GetUsersForClaimAsync` and `IUserRoleStore.GetUsersInRoleAsync` would materialize many subjects' decrypted PII, which the subject-singular read law (PII spec §4.1) forbids. In the key-destroyed window (§5), one erased member would also fail the whole list. **Both throw `NotSupportedException`**, citing the law. Nothing calls either today (grepped 2026-10-24). A future admin "who holds role X" surface answers with ids and masked projections through the disclosure contract, never with writable `NorseUser` instances, and that surface is its own design. The law is not amended.

### 4.10 Commit outcome — definite or indeterminate (answers R4)

A connection can fail after the database commits but before the application sees the acknowledgement ([EF Core: transaction commit failure](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency#transaction-commit-failure-and-the-idempotency-issue)). The store never guesses:

- **An exception from `CommitAsync`, or a connection failure after it was issued, is *indeterminate*.**
- **Verification, where a marker exists.** If the flush included the `users` statement, the store reads `concurrency_stamp` on a fresh connection:
  - the new stamp means **committed**: the baseline is refreshed and success returned;
  - the baseline stamp means **not committed**: handled as a definite failure (§4.8);
  - anything else means a concurrent writer intervened, so the outcome stays indeterminate.
- **No marker** (a credential-only or child-only flush, an increment, a redemption): the outcome stays **indeterminate**.
- **Indeterminate result:**
  - the operation returns `IdentityResult.Failed` with the named code `IndeterminateOutcome`, or throws `IndeterminateWriteException` from the immediate operations (§4.5);
  - staged intents are dropped;
  - the instance is **poisoned**: the baseline is removed, so §4.2 refuses any further `UpdateAsync` on it until the caller re-reads.
  
  Nothing is retried. The caller re-reads and decides.
- **Retry policy is per operation, not per flush.** The caller's documented recovery from `IndeterminateOutcome` is always "re-read, then repeat the manager operation". Each statement class has its own answer for whether that repeat is safe. A flush is safe to repeat only if **every** statement in it is.

  | Statement class | After acknowledgement loss, a repeat is… | Why |
  |---|---|---|
  | `users` scalar update | resolved, not repeated | The stamp check verifies it (above). If the commit landed, the re-read baseline already holds the new values, so the repeated diff is empty. |
  | credential compare-and-swap (`PasswordHash`, `SecurityStamp`, lookup hashes) | safe | The re-read rebaselines to the committed values. A repeat with the same target is an empty diff; a repeat with a fresh target swaps against the right baseline. |
  | `LockoutEnd` set, `AccessFailedCount` reset | safe | Value writes; replay converges. |
  | token upsert | safe | Convergent upsert. |
  | add claim | safe | Insert-if-absent on `(user_id, claim_type, claim_value_hash)` (§4.6); a replay is a no-op. |
  | add role | safe | Insert-if-absent on its primary key. |
  | add login | safe | A primary-key conflict with the same user is success (§4.6). |
  | passkey enrollment | safe | An identical-row conflict is success (§4.6). |
  | replace claim | safe | "New present, old absent" is success (§4.6). |
  | passkey assertion update | fails closed | The re-read sign count may already be the committed one; the repeated assertion proposes a count that is no longer greater, so sign-in fails. The user signs in again. |
  | any delete by key | safe | Idempotent. |
  | erasure | safe | Idempotent by construction (§7). |
  | `IncrementAccessFailedCountAsync`, `RedeemCodeAsync` | **never repeated** | Not idempotent. Both fail closed (§4.5). |

  If G6 fails, so that claims cannot carry a hash unique key on both providers, "add claim" moves to **unresolved**. The store then returns `IndeterminateOutcome`, documenting that the caller must read the user's claims (`GetClaimsAsync` reads the database) before repeating. That fallback is the only one recorded; the design does not silently degrade.
- **Failure injection:** a `DbTransactionInterceptor` that throws from `TransactionCommittedAsync` simulates "committed, acknowledgement lost". Paired with one that throws before commit, it proves both branches for every operation class (§10).

### 4.11 What stays stock

`RoleStore<…>`, `NorseSignInManager`, and the claims factory are untouched. `RoleStore.UpdateAsync` has the same blanket-write shape against temporal `roles`, but roles change rarely and only through admin action; it is a follow-on (§11).

### 4.12 The Outcome surface (ruling 12)

**Layering:**
- **Store:** `NorseUserStore`'s public face is Identity's fixed `IUser*Store` signatures, because `UserManager` is its caller. Internally the work is done by `Outcome`-returning methods. The interface methods are thin adapters, and they are the only place a state that `IdentityResult`/`int`/`bool` can't carry becomes a typed exception: `IndeterminateWriteException`, or `NotSupportedException` for the refused methods. Those exceptions exist only to cross `UserManager`'s plumbing.
- **Managers:** `UserManager`, `SignInManager` and the OpenIddict managers stay stock. Replacing them would mean rewriting token providers, the hasher and validator pipeline, the passkey handler and the lockout and 2FA flows — thousands of security-sensitive lines, rewritten for a return type.
- **Norse surface:** every Norse caller reaches Identity and OpenIddict through an **`Outcome` view**: a C# 14 extension property, `.Outcome`, on `UserManager<NorseUser>`, `SignInManager<NorseUser>` and the OpenIddict managers Norse uses (`IOpenIddictApplicationManager`, `IOpenIddictAuthorizationManager`, `IOpenIddictTokenManager`). The view's methods keep Identity's and OpenIddict's own names (`userManager.Outcome.ChangeEmailAsync(…)` → `Outcome<…>`), so each wrapper names exactly what it wraps.

**Mapping law (one table, in Himinbjörg; no other code translates `IdentityResult`):**
- `ConcurrencyFailure` → `Conflict`;
- `DuplicateEmail`/`DuplicateUserName` → `Conflict`;
- password, username and email validator codes → `Validation`;
- `SignInResult.IsLockedOut` → `LockedOut`;
- `IsNotAllowed` → `NotAllowed`;
- failed credentials → `InvalidCredentials`;
- `IndeterminateOutcome`/`IndeterminateWriteException` → **`Indeterminate`**;
- `KeyDestroyedException` → `Erased` (the existing `NorseSignInManager` fold, now centralized);
- refused methods → `Fault`;
- **any unmapped code → `Fault`**, never a silent default.

Each mapping is pinned by a test that enumerates every `IdentityErrorDescriber` code on the pinned Identity version, so a new code added upstream fails the build instead of falling through. OpenIddict exceptions map the same way: concurrency → `Conflict`, not found → `NotFound`, and others → `Fault`.

**`ErrorCategory.Indeterminate = 12` (Asgard, `Abstractions.Contracts`):** "the operation may or may not have taken effect; re-read before acting". `Fault` would misstate the outcome (it implies no effect), and `Conflict` would misstate it the other way. This is the only Asgard contract change in this spec, and it ships with Asgard's version bump ahead of Himinbjörg. Existing exhaustive `ErrorCategory` switches across the platform get the new arm in the same train (V9 enumerates them). **Transport disposition (ruled 2026-10-24):** `TransportDispositions.For(Indeterminate)` is `(500, 4, true)`: HTTP 500, gRPC `DEADLINE_EXCEEDED`, body permitted. `DEADLINE_EXCEEDED` is gRPC's own code for a state-changing call that may have completed, and HTTP 500 keeps clients from reading it as a gateway timeout or a retry cue (`503`/`UNAVAILABLE` were rejected because transient-fault retry layers resend on them, the blind retry this category exists to prevent).

**Placement (the dependency graph):**

```
Identity.EntityFramework  (entities, context; Microsoft.NET.Sdk)
        ▲
Identity.Outcomes          (NEW, Microsoft.NET.Sdk: IdentityOutcomeMap, the UserManager and OpenIddict-manager .Outcome views,
        ▲                   IndeterminateWriteException; references Asgard Abstractions.Contracts/Backend)
   ┌────┴─────────────┐
Identity.Web.Server   Identity.Migrations
(SignInManager .Outcome view, the store, handlers)   (MachineClientSeedContributor uses the OpenIddict view)
```

`SignInManager` lives in the ASP.NET Core shared framework, so its view stays in `Identity.Web.Server`. Everything the migration host needs lives in the non-web `Identity.Outcomes`, so `Identity.Migrations` never acquires `Identity.Web.Server` or the ASP.NET Core framework reference. A build check pins this.

**Framework-driven boundaries keep their own adaptation.** Some callers are the framework itself, not Norse code, and never pass through `.Outcome`:
- `SecurityStampValidator` calls `SignInManager.ValidateSecurityStampAsync` directly;
- Blazor's revalidating state provider calls `IdentityRevalidatingAuthenticationStateProvider.ValidateAuthenticationStateAsync`.

At those boundaries the existing narrow adaptation stays: `NorseSignInManager.ValidateSecurityStampAsync` folds `KeyDestroyedException` to `null`, and the revalidating provider folds it to "invalid". Both classify through the same shared helper the mapping table uses (`IdentityOutcomeMap.IsErased(exception)`), so there is one classification and two adapters. The framework paths are proven through the real framework callers (§10).

**Enforcement:** until banned-symbol infrastructure exists (PII spec §4.4's named trigger), a test in `Identity.Web.Server.Tests` scans Himinbjörg's handler, service and endpoint types and fails if any of them references a raw `IdentityResult`- or `SignInResult`-returning manager method, or calls an OpenIddict manager directly, instead of going through `.Outcome`. The scaffold pages (`Components/`) are exempt until #48 retires them.

## 5. Session and lookup behavior after the change

- **Revalidation:** `IdentityRevalidatingAuthenticationStateProvider` reads the stamp through `GetUserAsync` → `FindByIdAsync`. That is now a full-entity read with one join, which is accepted for a single-row lookup by primary key.
- **Erased subjects:** once the row is gone, revalidation finds no user and the session dies within one revalidation interval. That is the same session-kill SLA as before.
- **Key destroyed, rows not yet deleted** (the §7 window): materializing the user throws `KeyDestroyedException`. Login, `FindBy*` and revalidation must treat it as "no usable account": failed sign-in, through `LoginHandler`'s single static `Failed`, so the anti-enumeration collapse holds; revalidation fails; disclosure answers `Erased`. The suites in §10 test this window directly.

## 6. SQL Server realization — Urðarbrunnr (answers R9)

### 6.1 Shape

- **Non-split temporal entities: unchanged.** EF-native `IsTemporal()`, `SystemPeriodStart`/`SystemPeriodEnd` shadow periods, `{Table}History`. Every other realm keeps EF's temporal LINQ.
- **Split temporal entities: explicit opt-in, `TemporalOnPrincipalTableOnly()`.** It replaces `TemporalParkedOnSqlServer()`, which is deleted along with its annotation. The realization does **not** call `SetIsTemporal`. Instead it stamps the principal table's mapping, and a `NorseSqlServerMigrationsAnnotationProvider` plus `NorseSqlServerMigrationsSqlGenerator` carry that stamp into migration operations and emit engine-native system-versioning for the **principal table only**. This mirrors the PostgreSQL pair (`NorseNpgsqlMigrationsAnnotationProvider` / `NorseNpgsqlMigrationsSqlGenerator`). The period columns are `HIDDEN` and absent from the EF model, so inserts, updates and reads never name them, and fragments get nothing. Engine-native versioning is kept; this is **not** the rejected hand-rolled Model B triggers (chassis spec §4).
- **The split guard stays loud.** A split temporal entity without the opt-in throws at model finalize, with a message that names the opt-in and its cost.
- **Cost, accepted for Identity only:** no `TemporalAsOf`/`TemporalAll` on `NorseUser` under SQL Server. No realm uses temporal LINQ today (grepped 2026-10-24).
- **Re-entry trigger:** when #26457 ships, the opt-in's implementation can switch to EF-native without changing call sites.
- **PostgreSQL: unchanged**; the opt-in is a no-op there (chassis spec §2.3).

### 6.2 Lifecycle — supported operations on an opted-in table

| Migration operation | Emitted |
|---|---|
| `CreateTable` | Period columns (`GENERATED ALWAYS AS ROW START/END HIDDEN`), `PERIOD FOR SYSTEM_TIME`, `WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[{Table}History]))` inline. |
| `DropTable` | `SET (SYSTEM_VERSIONING = OFF)`, then drop the history table and the table, in either order once versioning is off. One batch (G5). |
| `AddColumn` / `DropColumn` / `AlterColumn` on the principal table (non-key, non-computed, non-identity) | Issued against the current table while versioning stays on. SQL Server propagates the change to the history table, so the history association is preserved; `AlterColumn` propagation includes `HIDDEN`-period tables, and an added `NOT NULL` column's default backfills existing history rows (G5). |
| Opt-in added to an existing table (enable) | `ADD` the hidden period columns with defaults (existing rows open at the enable timestamp), `ADD PERIOD`, `SET (SYSTEM_VERSIONING = ON (…))`. The period defaults are named `DF_{Table}_SystemPeriodStart`/`DF_{Table}_SystemPeriodEnd`; `SYSUTCDATETIME()` is evaluated once per statement, so every existing row opens at one enable timestamp. One batch (G5). This matches the chassis's PostgreSQL enable semantics (chassis spec §3.3). |
| Opt-in removed (disable) | `SET (SYSTEM_VERSIONING = OFF)`, `DROP PERIOD FOR SYSTEM_TIME`, `DROP CONSTRAINT IF EXISTS` both period defaults (present only when the table was enabled rather than created; otherwise the column drop fails with 5074), drop the period columns. One batch, as one unit: after `DROP PERIOD` the formerly hidden columns are visible (G5). The history table is handled per chassis spec §3.3's PostgreSQL disable semantics, so both providers behave alike. |
| Fragment columns (any operation) | Ordinary DDL; fragments are never versioned. |

**Rejected at migration generation with a named diagnostic** (mirroring chassis spec §3.4's PostgreSQL rejections): primary-key change, table rename, schema move, adding a computed or identity column to the principal table, and splitting or unsplitting an opted-in entity in a single migration. The diagnostic names the manual sequence the author must write instead. For a computed column the engine itself refuses the add while versioning is on (`13724`); the sequence is `OFF` (in a batch of its own) → add the column → add a plain column of the same type on history → `ON (…, DATA_CONSISTENCY_CHECK = ON)` (G5/G6). **Batching (G5):** a column added and referenced in one batch fails (207), and a change that needs versioning off can't share a batch with the statement that turns it off (13724); no other lifecycle sequence needs a split.

**Engine floor:** SQL Server **2025**, everywhere — the PoC, the Urðarbrunnr fixture and the Himinbjörg fixture. `NorseSqlServerEfProvider` forces compatibility level 2025, and the identity schema uses the native `json` type (passkey `Data`), which SQL Server 2022 lacks. Every fixture asserts `SERVERPROPERTY('ProductMajorVersion') >= 17` at startup.

The chassis gains its first **SQL Server container fixture**, which chassis spec §8 had deferred and is now required. Each row of the table above is applied and torn down against a live SQL Server, with a history row written before and read after every evolution step.

## 7. Erasure — hard delete, key first, external key store (answers R6)

This supersedes PII spec §4.2's darken-in-place ceremony and reverses its ordering.

**The key store is outside the database, today.** The active provider is Midgard's file-backed `DevelopmentSubjectKeyStore`, registered by `AddNorseDevelopmentKeys` and used by both Yggdrasil hosts and the Himinbjörg fixture. Its `DestroyAsync` is a two-phase file operation: it writes a pending receipt, deletes the key file, then promotes the receipt. `GetAsync` answers `Available`, `Destroyed(receipt)` or `Missing`. `SubjectKey`'s row is not the point of destruction. **No database transaction can include key destruction**, now or with the vault (Bifröst#14). The external-key contract applies to the current provider.

### 7.1 `ErasureService.ShredAsync(subjectId)` — every successful run does every step

r2's table let a retry return success from "row absent, key destroyed" without proving the OpenIddict cleanup had run, and its `Destroyed` branch never called `DestroyAsync`, so a pending receipt (which `GetAsync` also reports as `Destroyed`) was never promoted. r3 removes the branching. **Whether the row exists proves nothing about completion. Every run that returns `Ok` performs every step, and every step is idempotent:**

1. **Probe the key** with `GetAsync`.
   - `Missing`, and no `users` row: return `Err(NotFound)` with no writes (no key is burned for a ghost).
   - `Missing`, and a `users` row exists: return an honest `Fault` (an incident, not an erasure — the existing `KeyMissingException` law).
2. **`DestroyAsync`, unconditionally,** whenever the key is `Available` or `Destroyed`. It is idempotent by the provider's contract:
   - `Available` → destroys;
   - pending receipt → deletes the key if it's still present and promotes the receipt (this finishes an interrupted destruction);
   - promoted receipt → returns it (`DevelopmentSubjectKeyStore.cs:136-166`; V3 pins it by test).
3. **The database transaction (§7.2), unconditionally.** Both deletes are by id and idempotent, so they run whether or not the user row still exists. OpenIddict subject rows left behind by a failure that happened after the user row was deleted are cleaned up here.
4. **Return `Ok(receipt)`.**

Concurrent `ShredAsync` calls for the same subject converge **only if `DestroyAsync` is linearizable per subject** (§7.5). The database deletes are idempotent, so the key store carries the whole concurrency obligation.

### 7.2 The database transaction

1. Delete the OpenIddict tokens and authorizations whose `Subject` is the user id. No foreign key exists, so no cascade reaches them. Nothing matches today (only `client_credentials` is in use, whose subject is a client); this becomes load-bearing with #50. `applications` and `scopes` are never touched.
2. Delete the `users` row. The cascade removes `user_credentials`, `user_claims`, `user_roles`, `user_logins`, `user_tokens` and `user_passkeys`. The temporal tables write their final closed version: ciphertext under a key that no longer exists.

### 7.3 Failure boundaries and retry

| Failure point | State left | Re-run |
|---|---|---|
| Before or during `DestroyAsync` | Key `Available`, or a pending receipt; rows intact | Step 2 finishes the destruction; step 3 deletes |
| After `DestroyAsync`, before or during the database transaction | Key destroyed; rows intact (rolled back) — the §5 window | Step 2 returns the receipt; step 3 deletes |
| The commit of the database transaction is indeterminate (§4.10) | Rows gone or intact; unknown | Step 3's deletes are idempotent, so the re-run is safe either way |
| After commit, before returning | Everything gone | Steps 2 and 3 are no-ops; `Ok(receipt)` |
| Row deleted earlier, OpenIddict cleanup never ran (r2's hole) | Row gone; key in any non-`Missing` state; OpenIddict rows remain | Step 3 deletes them; success only after it commits |

Retry-until-`Ok` is the caller's obligation, recorded as the future DSAR machinery's contract (#55).

### 7.4 What a deleted subject's history retains

The final version of the `users`, `user_claims` and `user_roles` rows: ciphertext under the destroyed key, booleans, role ids, timestamps and our own opaque user id. No password hash, stamp, blind index or third-party identifier survives. Who may read history remains the Norns §7.3 grants story.

**`NotFound` vs `Erased` on live paths:** once the row is gone, a live lookup finds nothing. Until #55 persists receipts, an erased subject reads as `NotFound` on live paths; `ShredAsync` itself returns the receipt idempotently (§7.1 row 3). This is an accepted interim: an erased subject has no live session (§5).

### 7.5 Concurrent destruction — one authoritative receipt (answers R12)

**The contract (Asgard, `ISubjectKeyStore.DestroyAsync`, documentation and contract tests):** destruction is linearizable per subject. There is **one authoritative receipt** per subject. Authority begins when a receipt is **durably published** (the pending file is in place); from then on, however many callers race and however many retries follow, process restarts included, every caller receives that same receipt. A candidate minted in memory and lost before publication (a crash or failure before the pending write) has no authority and can't be recovered. The contract doesn't forbid it; it requires that such a failure leaves the key **intact**, so a retry completes destruction, possibly under a different candidate that then becomes authoritative on publication. A provider that can't guarantee this isn't a conforming `ISubjectKeyStore`. Bifröst#14's vault providers inherit the obligation (and it is the vault's job across hosts).

**The current provider (Midgard, `DevelopmentSubjectKeyStore`) violates it today.** Its existence checks, pending-receipt write and promotion (`DevelopmentSubjectKeyStore.cs:136-166`) are unsynchronized. Two callers can each write a pending receipt, so one caller returns a receipt that isn't the stored one. New protocol:

1. **The per-subject lock** is `{subjectId}.destroy.lock` in the store's root. **`FileShare.None` alone is not trusted:** on Unix, .NET treats share-mode locking as best effort. It skips locking when it's disabled (`DOTNET_SYSTEM_IO_DISABLEFILELOCKING`) or unsupported by the filesystem, and it ignores some locking errors, so a successful open doesn't prove exclusive ownership. Exclusion uses a **checked** primitive instead, per platform:
     - **Unix (macOS and Linux):** `flock(fd, LOCK_EX | LOCK_NB)` through a source-generated `[LibraryImport("libc")]`, on a descriptor from a **native** `open(path, O_RDWR | O_CLOEXEC)` (also `[LibraryImport]`, returning a `SafeFileHandle`), **never a `FileStream`'s**. V8 found that the .NET runtime takes its own unchecked `flock` whenever a `FileStream` opens a file on Unix (every access and share combination), so contention would surface as an `IOException` at open and the checked call would never be reached (ruled 2026-10-24). The lock file is ensured to exist first in a separate step (`open` is variadic, and passing a `mode` through P/Invoke isn't safe on Apple arm64): a .NET open-or-create that is disposed at once, where an `IOException` means the file already exists and is in use. `O_CLOEXEC` keeps child processes from inheriting the lock. `flock` returns `-1` with `errno` on failure, and is checked on every call. `flock` locks belong to the open file description, so two handles **in one process** exclude each other as well as two processes. `FileStream.Lock` is not used here: it throws `PlatformNotSupportedException` on macOS.
     - **Windows:** `FileStream.Lock` (`LockFileEx`), which throws on contention.
     
     The protocol treats **any** failure to obtain the lock, for whatever reason, as "lock not held". A per-subject in-process `SemaphoreSlim` sits in front of the file lock, so threads in one process queue instead of spinning on the descriptor. It's an ordering convenience, never the exclusion guarantee. Acquisition is bounded, with backoff; exhaustion throws, and the caller retries (§7.3). **Destruction never proceeds without the lock.** There is no fallback to an unlocked path.
   - **Supported configuration:** a local filesystem that honors `flock` (Unix) or byte-range locks (Windows) — APFS, ext4, xfs, NTFS. Network and synced filesystems (NFS, SMB, cloud-drive folders) and the disable-locking switch are **unsupported**.
   - **Startup verification (fail at startup, house rules §2.7):** the store's constructor runs an exclusion self-test in its root. It locks a probe file through one handle and proves that a second handle's lock attempt on the same range is refused. If exclusion can't be demonstrated, the constructor throws with a message naming the root and the supported configurations. A store that can't exclude never exists, and `AddNorseDevelopmentKeys` fails the host at boot.
   - V8 confirms the chosen primitives' semantics on macOS, Linux and Windows: exclusion across handles and across processes, and how failure is reported. **If any supported platform lacks a checked primitive, that platform becomes unsupported or the protocol is re-planned. It never falls back to an unchecked lock.**
2. **Under the lock:**
   - a promoted receipt exists → return it;
   - a pending receipt exists → delete the key if it's still present, promote the receipt, and return it;
   - the key exists → mint the receipt, write the pending file **atomically** (write a temporary file through to disk, then move it into place; the move is a rename, so the path is either absent or complete — and because it runs under the lock, its no-overwrite check never races), delete the key, promote, and return;
   - otherwise → `KeyMissingException`.
**Lock-free readers** (`GetAsync`, `GetOrCreateAsync`'s fast path) check key, then pending, then receipt: promotion is a single rename, so a reader that finds no pending file finds the receipt. A **first mint** takes the same per-subject lock, so a subject has one authoritative key just as it has one authoritative receipt (found in Task 2.3 review; .NET's no-overwrite `File.Move` on Unix is not an atomic no-overwrite and can't decide a race by itself).
3. **The published pending receipt is the single source of truth.** Every later path returns its contents, never a freshly minted receipt. Before publication nothing is authoritative: the key is still present, and the next call mints a fresh candidate.

**Scope of coordination:** every process sharing one store root on one host. That is the dev provider's entire deployment scope: Yggdrasil's web server and migrations service each root at their own content directory, and the Himinbjörg fixture uses its own temp root. Cross-host coordination is out of scope for the dev provider by definition.

**Proof (Midgard tests):**
- N concurrent `DestroyAsync` calls (in-process tasks, and two child processes sharing one root) all return byte-identical receipts equal to the stored one;
- a failure **before** publication leaves the key intact and writes no pending or promoted receipt, and a retry completes destruction (its receipt isn't compared with the lost candidate);
- a retry after each injected failure point **after** publication (pending written, key deleted, promoted) returns the published receipt byte-for-byte;
- **destruction cannot proceed without the lock:**
  - with the lock held elsewhere (another handle, and another process), `DestroyAsync` exhausts acquisition and throws, leaving the key file intact and no receipt, pending or promoted;
  - with the lock primitive forced to fail (through a test seam on the lock acquisition), the same holds;
  - with the startup self-test forced to fail, the store's constructor throws and the host does not start;
- concurrent `ShredAsync` calls in Himinbjörg's suite return the same receipt.

## 8. Migrations

Following the one-`InitialCreate` law, delete each provider's `Migrations/` folder and re-add `InitialCreate` on both providers, refreshing `schema/norse_identity.sql`. Expected DDL changes:
- `user_lockout` becomes `user_credentials`, with the six columns from §3;
- the unique index on `normalized_user_name` moves to `user_credentials`, unfiltered;
- `user_logins` loses its history apparatus;
- SQL Server `Users` gains the §6 system-versioning with hidden periods.

## 9. Mechanics proof of concept — `Glitnir/poc/identity-explicit-writes`

A throwaway proof, following the `poc/` pattern. It answers EF and engine mechanics questions only. Behavior is proven against the real protector and manager in §10, not here. It runs Testcontainers against **both providers**:

- **G1:** Can each staged statement shape from §4.3–4.6 be issued as `ExecuteUpdateAsync`/`ExecuteDeleteAsync` against one fragment of a split entity? Answering that also settles whether interpolated raw SQL is needed, through the store's own helper. Can a transaction span them under the execution strategy? **Answered (2026-10-24, partial fail; ruled raw SQL):** `ExecuteUpdate` is clean for `users`-only statements. On a `user_credentials`-only statement EF always joins the principal (`UPDATE … FROM users`; on SQL Server that share-locks the `Users` row). `ExecuteDelete` is refused for split entities, and `ExecuteUpdate` can't return the incremented count. **Every `user_credentials` statement, the erasure `DELETE` and the increment are therefore provider-specific raw SQL** (one table each, no cross-fragment locks); only the `users` statement stays `ExecuteUpdate`.
- **G2:** Does EF accept `HIDDEN` period columns it doesn't know about — inserts, the targeted updates, and full-entity reads?
- **G3:** Does the fragment's foreign key, as EF generates it, carry `ON DELETE CASCADE` on both providers?
- **G4:** Do the provider-native upserts (`ON CONFLICT` and `MERGE … HOLDLOCK`) behave as §4.6 claims when two writers race on the same key?
- **G5:** The §6.2 lifecycle table against a live SQL Server: create, drop, the three column operations with history preserved, enable on a populated table, and disable.
- **G6:** Can a stored, provider-computed SHA-256 column (`claim_value_hash`) sit on temporal `user_claims` on both providers — SQL Server EF-native system-versioning (history column materialized as a regular column) and the PostgreSQL chassis apparatus (history mirrors main) — with a unique index on `(user_id, claim_type, claim_value_hash)` serving the insert-if-absent statements?

The verdict goes in `FINDINGS.md` and is a human gate before §10.

## 10. Proof obligations and exit criteria (Himinbjorg#47)

These run in Himinbjörg's container suites through **`NorseUserManager`/`NorseSignInManager` and the real `NorsePersonalDataProtector`**, on **both providers**. They need a SQL Server twin of `PostgresIdentityFixture`.

**History**
- [ ] A wrong password, a password change, a stamp rotation, a lockout and a no-op `UpdateAsync` each write **zero** `users_history` rows. `A_failed_password_attempt_currently_versions_users` flips and is renamed.
- [ ] An email change through the real confirmation flow (`ChangeEmailAsync` from `ConfirmEmailChange.razor`'s path) writes exactly **one** history row. `Email` and `UserName` both change, and both normalized hashes are equal. The unchanged `PhoneNumber` keeps identical ciphertext bytes.
- [ ] `SetUserNameAsync` is refused. On **both providers**, direct SQL writes are rejected: unequal hashes (by the `CHECK`), `normalized_email = NULL` (by `NOT NULL`), and both hashes `NULL` (by `NOT NULL`).

**Atomicity**
- [ ] Forced failure after the `users` statement (a credential compare-and-swap conflict, then a unique-index race) leaves no partial data, no history row and an instance restored to its baseline.
- [ ] A child intent followed by a validation failure commits nothing.
- [ ] Failure injection on every class of operation (an identity flush, a credential-only flush, a child-only flush, the increment, the redemption, the erasure transaction):
  - a failure **before** commit gives a definite failure and an unchanged database;
  - a failure **after commit but before acknowledgement** gives the verified-committed outcome where a marker exists, and `IndeterminateOutcome`/`IndeterminateWriteException` with a poisoned instance where none does;
  - there is no automatic replay, and no duplicate child rows or increments.
- [ ] **Acknowledgement loss followed by the documented caller retry** (re-read, then repeat the manager operation), for every row of §4.10's policy table: each "safe" class leaves exactly the intended state (one claim, one role, one login, one passkey, one token value), each "fails closed" class fails, and each "never repeated" class is not repeated.

**Concurrency** (the tests synchronize two operations at a barrier)
- [ ] Concurrent failed attempts below the lockout threshold lose no increment.
- [ ] Concurrent failed attempts across the threshold produce exactly one lockout decision path per crossing request, and the account ends up locked.
- [ ] Concurrent password changes on one account: one succeeds, the other returns `ConcurrencyFailure`.
- [ ] Concurrent redemption of the same recovery code: exactly one succeeds. Concurrent redemption of different codes: both succeed and neither code is resurrected.
- [ ] Concurrent creation of the same token key: no unhandled constraint violation, and a defined final value. Concurrent **equivalent** enrollments of one passkey (same credential id, same owner, same public key): both succeed, and exactly one row is stored. Concurrent **conflicting** enrollments of one credential id (a different owner or a different public key): one succeeds, and the other fails cleanly with no row change.
- [ ] Out-of-order passkey assertions (both read count 10; one writes 12; the delayed one proposes 11): the delayed sign-in fails, and the stored count stays 12.
- [ ] Passkey removal racing an assertion: the assertion's sign-in fails, and the credential is **not** re-inserted.
- [ ] Two accounts changing to the same email concurrently: one succeeds, the other gets `DuplicateEmail`, and the `CHECK` constraint holds.

**Correctness**
- [ ] The three stock lost-write paths now persist.
- [ ] A write through `GetUserAsync` preserves `PasswordHash`, `EmailConfirmed` and `LockoutEnabled` (regression test for the data-loss bug).
- [ ] An instance without a baseline is refused by `UpdateAsync`.

**Erasure**
- [ ] Every failure boundary of §7.3 is tested, including a login and revalidation attempt during the key-destroyed window, and both r2-hole sequences:
  - absent row, live key, leftover OpenIddict rows, cleanup failure, then retry;
  - pending receipt, then retry, which promotes it.
- [ ] `GetUsersForClaimAsync` and `GetUsersInRoleAsync` are refused.
- [ ] During the key-destroyed window, the **real** `SecurityStampValidator` rejects the cookie (the principal is cleared, with no 500), and the revalidating provider reports the state invalid. Both are driven through their framework entry points, never through `.Outcome`.
- [ ] Concurrent `ShredAsync` calls for one subject, and a retry after each injected failure, all return the same authoritative receipt (§7.5; Midgard's provider tests carry the cross-process half).
- [ ] Re-registering an erased email succeeds.
- [ ] `UserManager.DeleteAsync` fails and leaves the row, the key and the OpenIddict rows intact.

**Schema and ship**
- [ ] Seven identity-record tables are temporal on **both** providers, and `user_credentials`/`user_logins` are not. `TemporalParkedOnSqlServer()` no longer exists.
- [ ] The three pre-existing branch failures in `Identity.EntityFramework.Tests` (`NorseUserRoleConfigureTests`, `NorseRoleConfigureTests`, `NorseUserConfigureTests`) are fixed or each explicitly re-ruled.
- [ ] Standard ship gate for Midgard (key provider) and Urðarbrunnr (chassis) first, in either order, then Himinbjörg: PR merged, CI green, tagged, published; Bifröst pointer bumped. Asgard's contract-documentation change rides with Midgard's, or ships first if it touches a public surface.

## 11. Out of scope / follow-ons

- **Explicit writes for `RoleStore`**: same shape, same reasons, done once the user-store pattern is proven.
- **Receipts and DSAR verbs** (#55): link/unlink receipts (ruling 6) and the `Erased` answer on live lookups (§7.4).
- **Retiring the scaffold pages** (#48).
- **Upstream #26457** (ruling 11).
- **Enforcing "claim values carry no PII"** (ruling 7).

## 12. Verify items (plan-time)

- **V1:** The exact `IUser*Store` set that `UserManager<TUser>` probes on .NET 11 RC2 (`Supports*`).
- **V2:** Whether `NorseUserManager`'s `SubjectCryptoScope` wraps every flush and `CreateAsync`, now that encryption happens in the store's statements (through the converter or explicitly) rather than in `SaveChanges`.
- **V3:** `DevelopmentSubjectKeyStore.DestroyAsync` on an already-destroyed key returns the stored receipt and doesn't throw (it appears to, at `DevelopmentSubjectKeyStore.cs:139`; confirm by test).
- **V4:** *Resolved in r3 (§3.1)*: the `CHECK` constraint makes the username index enforce email uniqueness. The plan only confirms that both providers emit the constraint through the migration.
- **V9:** Every exhaustive `switch` over `ErrorCategory` across the realms (grep plus the compiler's exhaustiveness warnings once the member lands) gets an explicit `Indeterminate` arm in the same train.
- **V8:** The §7.5 primitives (`flock` on macOS and Linux, `LockFileEx` on Windows): cross-handle and cross-process exclusion, and failure reporting, proven on each platform. An unsupported or unchecked primitive is a re-plan trigger, never a fallback.
- **V7:** `PasskeyHandler` and `SignInManager.PasskeySignInAsync` on .NET 11 RC2 read the passkey through the store before calling `AddOrUpdatePasskeyAsync`, so the as-read state exists, and they fail the sign-in when that call fails. Confirm against the RC2 binaries, not just `main`.
- **V5:** An audit of every `UserManager` mutating method for staging followed by an early failure return that bypasses `UpdateUserAsync`; each one found gets an abandon hook or a recorded ruling.
- **V6:** That every `NorseUser` returned by `UserManager` or `SignInManager` comes from a store read that registers a baseline (no manager path constructs or copies a user).

## 13. Remand record

**Status key:** *design answered* means accepted at spec review. *Proven* means the named PoC gate or the §10 obligation has been executed. **Nothing is proven yet.** The PoC (§9) and the behavioral suites (§10) have not run.

| # | Objection | Answer | Status |
|---|---|---|---|
| R1 | Ciphertext comparison can't detect unchanged content | Plaintext diff against the as-read baseline; unchanged ciphertext never rewritten (§4.2) | Design answered (r2 review); proof §10 History |
| R2 | Compare-and-swap needs original values | Baseline captured at read and create, refreshed only on commit, carried across scopes; baseline-less instances refused (§4.2, §4.4) | Design answered (r2 review); proof §10 Concurrency/Correctness |
| R3 | Detecting a conflict doesn't preserve the increment | Atomic `+1 RETURNING`/`OUTPUT` (§4.5) | Design answered (r2 review); proof §10 Concurrency |
| R4 | No atomicity; commit outcome undefined; blanket retry claim | Staged flush in one transaction (§4.3); definite vs indeterminate commit, stamp verification, poisoning, no automatic retry (r3). r4: a per-statement retry policy table; child inserts made replay-safe by insert-if-absent keys (the claim hash unique key, PoC G6), with a recorded "unresolved" fallback (§4.6, §4.10). r5: the passkey enrollment proof split into equivalent and conflicting cases (§10) | Design answered (r4 review); proof §10 Atomicity, PoC G6 |
| R5 | Recovery-code redemption raced | Atomic consumption by bounded compare-and-swap (§4.5) | Design answered (r2 review); proof §10 Concurrency |
| R6 | Key store misdescribed; retry skipped cleanup | External two-phase store (§7). Every `Ok` run performs every idempotent step, and `DestroyAsync` is always called (§7.1, §7.3) | Design answered (r3 review); proof §10 Erasure |
| R7 | `DeleteAsync` bypassed erasure | Store refuses it (§4.7) | Design answered (r2 review); proof §10 Erasure |
| R8 | Unsafe upserts; passkey regression and resurrection | Provider-native upserts for tokens; passkey enrollment insert-only, assertion a guarded update by the as-read sign count, never an insert (§4.6) | Design answered (r3 review); proof §10 Concurrency, PoC G4 |
| R9 | SQL Server lifecycle undefined | Supported and rejected operations defined; annotation flow mirrors PostgreSQL (§6.2) | Design answered (r2 review); proof PoC G5 |
| R10 | Email change contradicted *email is the username*; `CHECK` admitted a null hash | `CHECK` constraint; one atomic manager operation for email + username + hashes; `SetUserNameAsync` refused; confirmation page fixed (r3). r4: both hashes `NOT NULL`; null and unequal direct writes tested on both providers (§3.1) | Design answered (r4 review); proof §10 History |
| R11 | List reads violated the subject-singular law | `GetUsersForClaimAsync`/`GetUsersInRoleAsync` refused; law not amended (§4.9) | Design answered (r3 review); proof §10 Erasure |
| R12 | Concurrent destruction could return a non-authoritative receipt | The `ISubjectKeyStore` contract requires linearizable per-subject destruction with one authoritative receipt; Midgard's dev provider gains a per-subject cross-process lock and an atomic pending-receipt write; Midgard and Asgard added to the touched realms (r4). r5: a checked byte-range lock instead of best-effort share modes; supported filesystems stated; a startup exclusion self-test that fails the host; tests showing destruction cannot proceed without the lock (§7.5) | r5 — awaiting review; proof §7.5, §10 Erasure |

## 14. House-rules exemptions (ruled 2026-10-24)

`house-rules.md` is written for greenfield code. ASP.NET Core Identity and OpenIddict are the platform's brownest brownfield: their entity shapes, nullability annotations and interface signatures are owned upstream. Buvy ruled these exemptions, **scoped to Identity- and OpenIddict-derived entities and contracts only**:

- **Shadow properties (house rules, Entities):** permitted on Identity/OpenIddict-derived entities when adding a CLR member would change the drop-in shape. Live case: `NorseUserClaim.claim_value_hash` (§4.6).
- **Hand-written `.IsRequired()` (house rules, Entities):** permitted on inherited properties whose nullability annotation belongs to an upstream base type (`IdentityUser<Guid>`'s `string?` members), with a comment naming the base type. Live cases: `UserName`, `NormalizedUserName`, `NormalizedEmail`, `ConcurrencyStamp`, `SecurityStamp`.
- **Not exempted — `Outcome<T>` over throwing:** instead of an exemption, the Norse surface returns `Outcome` (§4.12). Exceptions are confined to the store's fixed-signature adapters and to startup and infrastructure failure (§7.5's lock and self-test).

