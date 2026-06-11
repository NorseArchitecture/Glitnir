# Build Enforcement POC — Findings

**Date executed:** 2026-06-05
**SDK:** 11.0.100-preview.4.26230.115 (resolved via repo `global.json`, `rollForward: latestFeature`)
**Harness result:** PASS — `Verify-Enforcement.ps1` exit 0, all assertions green (two full runs: pre- and post-hardening)

## Verdicts

| Claim | Verdict |
|---|---|
| Chain import (`GetPathOfFileAbove`) propagates root law through two hops | **Confirmed** — `tests/` probe evaluates `AnalysisLevelSecurity=latest-All` |
| `AnalysisLevel<Category>` knobs escalate per category at root, tree-wide | **Confirmed** — all four categories land and fire (canary ledger below) |
| `TreatWarningsAsErrors` ratchets analyzer + compiler diagnostics | **Confirmed** — CS0219/CS8618 and all CA canaries fire as `error` |
| `NoWarn` accumulation suppresses CS1591 without weakening the rest | **Confirmed** — undocumented publics compile clean in `tests/`/`benchmarks/`; CA2200 still fires there |
| `InternalsVisibleTo "$(AssemblyName).Tests"` opens for the tests probe | **Confirmed** — `internal` member resolves across the boundary; CS0122 otherwise |
| Severed floors inherit nothing (`poc/`, `tests/smoke/` two-level nesting) | **Confirmed** — `AnalysisLevelSecurity` empty, ratchet off, CS0219 demoted to plain warning |
| Artifacts layout consolidates | **Confirmed with correction** — requires explicit `ArtifactsPath` pin at the law (deviation #5) |
| Smoke gate shape (build + run + exit 0) works under `PublishAot=true` | **Confirmed** — clean build, zero IL2xxx/IL3xxx, binary exits 0 |

## Canary Ledger

| ID | Category | Tier | Fired as error? |
|---|---|---|---|
| CA5394 | Security | latest-All-only | Yes |
| CA1810 | Performance | latest-All-only | Yes (static **field** required — deviation #6) |
| CA2007 | Reliability | latest-All-only | Yes |
| CA2201 | Usage | latest-All-only | Yes |
| CA2200 | Usage | latest-Recommended baseline | Yes — in all three governed layers |
| CS0219 | Compiler | ratchet | Yes |
| CS8618 | Compiler (Nullable) | ratchet | Yes (joined the set via deviation #7) |
| CS1591 | Docs | ratchet | Yes — via isolated `EnableDocCanaries` switch (deviation #7) |
| IDE0161 | Style (namespace) | EnforceCodeStyleInBuild + ratchet | Yes — block-scoped namespace |
| IDE0055 | Style (formatting) | EnforceCodeStyleInBuild + ratchet | Yes — space-indented line |
| IDE0007 | Style (var) | EnforceCodeStyleInBuild + ratchet | Yes — explicit type where var is law |
| IDE0008 | Style (var) | EnforceCodeStyleInBuild + ratchet | Yes — var on construction |
| IDE0090 | Style (new) | EnforceCodeStyleInBuild + ratchet | Yes — `new T()` where `new()` is law |
| IDE0040 | Style (modifiers) | EnforceCodeStyleInBuild + ratchet | Yes — redundant `private` |
| IDE1006 | Style (naming) | EnforceCodeStyleInBuild + ratchet | Yes — `m_`-prefixed field |
| IDE0005 | Style (usings) | EnforceCodeStyleInBuild + ratchet | Yes — gratuitous `using System.Text;` |
| IDE0305 | Style (collection) | EnforceCodeStyleInBuild + ratchet | Yes — fluent `.ToList()` with explicit target |
| CA1727 | Usage (logging) | targeted editorconfig severity | Yes — lowercase log placeholder |
| CA1848 | Performance (logging) | latest-All-only (no editorconfig line) | Yes — LoggerMessage delegates |
| CA2254 | Usage (logging) | latest-All-only (no editorconfig line) | Yes — interpolated log template |
| CA1852 | Performance (sealing) | latest-All + `ignore_internalsvisibleto` option | Yes — fires on `UnsealedCanary` once the option overrides IVT self-disable (deviation #12) |

## Deviations and Surprises

1. **CI condition parenthesized.** `('$(GITHUB_ACTIONS)' == 'true') or ('$(TF_BUILD)' == 'true')` — review-flagged MSB4130 risk (never empirically observed; parentheses cost nothing and match the docs' intent). The two-PropertyGroup form from the official `ContinuousIntegrationBuild` docs is the sanctioned alternative.
2. **`EnforceCodeStyleInBuild=true` makes a root `.editorconfig` a build prerequisite, not Phase-2 pedantry.** With none present, the Roslyn formatter's stock defaults (space indentation, CRLF, block-scoped namespaces) turned lawful tab-indented file-scoped code into IDE0055/IDE0160 build errors. Minimal seed forced into existence: `indent_style = tab`, `end_of_line = lf`, `csharp_style_namespace_declarations = file_scoped:error`. **The seed must ship in the same commit as the law, always.**
3. **Style-category escalation is real and uses stock options.** `latest-Recommended` + `EnforceCodeStyleInBuild` activates Style-tier IDE rules as build warnings → the ratchet makes them errors, judged against Roslyn's *defaults*: IDE0022 (prefers block bodies), IDE0059 (co-fires with CS0219), IDE0005 (unnecessary using; nested namespaces make parent usings redundant). Phase 2 must declare house options for the entire activated set — until then, stock preferences are silently the law. (An early attribution of IDE0022 to the parent-directory `.editorconfig` was wrong: `root = true` isolation holds, proven by tab-indented code passing while the parent file demands spaces.)
4. **A stray `.editorconfig` exists OUTSIDE the repo**, one level above the workspace root (the old space-indented template, itself `root = true`). Inert for the replica (see #3) and for any tree with a root-=true `.editorconfig`, but machine-local config sitting above a repo is a reproducibility hazard for any C# in the repo until the real root `.editorconfig` lands. Recommend deleting it.
5. **`UseArtifactsOutput` resolves its default root against the *nearest* `Directory.Build.props`,** not the file that declared it — each chained subtree grew its own `artifacts/`. The law must pin `<ArtifactsPath>$(MSBuildThisFileDirectory)artifacts</ArtifactsPath>`. Severed floors deliberately omit the pin (their litter lands beside their own props — intended).
6. **CA1810 requires a static field.** The original canary used a static auto-property; the rule never fired. `internal static readonly int Seed;` assigned in the static constructor fires correctly.
7. **CS1591 cannot coexist with other compiler errors** — the compiler skips the XML-doc diagnostics pass when the compilation already has errors. The doc canary moved to an isolated `EnableDocCanaries` toggle (fires alone, exactly one error); CS8618 (uninitialized non-nullable under `Nullable=enable`) joined the main canary set in its place.
8. **Canary toggles propagate into `ProjectReference` dependencies** (global properties flow down), killing the dependency build before the referencing probe compiles. `UndefineProperties="EnableCanaries;EnableDocCanaries"` on the ProjectReference strips them.
9. **Evaluation-time property trivia:** `IsPackable` defaults to `true` at evaluation (the tests layer's `false` is a real delta); accumulated `NoWarn` evaluates to `;CS1591` (leading separator is benign); `PublishAot=true` auto-appends `IL2121` to `NoWarn`; `NETSDK1057` (preview SDK) is a *message*, not a warning — it does not trip the ratchet.
10. **Incremental builds suppress warning re-emission** (cached outputs report 0 warnings). Irrelevant to the harness (exit codes + canary toggles force recompiles), but anyone eyeballing severed-tree warnings needs `--no-incremental`.
11. **Harness hardening:** `dotnet msbuild -getProperty` JSON is extracted by regex before parsing, guarding against SDK banners/workload nags polluting stdout in CI environments.
12. **CA1852 self-disables in any assembly that grants `InternalsVisibleTo` — and every `src` assembly does (§2.3) — until the dedicated option overrides it.** With IVT present, internal types are externally derivable by the friend assembly, so CA1852 cannot prove "no subtypes" and stays silent assembly-wide by default. This is documented Roslyn behavior, not error suppression and not a bug. Proven both directions in a minimal isolation project: an unsealed internal type fires CA1852 as `error` under `AnalysisLevelPerformance=latest-All` with **no editorconfig line** (confirms the Performance escalation reaches it) and **stops firing the instant** `[assembly: InternalsVisibleTo("…")]` is added (build then succeeds clean). A compile-define toggle (the `EnableDocCanaries`/`EnableStyleCanaries` isolation pattern) **cannot** rescue it, because IVT is assembly-scoped, not file-scoped. **Resolution:** CA1852 exposes a dedicated rule option, `dotnet_code_quality.CA1852.ignore_internalsvisibleto = true` (Microsoft Learn, CA1852 → "Configure code to analyze" → "Ignore InternalsVisibleTo attribute"; available since .NET 8), which tells the rule to run despite IVT. The law sets it in `.editorconfig` — tests *consume* internals, they do not *derive* from them, so ignoring IVT matches the rule's intent. With the option set, `UnsealedCanary` fires CA1852 as `error` directly inside the IVT-bearing Probe; the src-tier `Assert-CanaryBuild` asserts it. **Seeding implication:** carry the `ignore_internalsvisibleto` line into the real root `.editorconfig` alongside the §2.3 IVT grant — without it, CA1852 is silently inert platform-wide in every assembly that has a `.Tests` friend (i.e. all of them).
13. **Phase 2 style law fires cleanly under the ratchet.** All nine targeted IDE rules (IDE0005/0007/0008/0040/0055/0090/0161/0305/1006) and the three logging CAs (CA1727 targeted, CA1848/CA2254 via latest-All with no editorconfig line) fire as `error` against the canary file. The file leaves the compilation entirely when `EnableCanaries` is off (via `<Compile Remove>`, not `#if`) — formatting/style analysis of `#if`'d-out text is unreliable, so the gate is compilation membership, not preprocessor exclusion. Clean builds (canaries off) stay green. Co-traveling diagnostics (IDE0017, CA1812, etc.) appear in the canaried output and are harmless — only the asserted IDs are checked.
14. **`Microsoft.Extensions.Logging.Abstractions` trips NU1510 under the ratchet — because the `PackageReference` was unnecessary.** On .NET 10+, package pruning flags this package as subsumed by the targeting pack and emits NU1510 ("will not be pruned… likely unnecessary"); `TreatWarningsAsErrors` turns that into a **restore** failure (before compile, so every downstream canary reports "did not fire"). NU1510 is exactly the SDK's signal that the package is framework-provided. **Resolution:** dropping *both* the `PackageReference` and the `<RestoreEnablePackagePruning>false</RestoreEnablePackagePruning>` workaround, the canaried Probe still compiles against the real `ILogger` surface (the logging CAs — CA1727/CA1848/CA2254/CA1873 — fire as expected; no CS0246/CS0234). The abstractions arrive transitively from the SDK targeting pack; the explicit reference was redundant. Seeding note: where a real assembly appears to need the logging abstractions, drop the package reference rather than disabling pruning — under the platform SDKs they arrive framework-provided. (The pruning-disable workaround is retained nowhere.)
15. **PowerShell `switch -Wildcard` has no implicit break.** The harness's new `'!~*'` (not-contains) assertion prefix matches BOTH the `'!~*'` and `'!*'` patterns; without explicit `break` statements the LAST matching branch wins and the new branch silently never routes. Caught by a deliberate routing proof (temporarily asserting `'!~IL2121'` and expecting failure — it passed instead). Fix: explicit `break` on the wildcard branches; first-match-wins is now real. Lesson: ordering alone is insufficient in PowerShell switch semantics.
16. **`dotnet sln add` (slnx, preview SDK) errors instead of no-oping on duplicates reached via `ProjectReference`.** Adding `tests/Glitnir.Probe.Tests` (which references `src/Glitnir.Probe`) failed with "Solution folder 'src' already contains a project with the filename 'Glitnir.Probe.csproj'" and rolled back the whole add transactionally (batch adds lost everything after the collision). Workaround: hand-edit the slnx XML — trivially safe precisely because slnx is readable XML (a point in slnx's favor). `dotnet sln list` and a full solution build verified the result.

17. **The VS Razor formatter is editorconfig-blind, upstream, permanently (for now) — razor re-ruled to spaces-4.** The VS test drive reproduced the historic wound on the first try: Ctrl+K,D converted the tab-indented `.razor` probe to spaces despite the explicit `[*.{razor,cshtml}]` tabs section. Root cause is not local: generic editorconfig properties (`indent_style`/`indent_size`/`tab_width`) for Razor are blocked at the VS editor platform (dotnet/razor #4406 — open since 2021, backlogged, no owner; #7972 closed as its duplicate; #12223 shows razor formatting trouble persisting into VS 2026). The formatter reads Tools → Options → Text Editor → Razor (ASP.NET Core) → Tabs, default spaces-4, on every VS version — there is no pinnable floor. **Ruling (Buvy, 2026-06-06): don't fight the ecosystem** — razor joins the spaces exceptions at the VS default (spaces, 4), so stock VS emits lawful files with zero machine config; the per-machine "Keep tabs" alternative was rejected as the `core.autocrlf` failure mode reborn. Probe renamed `TabProbe.razor` → `FormatProbe.razor` (it proves format-is-a-no-op now, not tab survival). Seeding implication: the spaces-4 razor section (with the #4406 citation) carries to the real root `.editorconfig`; re-open the tabs question only if #4406 lands upstream.

18. **Naming-rule build enforcement is SDK-version-dependent — surfaced by the carrier-platform port (.NET 10).** On Glitnir's .NET 11 preview Roslyn, `dotnet_naming_rule.*.severity = error` reaches the command-line build (the IDE1006 canary fails the build — proven). On the carrier's stack (.NET 10), the same config left the build clean despite live IDE1006 hits: the per-rule severity path is honored in the IDE but not at `dotnet build` on the older Roslyn (roslyn #49439 — long-standing CLI-vs-IDE naming-enforcement gap). Not a tier difference (the config is identical and says error) — a compiler-version difference in which severity key the build reads. **Fix:** added `dotnet_diagnostic.IDE1006.severity = error` (the diagnostic-ID path, which the build reads reliably — same lesson as preferring `dotnet_diagnostic` over the `option:severity` suffix). Harness re-verified green on .NET 11 (no regression; IDE1006 still fires). **Carrier-side re-test CONFIRMED (2026-06-06):** with the diagnostic line, `dotnet build` on .NET 10 forces the error (`FileReader.cs(40,34): error IDE1006: Missing prefix: '_'`) — naming is now build-law on both SDKs. The diagnostic-ID severity is therefore **required, not belt-and-suspenders**: it is the only path that build-enforces naming on the older Roslyn. Permanent template law; carries to the real root and to every stack regardless of SDK.

## Seeding Recommendation

What transfers to the real Glitnir root (and later the Yggdrasil meta-repo root), from the replica's proven state:

- **`Directory.Build.props` — verbatim** (`poc/build/Directory.Build.props`), including the `ArtifactsPath` pin and parenthesized CI condition.
- **Root `.editorconfig` — the three-line seed ships in the same commit as the law** (deviation #2). Phase 2 curates the full pedantic pass on top; prefer `dotnet_diagnostic.IDExxxx.severity` over the `option:severity` suffix at scale (review finding — the suffix form's behavior is tied to the rule being enabled, fragile across SDK drift).
- **Governed layer deltas — verbatim shapes:** `src/` (chain + IVT), `tests/` (chain + `NoWarn` CS1591 + `IsPackable=false`), `benchmarks/` (chain + `NoWarn` CS1591).
- **Severed floors — verbatim shapes:** `poc/` (Glitnir-only) and `tests/smoke/` (+`PublishAot`).
- **`Directory.Build.targets` — empty placeholder**, reserved for the `UseProjectReferences` session. Note: targets-file resolution is independent of props severance — severed trees still inherit the nearest targets file; tomorrow's session must account for that.
- **Harness — promote to `scripts/Verify-Enforcement.ps1`** with paths rebased to the real tree; keep the canary IDs per the ledger; runs in CI as a build-law regression gate. Before promotion, tighten the one deliberately-weak assertion: the smoke floor's `NoWarn = '!~CS1591'` routes to the not-equal branch (weaker than not-contains); add a `'!~*'` not-contains case to `Assert-Properties`.
- **Probes — carry over as-is** (`Glitnir.Probe`, `.Tests`, `.Probe.Benchmarks`, `.Probe.Severed`, `.Probe.Smoke`), solution-excluded, built only by the harness.

- **Root `.gitignore` + `.gitattributes` — currently absent from the repo entirely** (the templates were cleared pre-session; `poc/build/.gitignore` covers only the replica). The seeding pass must bring a real root `.gitignore` (`artifacts/` at minimum) and a `.gitattributes` (`* text=auto` + `*.ps1/.cs/.props/.targets text eol=lf` or Buvy's ruling) — every LF→CRLF warning this session traces to their absence. These are **commit #1** artifacts, never a fast-follow — the Brownfield Rollout Post-Mortem (end of this file) is the receipts for what retrofitting them costs.

Sequencing: Phase 2 (`.editorconfig` curation) next, on top of this proven substrate; then the `UseProjectReferences` session (owns the targets file); real-tree seeding can land with either, in whichever order Buvy rules.

---

# Phase 2 Appendix — .editorconfig Curation (executed 2026-06-06)

**SDK:** 11.0.100-preview.4.26230.115 (unchanged from Phase 1)
**Harness result:** PASS — exit 0, all assertions green (multiple full runs across the task sequence)

## Verdicts

| Claim | Verdict |
|---|---|
| Full style law tolerates lawful probe code unchanged | **Confirmed** — zero probe-code fixes needed across all governed trees |
| Every Phase 2 canary ID fires as error in one canaried build | **Confirmed** — IDE0005/0007/0008/0040/0055/0090/0161/0305, IDE1006, CA1727/CA1848/CA2254/CA1852 |
| Silent tier stays silent (inverse canary compiles clean) | **Confirmed** — IDE0046 bait in every clean build, never fires |
| CA1848/CA2254 reachable via category knobs alone (no editorconfig lines) | **Confirmed** — fire as errors with zero editorconfig mention |
| CA1852 reachable via category knobs | **Confirmed with correction** — requires `ignore_internalsvisibleto = true` (deviation #12); the platform IVT grant otherwise blinds it assembly-wide |
| CA1727 requires the targeted editorconfig severity | **Confirmed** — Naming category sits outside the escalated knobs |
| Razor project builds clean under full src/ law | **Confirmed** — no CS1591 on generated component classes (auto-generated provenance respected); Components projects need no carve-out |
| JSON source-gen switch lands in src/ and only src/ | **Confirmed** — present in src probe, empty in tests probe |

## VS Test Drive Results

- **Step 1 (razor Ctrl+K,D): FAILED as tab-law, ruled closed as spaces-law.** The formatter converted tabs → spaces on first try; investigation traced it upstream (deviation #17) and Buvy ruled razor a spaces-4 exception (don't fight the ecosystem). The checklist step is rewritten; re-run under the new law expects a no-op.
- **Tier refinements from live driving (spec ruling #14, refined same day):** the ternary-as-suggestion behavior confirmed pleasant; braces (IDE0011) demoted to silent with value `when_multiline` (single-line control statements may omit braces, "remove braces" suggested). Expression bodies re-tiered: enforced (error) on everything except functions — properties/indexers/accessors `true`, single-line constructors/operators `when_on_single_line` — while methods/local functions (IDE0022/IDE0061) sit at **suggestion**: visible IDE nudge, zero build impact (info-level; the ratchet only escalates warning+). Introduces the third severity tier (error / suggestion / silent).
- **Accidental discovery — dead-code detection comes free:** stripping `public` off the inverse canary during the drive (omit_if_default makes bare members private) produced `error IDE0051: unused private member` — under least-accessibility + the ratchet, a member nobody calls fails the build the moment it stops claiming to be public surface. Pit of success, demonstrated live. The canary now documents why its `public` is load-bearing.
- **The var law met Roslyn's apparent-bucket heuristic (spec ruling #15):** IDE0008 fired on `var culture = CultureInfo.GetCultureInfo(cultureTag);` — Roslyn classifies factory-style invocations (`TypeName.Method(...)` returning `TypeName`) as "type is apparent," same bucket as `new T()`. Re-ruled: all three var buckets true (factories get var); IDE0008 thereby unreachable (canary removed, severity frozen); `T x = new();` still enforced via IDE0090 — with a canary-proven nuance: **on locals IDE0090 defers to use-var** (its enforceable territory is var-impossible declarations: fields, property initializers; the canary moved to a field). The now-legal `var x = new T();` goes to the analyzer bench (no-var-on-construction, bench entry #4). Separately, the test drive's `public`-stripping pokes (JudgmentCitizen, UndocumentedDoorOpener) both produced IDE0051 unused-private-member build errors — dead-code detection via omit_if_default + ratchet, confirmed twice; the door opener was restored verbatim (its undocumented-public shape IS its probe duty).
- Remaining steps: (filled by Buvy's session — VS version pinned: _____)

---

# Brownfield Rollout Post-Mortem — Carrier-Platform Port (2026-06-06)

These conventions were ported to the fronting carrier's platform repo; the findings below are from that rollout.

The template's first contact with living history: existing clones, in-flight branches, years of blame. **Greenfield moral up front: every cost below is the price of retrofitting law onto a repo that already has history — all of it evaporates when the law ships in commit #1, before the first `.cs` file exists** (deviation #2's "same commit as the law," extended to "first commit of the repo"). Yggdrasil submodules are born this way; this section is the warning to any future repo that isn't.

But the playbook is not disposable knowledge. Every future amendment to formatting law replays this in miniature — this session alone re-ruled tab width (#7), razor (#13), braces (#14), and var (#15) — so it stands as the **law-change playbook**, greenfield or not.

1. **EOL merge conflicts → `git config --global merge.renormalize true`** — per teammate, before the law PRs merge. Makes git run the clean filters on all three merge stages, so a line differing only by CRLF/LF stops being a conflict at all; eliminates most renormalization-commit noise. Local-only config (cannot be committed) — it ships in the merge announcement. Greenfield carry-over: a free onboarding line, inert until a `.gitattributes` rule ever changes, pre-deployed when it does.
2. **Stale clones keep immortal CRLF.** Pulling the renormalized main does NOT rewrite stat-clean working files (1,003 in the proving clone — git skips them); the victim's next build sprays IDE0055 errors that look like the law broke their code. Antidote, per repo, after pulling: `git rm --cached -r . ; git reset --hard` — `rm --cached` invalidates the stat cache so `reset --hard` actually rewrites every file through the smudge filter. Ships in the announcement next to the renormalize line.
3. **In-flight branches:** `git rebase origin/main -X ignore-all-space` (auto-resolves whitespace-only hunks, taking the branch's side) → `dotnet format <Repo>.slnx --severity error` (**mandatory** — ignore-all-space quietly reintroduces old formatting on touched lines) → `dotnet build <Repo>.slnx --no-incremental` (incremental hides residue — deviation #10, reconfirmed in the field).
4. **Merge fast, in the documented order** — submodules in any order, meta-root last. Drift compounds daily; consider asking the team to merge or pause branches for the window.
5. **Identifier renames** (~40 in the burn-down: `_userId`, `_stringToGuid`, `Limit`, …) rarely merge-conflict but compile-break any branch touching those files. The build catches it; fixes are mechanical; one announcement line prevents the panic.
6. **`.git-blame-ignore-revs` lands post-merge, not before** — squash merges mean pre-merge local SHAs are not main's SHAs. One file per repo listing the format-commit SHAs + `git config blame.ignoreRevsFile .git-blame-ignore-revs`. Greenfield carry-over: the standing mitigation for any future mass-format amendment. (Generation pending on the carrier side once final SHAs exist.)
7. **Ruling #16 validated in anger:** in-flight EF migrations were exempted automatically by the `[**/Migrations/**.cs]` `generated_code` section — a whole conflict class pre-solved before the rollout reached it. Field evidence, ahead of the §11 inverse canary.
8. **The genuinely unavoidable conflicts:** feature branches with semantic edits on the same lines the style burn-down rewrote. The list is short and lives in the rollout log's hand-fix inventory — eyeball open PRs against it before merging.
