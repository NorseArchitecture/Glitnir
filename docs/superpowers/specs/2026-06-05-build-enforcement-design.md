# Build Enforcement Design — MSBuild Law, Layering, and the Probe Harness

**Date:** 2026-06-05
**Status:** Approved design, awaiting plan greenlight
**Scope:** Root-down MSBuild enforcement for Glitnir (and, by inheritance, the future Norse meta-repo root). Phase 1 of the build-enforcement session (spec-reconciliation punch item 4.2). Phase 2 (`.editorconfig` curation) begins only after this phase's harness is green.

---

## 1. Problem and Goals

The repo starts from bare `dotnet new editorconfig` / `buildprops` / `buildtargets` templates, with `global.json` locked to the .NET 11 preview SDK. Before a single in-house analyzer is authored, the developer IDE and build must behave correctly on formatting, style, and code enforcement — landed in the right file at the right level.

Goals, in Maslow order:

1. **Strict MSBuild law at the root**, inherited by everything governed, with zero override chains for developers to chase.
2. **A verification harness** proving settings *land* (properties evaluate as declared) and *behave* (diagnostics fire as errors) — the props become tested software.
3. **Phase 2 bridge**: `.editorconfig` severities must be born build-enforced when curated later — no second wiring step.

Non-goals (each deferred to its own session): Central Package Management, `UseProjectReferences` cross-repo switching (owns `Directory.Build.targets`, next session), CI pipeline definition, `.editorconfig` curation (Phase 2), BenchmarkDotNet posture.

## 2. Tree Taxonomy

Three tiers. The decision rule for any new folder: does the root law apply?

| Tier | Trees | `Directory.Build.props` behavior |
|---|---|---|
| **Root** | `/` | Declares the law |
| **Governed** | `src/`, `tests/`, `benchmarks/` | First line chains to the file above; adds only a delta |
| **Severed** | `poc/`, `tests/smoke/` | Standalone; no chain import; re-declares a minimal floor |

**The chaining backbone.** MSBuild auto-imports only the *nearest* `Directory.Build.props` walking up from each project. A subtree props file therefore severs root inheritance unless it explicitly re-imports the file above:

```xml
<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)..'))" />
```

In governed trees this import is mandatory and load-bearing. In severed trees its *absence* is the isolation mechanism — deliberate, not accidental.

**Severed-tree rationale:**

- `poc/` — ephemeral proving ground, Glitnir-only (operational repos never carry a `poc/`), never under CI. POCs get just enough platform floor to compile and none of the law.
- `tests/smoke/` — the AOT proving ground, and operational (it ships to the meta-repo era). Not lawless by indifference: the only law that matters there is "publishes AOT and executes without blowing up." Its CI gate is behavioral — `dotnet publish` + run the binary + exit 0/1 — never analytical and never artifact-producing. The tree is cut loose entirely when the platform reaches the 100%-AOT end state (performance posture spec's blocker register tracks the distance).

## 3. Root `Directory.Build.props` — the Law

```xml
<Project>
	<PropertyGroup Label="Platform">
		<TargetFramework>net11.0</TargetFramework>
		<LangVersion>preview</LangVersion>
		<Nullable>enable</Nullable>
		<ImplicitUsings>enable</ImplicitUsings>
	</PropertyGroup>

	<PropertyGroup Label="Output">
		<UseArtifactsOutput>true</UseArtifactsOutput>
		<ArtifactsPath>$(MSBuildThisFileDirectory)artifacts</ArtifactsPath>
	</PropertyGroup>

	<PropertyGroup Label="Enforcement">
		<AnalysisLevel>latest-Recommended</AnalysisLevel>
		<AnalysisLevelSecurity>latest-All</AnalysisLevelSecurity>
		<AnalysisLevelPerformance>latest-All</AnalysisLevelPerformance>
		<AnalysisLevelReliability>latest-All</AnalysisLevelReliability>
		<AnalysisLevelUsage>latest-All</AnalysisLevelUsage>
		<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
		<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
		<MSBuildTreatWarningsAsErrors>true</MSBuildTreatWarningsAsErrors>
		<GenerateDocumentationFile>true</GenerateDocumentationFile>
	</PropertyGroup>

	<PropertyGroup Label="Restore">
		<NuGetAudit>true</NuGetAudit>
		<NuGetAuditMode>all</NuGetAuditMode>
		<NuGetAuditLevel>low</NuGetAuditLevel>
	</PropertyGroup>

	<PropertyGroup Label="CI" Condition="('$(GITHUB_ACTIONS)' == 'true') or ('$(TF_BUILD)' == 'true')">
		<ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>
	</PropertyGroup>
</Project>
```

Reasoning on the non-obvious lines:

- **`TargetFramework` at root.** .NET 11 is locked platform-wide (2026-06-04 decision; no dual-targeting). Every csproj inherits it and says nothing; a project that genuinely needs `TargetFrameworks` (plural) overrides locally — a visible, deliberate escalation, same philosophy as `omit_if_default`.
- **`LangVersion=preview`.** Required for C# 15 (unions) on the preview SDK. Re-pin trigger: flips to `latest` when .NET 11 GAs (~Nov 2026), in the same pass as the RC1 re-pin.
- **`AnalysisLevel` strategy.** `latest-Recommended` baseline because `latest-All` falsely flags new language constructs (observed: C# extension blocks generating compiler errors). The per-category properties (`AnalysisLevel<Category>`) carry the escalation to `latest-All` for Security, Performance, Reliability, and Usage — declared once, at root, tree-wide. No subtree opt-in layer exists or is needed. Values are case-insensitive; the casing here is house style.
- **`TreatWarningsAsErrors=true`, all configurations, always.** Build time, not review time. `CodeAnalysisTreatWarningsAsErrors` follows it by default, so analyzer diagnostics ratchet too. No Debug-config softening: inner loop and CI see identical law.
- **`MSBuildTreatWarningsAsErrors`.** Closes the third front: MSBuild *engine* warnings (MSB-prefixed — double-writes, import oddities) become errors, not just C# and analyzer output.
- **`EnforceCodeStyleInBuild=true`.** The Phase 2 bridge: IDExxxx severities from `.editorconfig` participate in the build, not just IDE squiggles. Every severity curated in Phase 2 is born build-enforced. POC finding: this property makes a root `.editorconfig` a *build prerequisite*, not deferred pedantry — with none present, the Roslyn formatter's stock defaults (space indentation, CRLF, block-scoped namespaces) turn lawful tab-indented file-scoped code into IDE0055/IDE0160 errors. A minimal seed (`indent_style = tab`, `end_of_line = lf`, `csharp_style_namespace_declarations = file_scoped:error`) ships with the law; Phase 2 curates the rest.
- **`GenerateDocumentationFile=true`.** With warnings ratcheted, CS1591 makes undocumented `public` surface a build error. Synergy with least-accessibility (§2.3): `internal`-by-default code is exempt; *going public costs documentation*, compiler-enforced. Governed non-`src/` trees suppress CS1591 (their types stay internal-or-irrelevant); severed trees never generate docs, so CS1591 cannot exist there.
- **NuGetAudit maxed.** `all` audits direct and transitive packages; `low` fails on the lowest-severity advisory. With `TreatWarningsAsErrors` global, NU1901–NU1904 become restore errors: a vulnerable transitive dependency stops the build. Advisory data comes from nuget.org's feed.
- **CI detection covers both candidate pipelines.** `GITHUB_ACTIONS` (GitHub Actions) or `TF_BUILD` (Azure DevOps) — whichever pipeline wins later, the law already recognizes it. `ContinuousIntegrationBuild` stays off locally on purpose: it normalizes embedded source paths for determinism, which would break local debugger path mapping if always-on.
- **`UseArtifactsOutput`.** All `bin`/`obj`/publish output consolidates under one `artifacts/` root. Local-disk layout only — no CI or publishing implication. POC finding: the SDK resolves the default artifacts root against the *nearest* `Directory.Build.props`, so chained subtrees each grow their own `artifacts/` — the law therefore pins `ArtifactsPath` to the declaring file's directory explicitly. Severed floors deliberately omit the pin; their litter lands beside their own props file.

## 4. Root `Directory.Build.targets`

Template hello-world target deleted. The file remains, clean, with a comment reserving it for the `UseProjectReferences` cross-repo switching session (next session, own lifecycle). Nothing else moves in tonight. No subtree targets files exist until one earns its existence.

## 5. Governed Layer Deltas

Each governed props file: chain import first, then only its delta.

**`src/Directory.Build.props`** — the sanctioned internals door (CLAUDE.md §2.3):

```xml
	<PropertyGroup>
		<JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>
	</PropertyGroup>

	<ItemGroup>
		<InternalsVisibleTo Include="$(AssemblyName).Tests" />
	</ItemGroup>
```

The JSON source-gen switch is a `src/`-only delta by ruling (2026-06-06): tests and benchmarks reflect-serialize freely; only working software is held to the source-gen posture.

**`tests/Directory.Build.props`** — the noise ledger:

```xml
	<PropertyGroup>
		<NoWarn>$(NoWarn);CS1591</NoWarn>
		<IsPackable>false</IsPackable>
	</PropertyGroup>
```

**`benchmarks/Directory.Build.props`** — chain + `CS1591` NoWarn, same shape. BenchmarkDotNet-specific posture (Release-config guard, etc.) waits for the session that introduces BDN.

NoWarn rules: `CS1591` is the only suppression seeded up front. Everything else enters evidence-first — when the probe or real code proves a rule is noise. Always accumulate (`$(NoWarn);XXXX`), never replace; replacement silently wipes upstream suppressions.

## 6. Severed Tree Floors

**`poc/Directory.Build.props`:**

```xml
<Project>
	<PropertyGroup>
		<TargetFramework>net11.0</TargetFramework>
		<LangVersion>preview</LangVersion>
		<Nullable>enable</Nullable>
		<ImplicitUsings>enable</ImplicitUsings>
		<UseArtifactsOutput>true</UseArtifactsOutput>
	</PropertyGroup>
</Project>
```

No analysis escalation, no warnings-as-errors, no doc generation — SDK defaults rule. `Nullable` stays because it is semantics, not ceremony: a POC proving a platform design under different nullability than the platform proves less.

**`tests/smoke/Directory.Build.props`:** same floor plus `<PublishAot>true</PublishAot>` — the tree's entire reason to exist. IL2xxx/IL3xxx trim warnings stay warnings deliberately: third-party reflection noise is exactly what the blocker register tracks, and hard-failing on it would gate on dependencies we don't control. The signal is *runs or doesn't run*, exit 0/1.

Severed trees re-declare `UseArtifactsOutput`; their litter lands at `poc/artifacts/` and `tests/smoke/artifacts/`. The `.gitignore` must cover `artifacts/` at any depth (verify during implementation).

## 7. The Enforcement Probe Harness

The props are tested software. Three solution-excluded probe projects, one per governed tree, plus a severance witness:

| Project | Proves |
|---|---|
| `src/Glitnir.Probe/` | Root law lands and fires in `src/`; publics require docs; clean code builds clean |
| `tests/Glitnir.Probe.Tests/` | Chain survives a second hop; `CS1591` NoWarn delta works; `InternalsVisibleTo` door opens (touches an `internal` member of `Glitnir.Probe`) |
| `benchmarks/Glitnir.Probe.Benchmarks/` | Chain + `CS1591` delta for the benchmarks layer |
| `poc/Glitnir.Probe.Severed/` | Code that would be a build error under the law builds clean — severance is real |

`tests/smoke/` gets its props file now but no project: the first smoke project arrives with the first AOT-bearing platform code. An empty proving ground proves nothing.

**Canary mechanism.** Violations live behind `#if CANARY`; the normal build of every probe is green. Each probe csproj appends the constant conditionally:

```xml
	<PropertyGroup>
		<DefineConstants Condition="'$(EnableCanaries)' == 'true'">$(DefineConstants);CANARY</DefineConstants>
	</PropertyGroup>
```

The harness flips it with `-p:EnableCanaries=true`. Deliberately not `-p:DefineConstants=CANARY` from the CLI: a global property clobbers SDK-computed constants (`NET11_0`, `TRACE`, …); the csproj-append pattern dodges that.

**Canary selection criteria** (exact IDs pinned during implementation and verified empirically — preview SDK rule sets shift):

- One rule per escalated category (Security, Performance, Reliability, Usage) that is **latest-All-only** — a rule already in `latest-Recommended` proves nothing about the category knobs.
- One `latest-Recommended` baseline rule (proves the baseline fires).
- One plain compiler warning (proves `TreatWarningsAsErrors`).
- One undocumented `public` (proves CS1591) in `src/`; the **inverse** canary in `tests/` — undocumented public that must compile clean (proves the NoWarn delta behaves, not just lands).

**Harness:** `scripts/Verify-Enforcement.ps1`, exit 0/1, CI-runnable later. Per probe, three checks:

1. **Landing** — `dotnet build -getProperty:...` asserts effective values of `AnalysisLevel`, the four category levels, `TreatWarningsAsErrors`, `NoWarn`, `GenerateDocumentationFile` per layer (evaluation-only, fast).
2. **Clean build passes** — the law doesn't false-positive on lawful code.
3. **Canaried build fails correctly** — output parsed; each expected diagnostic ID must appear as `error`, not `warning`. A canary firing as a warning is a harness failure.

Every future props edit, SDK preview bump, or `.editorconfig` severity change re-runs the harness. The harness can grow IDExxxx canaries in Phase 2.

## 8. File Inventory

> **Execution note (2026-06-05):** Per directive, this design is proven first as a
> self-contained replica under `poc/build/` (see its `FINDINGS.md` for the verdict —
> harness green, eleven deviations/surprises recorded, all folded back into this spec's
> §3 and §6 where they changed the law). The real-tree files below are seeded from the
> replica in a follow-up pass.

| File | Action |
|---|---|
| `Directory.Build.props` (root) | Becomes the law (§3) |
| `Directory.Build.targets` (root) | Hello-world deleted; reserved for `UseProjectReferences` session (§4) |
| `src/Directory.Build.props` | Chain + `InternalsVisibleTo` (§5) |
| `tests/Directory.Build.props` | New — chain + `CS1591` NoWarn + `IsPackable=false` (§5) |
| `benchmarks/Directory.Build.props` | Chain + `CS1591` NoWarn (§5) |
| `poc/Directory.Build.props` | New — severed floor (§6) |
| `tests/smoke/Directory.Build.props` | New — severed floor + `PublishAot` (§6) |
| `src/Glitnir.Probe/` · `tests/Glitnir.Probe.Tests/` · `benchmarks/Glitnir.Probe.Benchmarks/` | New — probes (§7) |
| `poc/Glitnir.Probe.Severed/` | New — severance witness (§7) |
| `scripts/Verify-Enforcement.ps1` | New — harness (§7) |
| `.gitignore` | Verify `artifacts/` coverage at any depth |
| `.editorconfig` · `global.json` · `dotnet-tools.json` | Untouched |

## 9. Deferred Decisions (each its own session)

| Item | Disposition |
|---|---|
| Central Package Management (`Directory.Packages.props`) | Own session. Until then the probes pin their own (disposable) versions. |
| `UseProjectReferences` cross-repo switching | Own full lifecycle; owns root `Directory.Build.targets`. Next session. |
| CI pipeline (Actions vs. ADO) | Undecided; root law already detects both. |
| `.editorconfig` curation | Phase 2 of this session — starts only when the harness is green. Template file currently contradicts conventions (spaces vs. tabs) and is rewritten then, root-only. |
| BenchmarkDotNet posture | With the session that introduces BDN. |
| `LangVersion` re-pin (`preview` → `latest`) | At .NET 11 GA, with the RC1 re-pin pass. |

## 10. Decision Ledger (session record)

1. Category escalation via `AnalysisLevel<Category>` at root, tree-wide — no subtree opt-in layer.
2. Test noise suppressed via `<NoWarn>` accumulation in `tests/Directory.Build.props`, evidence-first.
3. CPM deferred.
4. `UseProjectReferences` switching deferred, own lifecycle.
5. Docs: `GenerateDocumentationFile=true` at root; CS1591 suppressed everywhere but `src/` — public in `src/` is working software and is documented.
6. Artifacts output layout adopted, governed and severed trees alike (local layout only; POC/smoke never produce CI artifacts; smoke's gate is exit 0/1).
7. `ImplicitUsings` enabled.
8. NuGetAudit maxed (`all` / `low`), errors via the global ratchet.
9. `ContinuousIntegrationBuild` on under either GitHub Actions or ADO.
10. `poc/` severed and Glitnir-only; `tests/smoke/` severed, operational, AOT-gated.
11. Probe-per-governed-tree harness, permanent.
