# Code and Schema Conventions — Enums and Database Objects

Reference detail relocated from `CLAUDE.md` §5 (which keeps the one-line rules and points here). These are rules, not preferences — §8's anti-pattern list cross-references them.

## Enums

- **Explicit values are required on every enum member.** Never rely on declaration order: `enum PolicyStatus { Draft, Quoted, … }` is forbidden; every member carries its integer (`Draft = 1, Quoted = 2, …`). Reordering or inserting members must be a **no-op for any persisted value** — implicit ordinals turn a refactor into a silent data-corruption event across every persisted row, in-flight message, and audit log entry, with no compile-time signal.
- **Reserve `0` for "unspecified" / sentinel only.** Real states start at `1`. A default of `0` is a column never deliberately set; it should not silently mean "Draft."
- **Never remove an enum value** that has been persisted. Mark `[Obsolete]` and keep the integer.
- **String-mapping over int-mapping at the database boundary** where the enum appears in queries, dashboards, or bordereaux. `HasConversion<string>()` keeps `policy_status = 'Bound'` readable and survives renumbering. The explicit-integer rule still applies on the C# side — it protects in-flight messages, audit logs, and int-mapped tables.

## Database Objects

- **Schema per bounded context:** `policy`, `claims`, `underwriting`, `customer`, etc.
- **Table names:** snake_case, plural — `policy.policies`, `claims.claim_payments`. EF Core conventions apply snake_case automatically.
- **Column names:** snake_case. Foreign keys: `{referenced_table_singular}_id`.
- **Constraints:**
  - PK: `pk_{table}` · FK: `fk_{table}_{referenced_table}` · Unique: `uq_{table}_{columns}` · Check: `ck_{table}_{rule}` · Index: `ix_{table}_{columns}`
- **Migrations:** descriptive names, no timestamps in the human-readable part (EF prepends them). Read as a changelog: `AddPolicyCancellationReason`, not `Update3`.

## Async

- **The elide law.** A method that does no work after its last await neither marks `async` nor awaits — it returns the `Task` directly. `await` exists only when there is work after the resumption. **Exception, load-bearing:** the await stays when the task is produced inside a `try`/`catch`/`finally` or `using` scope — eliding there lets the task escape the scope (the connection disposes before the query completes; exceptions detach from their handlers). Elide only pure tail positions. Enforcement: YGG analyzer bench (editorconfig spec §9); review-enforced until then.
- **Concurrent awaits use the tuple idiom.** Independent async operations are awaited concurrently as a tuple — `var (quote, risk) = await (GetQuoteAsync(id), GetRiskAsync(id));` (TaskTupleAwaiter) — not sequentially, and not via `Task.WhenAll` ceremony for disparate types.
- **ConfigureAwait discipline is already build law** — CA2007 is an error platform-wide (Reliability `latest-All`).

## Style Law

The complete style, formatting, and naming law lives in the root `.editorconfig`, designed in `docs/Platform/specs/2026-06-05-editorconfig-curation-design.md`. Headline rules: tabs (width 4); var everywhere except construction (type left, `new()` right); file-scoped namespaces; `omit_if_default` accessibility; `_camelCase` private fields; collection expressions (`[]` is the only legal empty); IDE0005 unnecessary-using as build error.
