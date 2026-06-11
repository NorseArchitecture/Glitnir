# .editorconfig Curation Design — Style Law, Declared in Full

**Date:** 2026-06-05
**Status:** Approved design, awaiting plan greenlight
**Scope:** Phase 2 of the build-enforcement session (spec-reconciliation punch item 4.2). The root `.editorconfig` — the entire style-option surface, severity tiers, naming law, non-C# file law — plus the harness canaries that prove it, the analyzer-bench entries it spawns, and the CLAUDE.md amendments it carries. Builds directly on the proven Phase 1 substrate (`2026-06-05-build-enforcement-design.md`, `poc/build/FINDINGS.md`).

---

## 1. Problem and Goals

Phase 1's POC surfaced the forcing function (deviation #3): with `latest-Recommended` + `EnforceCodeStyleInBuild=true` + the global ratchet, Style-tier IDE rules already fire as **build errors judged against Roslyn's stock defaults**. Until house options are declared, Microsoft's preferences are silently the law — the exact failure mode §2.5 of CLAUDE.md exists to prevent. The three-line seed (tabs, LF, file-scoped namespaces) was the down payment; this design is the full payment.

Goals:

1. **Nothing silently inherited.** Every style and formatting option carries an explicit house value — even where we agree with the stock default. An SDK preview shifting a default can never silently re-legislate.
2. **Severity is a deliberate act.** Mechanical rules are build errors; judgment rules are declared-but-silent, each with its reason inline. No unexamined middle ground.
3. **Born build-enforced.** Every error-tier severity declared here fires through the Phase 1 bridge on day one — no second wiring step. The harness proves it with canaries (and proves the silent tier stays silent), same as the props law.

Non-goals: razor build-time formatting enforcement (does not exist — §4 names the gap); analyzer authoring (bench entries recorded in §9, built in the Primitives analyzer session); `dotnet format` CI integration (rides the CI pipeline session).

## 2. Session Rulings (Decision Ledger)

1. **Coverage: full surface, declared law.** All ~150 style/formatting options explicit. (Alternatives rejected: activated-set-only and evidence-first accumulation — both leave stock defaults ruling gaps under SDK drift.)
2. **Severity: tiered — error + silent.** Mechanical → error. Judgment → declared value, `silent` severity, inline `# judgment:` reason. No blanket-error (taste suppressions become their own noise ledger) and no kill-list (`none` loses the IDE nudge where the rule is right).
3. **The var law:** var everywhere, **except construction — type left, `new()` right.** `var i = 42;` legal; `DataTable dt = new();` mandatory over `var dt = new DataTable();`.
4. **Naming: full classic-C# naming law now, at error.** Nothing custom. Compiler errors, not ReSharper squigglies. Private fields (instance and static): `_camelCase`.
5. **Non-C#: whole-repo coverage.** JSON joins the spaces exceptions at 2 (tooling emits 2-space; fighting it churns diffs).
6. **Don't fight the ecosystem — codified law.** Where an ecosystem's standard tooling has a fixed convention (Black's 4-space Python, Fantomas's F#, dotnet CLI's 2-space JSON), the ecosystem wins and the exception is declared with its reason inline.
7. **Tab width: 4** (was 2). Consequence of #6: C# tabs render at the same visual width as F#/Python's 4 spaces side by side in VS. CLAUDE.md §4 amendment (§10).
8. **Collection expressions: modern idiom enforced.** `when_types_loosely_match`; fluent `.ToList()` with an explicit collection target is a build error rewritten to `[.. source]`; `[]` is the only legal empty.
9. **Async suffix: omitted deliberately.** A .NET 4.5-era artifact; the platform's interface-driven surfaces (NSB `Handle`, mediator `Handle`) are async-by-signature with contract-owned names — a hard suffix rule fights the ecosystem.
10. **Async elide law landed as text** (§8); enforcement is analyzer-bench (§9) — no stock rule exists.
11. **No null collections** — law stated (§8), compiler share already enforced, declaration ban is analyzer-bench.
12. **`JsonSerializerIsReflectionEnabledByDefault=false` is a `src/`-only delta**, not root law. Tests and benchmarks reflect-serialize freely; only working software is held to the source-gen posture (and it is a runtimeconfig switch — inert on libraries, binding on deployables, which live in `src/`).
13. **Razor files: spaces, 4 — ruled 2026-06-06 after the VS test drive reproduced the Ctrl+K,D conversion.** Root cause is upstream and conclusive: the VS Razor formatter reads Tools → Options, never `.editorconfig` — blocked at the VS editor platform since 2021 (dotnet/razor #4406, backlogged, no owner; #7972 closed as duplicate; #12223 shows it persisting into VS 2026). There is no VS version floor where the tabs section works, and no build gate exists to catch drift. Don't fight the ecosystem (ruling #6) applies: razor joins the spaces exceptions at the VS default (spaces, 4), so stock VS produces lawful files on every machine with zero configuration. `tab_width = 4` keeps visual parity with tabbed C#. Named cost: `@code` blocks are spaces while `.cs` is tabs — the mix self-corrects in the enforced direction (space-indented paste into `.cs` fails IDE0055 at build; tab-indented paste into razor is reformatted by the next Ctrl+K,D). The per-machine alternative (Tools → Options "Keep tabs" as a workstation prerequisite) was rejected as machine config doing repo-law work — the `core.autocrlf` failure mode reborn.
14. **Braces and expression-bodied members: re-tiered 2026-06-06 from the VS test drive (refined same day).** IDE0011 demoted error → silent with the value flipped `true` → `when_multiline` (single-line control statements may omit braces; the IDE suggests removal on single lines and still nudges multiline safety). Expression bodies: **enforced on everything except functions** — properties/indexers/accessors (`true`, IDE0025/26/27 = error) and single-line constructors/operators (`when_on_single_line`, IDE0021/23/24 = error) are mechanical shapes; **methods and local functions get `suggestion`** (IDE0022/IDE0061) — function bodies are where logic lives, so expression-bodying them is the author's call, surfaced as a visible nudge with zero build impact (suggestion is info-level; the ratchet only escalates warning-and-above). This introduces the third tier: error (unmarked) / suggestion (visible, never build-blocking) / silent (IDE refactor only), each non-error entry carrying its reason inline.
15. **The var law's apparent bucket: flipped to `true` — re-ruled 2026-06-06 from the VS test drive.** Roslyn's "type is apparent" heuristic fuses factory-style invocations (`CultureInfo.GetCultureInfo(...)` — any `TypeName.Method(...)` returning `TypeName`) with object creation in a single knob; `false` made factories demand explicit types, fighting var-on-returns (IDE0008 fired on `var culture = CultureInfo.GetCultureInfo(tag)`). With all three buckets var-preferred: factories and casts get var; **IDE0008 becomes unreachable** (severity line kept, frozen; its canary removed — no canary can exist for a rule with no firing condition); `Type x = new();` remains stable law, with a nuance proven by the canary: **on locals, the IDE0090 analyzer defers to use-var** when var is preferred for the declaration — its enforceable territory under the all-var buckets is declarations where var is impossible (fields, property initializers); a verbose local reaches a lawful form through IDE0007 instead. The single now-legal unwanted form — `var x = new T();` — is inexpressible in the option surface and becomes analyzer bench entry #4 (a syntax-level rule: no var on object-creation initializers). The law's statement is unchanged; only its enforcement split moved.
16. **Committed generated code: carved out via `generated_code = true` — ruled 2026-06-06 from the carrier-platform port.** Files that a scaffolder owns but the repo commits and compiles must not answer to house style; the known instance is EF Core migrations. The `.Designer.cs` companion and `*ModelSnapshot.cs` carry `<auto-generated />` headers and are already Roslyn-auto-exempt; the main migration file deliberately is not (Up/Down are hand-editable), so the law declares it: a final `[**/Migrations/**.cs]` section sets `generated_code = true` (style diagnostics stop firing; `dotnet format` skips) plus `indent_style = space` / `indent_size = 4` so hand-edits inside `Up()` don't mix tabs into the scaffolder's 4-space output — don't fight the ecosystem (ruling #6): the scaffolder is the formatter and its convention wins. Alternatives rejected: reformatting scaffolder output (`dotnet format` post-step or custom `IMigrationsCodeGenerator`) fights the ecosystem and makes the easy path the wrong path; MSBuild project-level `EnforceCodeStyleInBuild=false` over-exempts hand-written neighbors and over-exempts whole projects where migrations are folders inside larger ones. Named quirk: editorconfig `**` ignores segment boundaries, so a `FooMigrations/` folder also matches — tolerated, because a `*Migrations` folder that isn't migrations violates naming law anyway. Named trade-off: hand-edits inside a migration get zero style policing; review covers them — the file is scaffolder-owned. `end_of_line`/`trim_trailing_whitespace` deliberately untouched in the section (known demand only; revisit on observed save-churn).

## 3. File Anatomy and Mechanics

**One file, root only: `/.editorconfig`, `root = true`.** No subtree `.editorconfig` anywhere, ever — zero override chains, same as the props law. Organized as numbered, commented sections: universal defaults → non-C# overrides → C# style law → C# formatting law → naming law → targeted diagnostic severities.

**Tier convention, visible in the file.** Error tier is the unmarked default. Every silent-tier entry carries an inline `# judgment:` comment with its one-line reason — a future reader never wonders whether a severity was chosen or forgotten.

**Form rules** (POC review finding — the `option:severity` suffix is fragile across SDK drift):

- Option values bare: `csharp_style_namespace_declarations = file_scoped`
- Severities separate and explicit: `dotnet_diagnostic.IDE0161.severity = error`
- The Phase 1 three-line seed dissolves into the full law (it was the down payment, not a separate artifact)

**Boundary with `Directory.Build.props`:** category-level escalation lives in props (Phase 1, done). `.editorconfig` carries (a) the style-option surface, (b) per-rule IDE severities, (c) targeted `dotnet_diagnostic.CAxxxx` lines **only** for rules unreachable by the category knobs. Concretely: CA1848/CA2254/CA1852 are Performance/Usage rules `latest-All` should already activate — they get harness canaries proving it, not redundant lines. CA1727 is **Naming** category (not escalated, disabled by default) — it gets an explicit `dotnet_diagnostic.CA1727.severity = error`. Evidence decides each queued ratchet's side of the boundary during implementation, via the harness.

**Frozen-at-stock principle for the long tail.** Options where this session made no explicit ruling are declared at Roslyn's current stock value — declared to *freeze* them, not to change them. Severity escalation beyond what `latest-Recommended` already activates happens only where this spec lists it; unactivated rules keep their declared value at IDE-only severity. (The mechanical/judgment test in ruling #2 guides any rule the implementation pass finds unlisted; precedents are §5's sets.)

**Severed trees need no editorconfig severance.** POCs inherit the root file's *options* (IDE squiggles show house style — good) but their severed props floors have no `EnforceCodeStyleInBuild` and no ratchet, so nothing fires at build time. `poc/build/` keeps its own `root = true` replica — deliberately a sealed universe.

## 4. Universal Defaults and Non-C# Law

**`[*]` floor:** `charset = utf-8` · `end_of_line = lf` · `insert_final_newline = true` · `trim_trailing_whitespace = true` · `indent_style = tab` · `tab_width = 4` · `indent_size = 4`. Mirrors the 2026-06-05 `.gitattributes` ruling (`* text=auto eol=lf`) — git normalizes at commit as the backstop; the editor births files correctly in the first place.

**Declared exceptions, each with its inline reason:**

| Section | Override | Reason |
|---|---|---|
| `[*.{yml,yaml}]` | spaces, 2 | Whitespace-aware; ecosystem norm is 2 |
| `[*.md]` | spaces, 2; `trim_trailing_whitespace = false` | Trailing double-space is a hard line break |
| `[*.json]` | spaces, 2 | .NET tooling rewrites JSON 2-space; don't fight the ecosystem |
| `[*.py]` | spaces, 4 | PEP 8; Black is hardcoded to 4 |
| `[*.{fs,fsx,fsi}]` | spaces, 4 | F# compiler rejects tabs; style guide / Fantomas is 4 |
| `[*.{bat,cmd}]` | `end_of_line = crlf` | cmd.exe requirement; matches `.gitattributes` |
| `[*.{razor,cshtml}]` | spaces, 4 — **explicit, never `[*]` fallthrough** | VS Razor formatter is editorconfig-blind upstream (ruling #13); align with its default |
| `[**/Migrations/**.cs]` | `generated_code = true`; spaces, 4 | EF scaffolder owns the file; the main migration lacks the `<auto-generated />` header by design (ruling #16) |

`[*.{props,targets,csproj,slnx}]`, `[*.ps1]`, `[*.sql]` carry no sections — they ride the `[*]` tab law.

**Razor — the Ctrl+K,D history, resolved by ruling #13 (2026-06-06):**

1. **The original tabs plan died on evidence.** The VS test drive reproduced the conversion immediately; investigation found the VS Razor formatter never consults `.editorconfig` for indentation — blocked at the VS editor platform since 2021 (dotnet/razor #4406). The "VS floor pinned" mitigation in this spec's first draft was unfoundable: no floor exists.
2. **Razor is a declared spaces exception (spaces, 4)** — don't fight the ecosystem. Stock VS produces lawful output on every machine, zero configuration. The explicit section stays, binding every consumer that *can* read editorconfig (Rider, future VS if #4406 lands), with the upstream issue cited inline.
3. **Honest gap, still named:** `EnforceCodeStyleInBuild` does **not** cover razor markup — no build-time razor formatting gate exists; enforcement is IDE-level only. The ruling shrinks the gap's blast radius: the law now agrees with the only tool that formats these files, so there is no setting left to drift. `@code` C# is spaces inside razor (named cost in ruling #13; self-correcting at the `.cs` boundary via IDE0055).

## 5. C# Style Law

Full option-by-option table is the implementation artifact; the law is by cluster. Severity per ruling #2.

### Error tier (mechanical, build-enforced)

| Cluster | Options / rules |
|---|---|
| Namespaces | `csharp_style_namespace_declarations = file_scoped` (IDE0161); `dotnet_style_namespace_match_folder = true` (IDE0130) — CLAUDE.md §5's "folder mirrors namespace" becomes a build error |
| Usings | **IDE0005 unnecessary-using = error** — the drive-by-using ratchet; twenty AI-suggested usings irrelevant to the change are a build failure. Build participation requires `GenerateDocumentationFile=true` — already tree-wide law from Phase 1 (the CS1591 synergy). Placement `outside_namespace` (IDE0065); `dotnet_sort_system_directives_first = true`; `dotnet_separate_import_directive_groups = false` |
| Accessibility | `dotnet_style_require_accessibility_modifiers = omit_if_default` (IDE0040) — closes the queued reconciliation item; a redundant `private` is a build error, matching CLAUDE.md §2.3 |
| The var law | All three var buckets `true` (re-ruled — ruling #15: the apparent bucket fuses factories with construction, and factories get var) · IDE0007 = error, IDE0008 = error-but-unreachable · `csharp_style_implicit_object_creation_when_type_is_apparent = true` (IDE0090 = error). Composition yields the house forms: `var culture = CultureInfo.GetCultureInfo(tag);` and `DataTable dt = new();` — the residual gap (`var dt = new DataTable();` now legal) is analyzer bench #4 |
| Collection expressions | `dotnet_style_prefer_collection_expression = when_types_loosely_match`; IDE0300–IDE0305 = error. Fluent `.ToList()`/`.ToArray()` with an explicit collection-typed target rewrites to `[.. source]` (IDE0305); `[]` is the only legal empty (IDE0301). Loosely-match is what makes interface targets (`IList<int> vals = [.. x]`) fire. A collection expression is construction — explicit type left, brackets right — consistent with the var law. **Limitation, named:** collection expressions need a target type, so `var vals = x.ToList();` is unreachable by any editorconfig rule — see analyzer bench (§9). **Nuance, recorded:** for `IList<T>`/`ICollection<T>` targets the compiler emits `List<T>` (identical to `.ToList()`); for `IEnumerable<T>` targets it may synthesize an opaque immutable — only observable to code downcasting interface-typed collections, which is its own smell |
| Modifiers | modifier order (IDE0036); `readonly` where possible (IDE0044) |
| Expression bodies — non-functions | properties/indexers/accessors `true` (IDE0025/26/27); single-line constructors/operators `when_on_single_line` (IDE0021/23/24) — mechanical shapes (ruling #14) |
| Null handling | `is null` over reference-equality (IDE0041); null-propagation (IDE0031); coalesce (IDE0029/0030); throw-expressions (IDE0016) |
| Keywords and simplification | language keywords over BCL names — `int` not `Int32` (IDE0049); simplified `default` (IDE0034); inferred tuple/anonymous member names (IDE0037) |
| Hygiene | unused parameters (IDE0060); unnecessary assignment (IDE0059 — proven firing in the POC); no `this.` qualification (`dotnet_style_qualification_for_*` = false, IDE0003) — pairs with `_camelCase` fields |

### Silent tier (declared value, IDE nudge only, `# judgment:` reason inline)

| Rule | Declared value | Judgment reason |
|---|---|---|
| IDE0045/IDE0046 (prefer conditional expression) | `true` | The famous readability degrader — right often, wrong badly in nontrivial cases |
| IDE0066 (switch expression) | `true` | Complex bodies read worse as expressions |
| IDE0290 (primary constructors) | `true` | Capture semantics make it genuinely situational |
| IDE0022/IDE0061 (expression-bodied methods/local functions) | `when_on_single_line` | **Suggestion tier** (ruling #14) — function bodies are where logic lives; visible nudge, author's call, zero build impact |
| IDE0011 (braces) | `when_multiline` | Demoted from error, value flipped from `true` (ruling #14) — single-line control statements may omit braces; "remove braces" is the suggested direction, multiline keeps the safety nudge |
| IDE0019/IDE0020/IDE0078 (pattern-matching maximalism) | `true` | Preferred idiom, not a correctness issue |
| IDE0047/IDE0048 (parentheses) | `always_for_clarity` | Clarity is the point; enforcement would litigate taste |
| IDE0039 (local function over lambda) | `true` | Situational |
| IDE0056/IDE0057 (index/range operators) | `true` | Situational |
| `csharp_style_prefer_top_level_statements` | `true` | Hosts use them; libraries have no entry points to care |

**Deliberately not set:** `file_header_template` — no license-header requirement exists; the day one does, it is one line.

## 6. Formatting Law

IDE0055 already fires as error on the substrate, so the entire formatting option set must be declared or stock defaults adjudicate (deviation #3's lesson). House formatting is deliberately un-exotic — **stock VS C# conventions, declared explicitly, on tabs**:

- **New lines:** Allman everywhere — `csharp_new_line_before_open_brace = all`; before `else`, `catch`, `finally`; before members in object initializers and anonymous types; between query clauses
- **Indentation:** switch case contents and labels indented; block contents indented; braces not; labels one-less-than-current
- **Spacing:** space after control-flow keywords and around binary operators; none after method names, inside parentheses, or before commas; standard C# defaults throughout
- **Wrapping:** `csharp_preserve_single_line_blocks = true`; `csharp_preserve_single_line_statements = false`

The value is not novelty — it is that an SDK preview can never silently re-legislate formatting. All enforced through IDE0055 = error.

## 7. Naming Law

Classic C# conventions, nothing custom, full `dotnet_naming_*` rule set at **error** — IDE1006 violations are compiler errors in build output, not editor decoration. (ReSharper reads the same file and squiggles the same rules — a preview of the failure, not the source of truth.)

**Build enforcement requires the diagnostic-ID severity line, not just the per-rule severities** (carrier-platform port, 2026-06-06; FINDINGS #18). `dotnet_naming_rule.*.severity = error` reaches `dotnet build` on .NET 11's Roslyn but only the IDE on .NET 10 (roslyn #49439). The law therefore carries `dotnet_diagnostic.IDE1006.severity = error` — the path the build reads on every SDK; confirmed forcing the error on .NET 10. Same form-rule lesson as preferring `dotnet_diagnostic` over the `option:severity` suffix: the diagnostic-ID path is the robust one. Not optional.

| Symbol class | Rule |
|---|---|
| Interfaces | `I` + PascalCase |
| Type parameters | `T` + PascalCase |
| Types (class/struct/enum/record/delegate) | PascalCase |
| Methods, properties, events, local functions | PascalCase |
| `const` (fields and locals) | PascalCase — never SCREAMING_CASE |
| Private fields (instance **and** static) | `_camelCase` — under `omit_if_default` the prefix does real work: a bare `name = value;` is instantly a field write, and it kills `this.` qualification permanently |
| Public/internal fields (rare — effectively const/static readonly only) | PascalCase |
| Parameters and locals | camelCase |

**Deliberately omitted: the `Async` suffix rule.** The engine can express it (`required_modifiers = async`), but the platform's interface-driven surfaces — NSB `Handle`, mediator `Handle`, plugin contract methods — are async-by-signature with contract-owned names; a hard suffix rule would flag every handler implementation. A .NET 4.5-era artifact; omitted with this reason inline. (Scoped-to-public-library-surface enforcement would be analyzer-bench work, not a blunt naming rule.)

## 8. Landed Contours — Laws Stated Here, Enforced Elsewhere

These are platform law as of this spec; their enforcement homes are named per clause.

### 8.1 No null collections

**Absence of items is `[]`, never null. `?` on an enumerable-shaped type is a design error.** Null is reserved for genuinely-optional references (NRT-declared `T?`) and `Nullable<T>` structs.

| Clause | Enforcement home | Status |
|---|---|---|
| Returning/assigning null into a non-nullable enumerable | `Nullable=enable` + ratchet (CS8603/CS8625 = error) | **Already enforced** — Phase 1 substrate |
| The empty form is `[]` | IDE0301 = error (§5) | This spec |
| Declaring `IEnumerable<T>?` / `IList<T>?` / etc. at all | YGG analyzer (§9) | Bench; review-enforced until built |

CLAUDE.md §8 gains the anti-pattern line (§10).

### 8.2 The async elide law

**A method that does no work after its last await neither marks `async` nor awaits — it returns the `Task` directly. `await` exists only when there is work after the resumption.**

**Exception, load-bearing:** the await stays when the task is produced inside a `try`/`catch`/`finally` or `using` scope — eliding there lets the task escape the scope: the connection disposes before the query completes, exceptions detach from their handlers. **Elide only pure tail positions.**

No stock IDE/CA rule expresses this → analyzer bench (§9). Prior art exists (AsyncFixer AF01, Roslynator RCS1174 — both scope-aware); whether the analyzer session adopts one surgically (everything-off-except-one-rule) or writes the YGG rule is that session's call.

Adjacent, already enforced: **CA2007 is an error** since Phase 1 (Reliability `latest-All` canary) — ConfigureAwait discipline holds platform-wide.

### 8.3 Concurrent awaits — the tuple idiom

Independent async operations are awaited concurrently as a tuple, not sequentially and not via `Task.WhenAll` ceremony for disparate types:

```csharp
var (quote, risk) = await (GetQuoteAsync(id), GetRiskAsync(id));
```

**TaskTupleAwaiter** (maintained by Buvy with Joseph Musser) is the enabling library and enters the platform stack-defaults table when the Primitives/async work lands. Recorded here as platform idiom; conventions doc carries it (§10).

## 9. Analyzer Bench

YGG rules this spec spawns but does not build — they land in the Primitives analyzer session. Recorded now so the law's text has a home before its enforcement does:

| Bench entry | Rule sketch | Why no editorconfig rule can |
|---|---|---|
| Nullable-enumerable ban | Flag `?` annotations on enumerable/collection-shaped types in declarations (returns, parameters, properties, fields) | Nullability-of-declaration is invisible to the style-option engine |
| ToList-into-var | Flag `var x = expr.ToList()/.ToArray()` — declare the collection type and spread it | Collection expressions need a target type; `var` gives Roslyn nothing to convert to |
| No-var-on-construction | Flag `var x = new T(...);` — the law's form is `T x = new(...);` (ruling #15) | The apparent bucket fuses factories with object creation; preferring var for factories makes var-on-new legal to the option surface — only a syntax-level rule can split them |
| Async/await elide | Flag `async` methods whose only await is in pure tail position (outside `try`/`using` scopes) — return the task directly | No stock rule; requires scope-aware semantic analysis (prior art: AsyncFixer AF01, Roslynator RCS1174) |

## 10. Amendments Ledger

Riding this spec, applied on approval:

| Target | Amendment |
|---|---|
| CLAUDE.md §4 (Runtime and Language) | Tabs, **4-space width** (was 2); add **"Don't fight the ecosystem"** to the convention bullets — ecosystem-formatter conventions beat house style, exceptions declared with reasons |
| CLAUDE.md §8 (Anti-Patterns → Type Safety) | **No null collections** — empty is `[]`, never null; nullable enumerable declarations are banned (analyzer-bench until YGG rule lands) |
| `docs/conventions.md` | New sections: the async elide law (§8.2 text), the tuple-await idiom (§8.3), pointer to this spec for style law |
| `docs/spec-reconciliation-2026-06-04.md` item 4.2 | Phase 2 mechanics checked: `omit_if_default` ✓, CA1848/CA2254/CA1727 ✓ (boundary rule §3), CA1852 ✓ (canary-verified), JSON feature switch ✓ (relocated to `src/` delta, ruling #12), root config files ✓ (committed 2026-06-05) |
| Build-enforcement spec §5 | `src/Directory.Build.props` delta gains `<JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>` (ruling #12) |

## 11. Verification

**Harness growth — deviation #3's promissory note, paid.** New `#if CANARY`-gated probes, each asserted to fire as `error`:

| Canary | Proves |
|---|---|
| Space-indented line | IDE0055 + declared formatting law |
| Block-scoped namespace | IDE0161 |
| Verbose-new **field** initializer **and** `int count = GetCount();` local | The var law: IDE0090 (verbose new — fields only; on locals the analyzer defers to use-var) + IDE0007 (use var). IDE0008 is unreachable under the all-var buckets (ruling #15) — no canary possible |
| Redundant `private` keyword | IDE0040 `omit_if_default` |
| A `m_field` | IDE1006 naming law |
| One gratuitous using | IDE0005 — the slop ratchet, proven |
| Fluent `.ToList()` with explicit collection target | IDE0305 |
| CA1727 violation (lowercase log placeholder) | The targeted Naming-category severity line |
| CA1848 / CA2254 / CA1852 violations | Category escalation reaches them **without** editorconfig lines (boundary rule §3) |

**Inverse canary:** an IDE0046-violating conditional that must build **clean** — proving the silent tier stays silent, not just that errors fire.

**Generated-code inverse canary (ruling #16):** a deliberately unlawful, scaffold-shaped file inside a `Migrations/` folder in the harness — space-indented, block-scoped namespace, the works — that must build **clean**, proving the carve-out actually exempts.

**CS1591 check on migrations (ruling #16):** confirm EF's scaffolded `/// <inheritdoc />` keeps the docs requirement quiet on the main migration file — `generated_code` silences analyzer diagnostics but **not** compiler warnings. If it fails, the fix is a `NoWarn`-scoped conversation, flagged here before it's needed.

**Landing assertions:** `src/` probe evaluates `JsonSerializerIsReflectionEnabledByDefault=false`; `tests/` probe evaluates it absent (ruling #12).

**Razor acceptance check (manual, documented):** lawful spaces-4 `.razor` probe → Ctrl+K,D in VS → **no-op** (stock VS output is lawful by construction, ruling #13). Re-run on VS major updates; if #4406 ever lands upstream, re-open the tabs question. Named residual gap: no build-time razor enforcement exists.

**Housekeeping:** delete the stray `.editorconfig` one level above the workspace root (POC deviation #4) — machine-local config above the repo is a reproducibility hazard.

## 12. Deferred

| Item | Disposition |
|---|---|
| YGG analyzers (§9 bench) | Primitives analyzer session |
| `dotnet format` as CI formatting gate | CI pipeline session |
| Razor build-time formatting enforcement | Does not exist; revisit when tooling ships it |
| `Async`-suffix-for-public-library-surface | Only if ever wanted; analyzer-bench shape, not naming-rule shape |
| TaskTupleAwaiter stack-table entry | Primitives/async session |
