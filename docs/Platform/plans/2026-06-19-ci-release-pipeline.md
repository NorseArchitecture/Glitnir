# CI + Tag/Release Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: `superpowers:subagent-driven-development` — the default, not a recommendation among equals (`superpowers:executing-plans` is the narrow fallback for a separate session with human review checkpoints) — paired with `superpowers:test-driven-development`. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the shared reusable CI/release workflows in `NorseArchitecture/.github` and prove them end-to-end against Svartalfheim — the one realm with real, buildable code — producing a real green PR gate and a real published package as the "receipts" that justify spreading the pattern to every other realm later.

**Architecture:** Two reusable workflows (`workflow_call`) in the org's `.github` repo — `ci-build-test.yml` (restore/build/test) and `release-nuget.yml` (rebuild, CodeQL, SBOM, `dotnet pack`, push to GitHub Packages, GitHub Release). Svartalfheim gets two thin caller workflows referencing them, plus MinVer wired into its `Directory.Build.props` so a pushed `vX.Y.Z` tag is the only place a version number is ever typed.

**Tech Stack:** GitHub Actions (`workflow_call` reusable workflows), MinVer, `github/codeql-action`, `anchore/sbom-action`, GitHub Packages (NuGet), GitHub's existing "Law of the Aesir" repository ruleset.

## Global Constraints

- **Scope is Svartalfheim only.** Per spec `../Glitnir/docs/Platform/specs/2026-06-19-ci-release-pipeline-design.md` and explicit direction in-session: Svartalfheim ("the dwarven forge") is the sole proving ground this round. Bifrost and every other realm are out of scope — propagating the pattern elsewhere is Buvy's own follow-up, done once this plan's receipts (a green PR run, a published package) exist. Do not add caller workflows to any other repo as part of this plan.
- **No automatic git commits, pushes, merges, tags, or PR/release creation by the executor — ever, in any repo touched by this plan.** Every Norse Architecture realm's CLAUDE.md restates this; it is platform law, not a per-repo quirk. The executor (subagent or otherwise) edits files, runs `git add`, shows the diff, and **stops**. Buvy personally runs every `git commit`, `git push`, branch-protected merge, `git tag`, and `gh release` action in this plan. After he reports an action is done, the executor may resume using **read-only** `gh`/`git` commands (`gh pr checks`, `gh run view`, `gh ruleset list`, `git fetch`, `git log`) to verify and report results — never to mutate.
- **Branch is `master`, never `main`,** everywhere in this plan — confirmed against this org's actual repos.
- **SDK pinning:** every `actions/setup-dotnet` step uses `global-json-file: global.json` so the runner installs exactly what `global.json` declares (`11.0.100-`, `rollForward: latestFeature`, `allowPrerelease: true`) — never a hardcoded `dotnet-version`. Every such step also sets `dotnet-quality: "preview"` explicitly — `global.json`'s `allowPrerelease`/`rollForward` combination has long-standing, documented SDK-resolution flakiness (e.g. `dotnet/sdk#18272`, `dotnet/sdk#16418`), and an SDK that has never had a GA release (.NET 11, today) is exactly the case where relying on inference instead of an explicit quality channel is riskiest.
- **Warnings-as-errors and `NuGetAudit` are already enforced** via Svartalfheim's `Directory.Build.props` (`TreatWarningsAsErrors=true`, `WarningLevel=9999`) per the build-enforcement spec (2026-06-05). CI executes that law; this plan does not reconfigure it.
- **Tags are `vX.Y.Z`** (semver, `v`-prefixed) — the human-typed version is the audit-trail moment (design spec §2.5). MinVer is configured with `MinVerTagPrefix=v` to match.
- **The `.github` repo already exists** (`NorseArchitecture/.github`, created 2026-06-11) — it is **not** created in this plan, only added to. It is cloned to a sibling working directory, `../.github` relative to the Bifrost workspace root — **never** as a Bifrost submodule (Bifrost CLAUDE.md §4: only platform realms and the AppHost belong inside Bifrost).
- **Branch protection already exists.** The "Law of the Aesir" ruleset (`NorseArchitecture/.github/scripts/carve-the-laws.ps1`) is already active on Svartalfheim — PRs required, no force-push, no deletion, a required status check with `context: 'build'`. This plan does not invent new branch protection; it aligns that existing required-check name to whatever GitHub Actions actually reports once the real workflow exists (the script's own comment anticipates this: *"Adjust when workflows settle"*).
- **GitHub Packages is the NuGet feed:** `https://nuget.pkg.github.com/NorseArchitecture/index.json`, authenticated with the workflow's automatic `GITHUB_TOKEN` — no new secret is provisioned anywhere in this plan.
- **US English spelling** in every file, commit message, and PR/release title touched by this plan.
- **No packaging-metadata authoring beyond what `dotnet pack` needs to succeed, plus making `PackageId` explicit.** Svartalfheim's own CLAUDE.md already tracks "NuGet packaging metadata" as its own deferred increment (#3) — icon, license expression, tags, embedded README stay there, out of scope here. The one exception: `PackageId` is set explicitly to `Norse.$(MSBuildProjectName)` in `Directory.Build.props` (Task 3), mirroring `AssemblyName`/`RootNamespace` — it already resolves to that value by NuGet's default fallback, but the platform's convention is to state identity properties explicitly rather than lean on an implicit default. This also fixes the package identity in place for whenever the platform matures enough to publish to a public feed instead of GitHub Packages — same `PackageId`, just a different `--source`.

---

## File Structure

In `../.github` (sibling working clone of `NorseArchitecture/.github`):
- `.github/workflows/ci-build-test.yml` — new. Reusable `workflow_call` workflow: checkout, setup .NET from `global.json`, restore, build, test.
- `.github/workflows/release-nuget.yml` — new. Reusable `workflow_call` workflow: calls `ci-build-test.yml`, runs CodeQL, packs, generates an SBOM, pushes to GitHub Packages, creates the GitHub Release.
- `scripts/carve-the-laws.ps1` — modified. `required_status_checks` context updated from the placeholder `'build'` to the real check name GitHub reports for Svartalfheim's caller job.

In `Svartalfheim/` (existing Bifrost submodule):
- `Directory.Build.props` — modified. Adds the MinVer `PackageReference` and `MinVerTagPrefix`.
- `.github/workflows/ci.yml` — new. PR-gate caller (`uses: NorseArchitecture/.github/.github/workflows/ci-build-test.yml@master`).
- `.github/workflows/release.yml` — new. Tag-triggered caller (`uses: NorseArchitecture/.github/.github/workflows/release-nuget.yml@master`).

---

### Task 1: Author the PR-gate reusable workflow (`ci-build-test.yml`)

**Files:**
- Create: `../.github/.github/workflows/ci-build-test.yml` (path inside the sibling clone of `NorseArchitecture/.github`)

**Interfaces:**
- Produces: a `workflow_call` reusable workflow with one job, id `build`, consumed by Task 4's Svartalfheim caller and by Task 6's release workflow (Task 2).

- [ ] **Step 1: Clone the `.github` repo to a sibling working directory**

```bash
gh repo clone NorseArchitecture/.github ../.github
```

Run this from the Bifrost workspace root. Verify: `git -C ../.github remote -v` shows `origin https://github.com/NorseArchitecture/.github.git`.

- [ ] **Step 2: Write the reusable workflow**

Create `../.github/.github/workflows/ci-build-test.yml`:

```yaml
name: CI Build & Test

on:
  workflow_call:

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v7
        with:
          fetch-depth: 0

      - name: Setup .NET
        uses: actions/setup-dotnet@v5
        with:
          global-json-file: global.json
          dotnet-quality: "preview"

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore -c Release

      - name: Test
        run: dotnet test --no-build -c Release
```

`fetch-depth: 0` is required so MinVer (added in Task 3) can see tag history when this workflow runs as part of a release rebuild (Task 2/6) — a shallow checkout would make MinVer fall back to an imprecise default version even on a tagged commit.

- [ ] **Step 3: Stage and stop**

```bash
git -C ../.github add .github/workflows/ci-build-test.yml
git -C ../.github diff --cached
```

**STOP.** Show Buvy the diff. He runs the commit and push himself (this repo has no PR gate of its own yet — see Global Constraints — so this is a direct push to `master`, his call to make):

```bash
git -C ../.github commit -m "Add ci-build-test.yml reusable workflow"
git -C ../.github push origin master
```

Do not run these two commands yourself. Wait for Buvy to confirm they're done before Task 2.

---

### Task 2: Author the release-ceremony reusable workflow (`release-nuget.yml`)

**Files:**
- Create: `../.github/.github/workflows/release-nuget.yml`

**Interfaces:**
- Consumes: `ci-build-test.yml` (Task 1), called via `uses: ./.github/workflows/ci-build-test.yml` (same-repo relative reference).
- Produces: a `workflow_call` reusable workflow with three jobs (`build-test`, `codeql`, `pack-and-publish`), consumed by Task 6's Svartalfheim release caller.

- [ ] **Step 1: Write the reusable workflow**

Create `../.github/.github/workflows/release-nuget.yml`:

```yaml
name: NuGet Release

on:
  workflow_call:

permissions:
  contents: write
  packages: write
  security-events: write

jobs:
  build-test:
    uses: ./.github/workflows/ci-build-test.yml

  codeql:
    runs-on: ubuntu-latest
    permissions:
      security-events: write
      contents: read
    steps:
      - name: Checkout
        uses: actions/checkout@v7

      - name: Setup .NET
        uses: actions/setup-dotnet@v5
        with:
          global-json-file: global.json
          dotnet-quality: "preview"

      - name: Initialize CodeQL
        uses: github/codeql-action/init@v4
        with:
          languages: csharp

      - name: Restore and build
        run: |
          dotnet restore
          dotnet build --no-restore -c Release

      - name: Analyze
        uses: github/codeql-action/analyze@v4

  pack-and-publish:
    needs: [build-test, codeql]
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v7
        with:
          fetch-depth: 0

      - name: Setup .NET
        uses: actions/setup-dotnet@v5
        with:
          global-json-file: global.json
          dotnet-quality: "preview"

      - name: Restore
        run: dotnet restore

      - name: Pack
        run: dotnet pack -c Release -o ./artifacts/nupkg

      - name: Generate SBOM
        uses: anchore/sbom-action@v0
        with:
          path: .
          format: cyclonedx-json
          output-file: sbom.cyclonedx.json

      - name: Push to GitHub Packages
        run: dotnet nuget push "./artifacts/nupkg/*.nupkg" --api-key ${{ secrets.GITHUB_TOKEN }} --source "https://nuget.pkg.github.com/NorseArchitecture/index.json" --skip-duplicate

      - name: Create GitHub Release
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          gh release create "${{ github.ref_name }}" \
            ./artifacts/nupkg/*.nupkg \
            ./sbom.cyclonedx.json \
            --generate-notes
```

`fetch-depth: 0` again, for the same MinVer reason as Task 1. `pack-and-publish` waits on both `build-test` and `codeql` so a release never publishes a package that failed its test suite or its security scan.

- [ ] **Step 2: Stage and stop**

```bash
git -C ../.github add .github/workflows/release-nuget.yml
git -C ../.github diff --cached
```

**STOP.** Buvy commits and pushes directly to `master` himself, same as Task 1:

```bash
git -C ../.github commit -m "Add release-nuget.yml reusable workflow"
git -C ../.github push origin master
```

Wait for his confirmation before Task 3.

---

### Task 3: Wire MinVer into Svartalfheim

**Files:**
- Modify: `Svartalfheim/Directory.Build.props`

**Interfaces:**
- Produces: every project under Svartalfheim gets a MinVer-computed `Version` at build time; `Primitives.csproj`'s package version is `0.0.0-alpha.0.{height}` on untagged commits, and exactly `X.Y.Z` on a commit tagged `vX.Y.Z`.

- [ ] **Step 1: Add the MinVer reference**

Read the current file first:

```bash
cat Svartalfheim/Directory.Build.props
```

Add `MinVerTagPrefix` and an explicit `PackageId` (mirroring the existing `AssemblyName`/`RootNamespace` pattern — `PackageId` defaults to `AssemblyName` already, but stated explicitly here so the three identity properties read as one deliberate decision, not an implicit fallback) to the existing `<PropertyGroup>`, plus a new `<ItemGroup>` for MinVer:

```xml
<Project>
	<PropertyGroup>
		<!--
			Analyzer tiers follow the platform baseline: Security/Performance/Reliability/Usage
			at latest-All; Design stays at the global baseline because latest-All enables rules
			(e.g. CA1034) that conflict with discriminated-union-style type shapes.
		-->
		<AnalysisLevel>latest-Recommended</AnalysisLevel>
		<AnalysisLevelSecurity>latest-All</AnalysisLevelSecurity>
		<AnalysisLevelPerformance>latest-All</AnalysisLevelPerformance>
		<AnalysisLevelReliability>latest-All</AnalysisLevelReliability>
		<AnalysisLevelUsage>latest-All</AnalysisLevelUsage>
		<!--
			The brand prefix lives here and nowhere else: project folders and .csproj files are
			brand-free, so a fork rebrands by changing "Norse" once per realm — no file renames.
		-->
		<AssemblyName>Norse.$(MSBuildProjectName)</AssemblyName>
		<Authors>Norse Architecture</Authors>
		<Deterministic>true</Deterministic>
		<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
		<GenerateDocumentationFile>true</GenerateDocumentationFile>
		<ImplicitUsings>enable</ImplicitUsings>
		<LangVersion>preview</LangVersion>
		<MinVerTagPrefix>v</MinVerTagPrefix>
		<Nullable>enable</Nullable>
		<PackageId>Norse.$(MSBuildProjectName)</PackageId>
		<RootNamespace>Norse.$(MSBuildProjectName)</RootNamespace>
		<TargetFramework>net11.0</TargetFramework>
		<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
		<WarningLevel>9999</WarningLevel>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="MinVer" Version="6.*">
			<PrivateAssets>all</PrivateAssets>
			<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
		</PackageReference>
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Verify locally**

```bash
cd Svartalfheim
dotnet build Svartalfheim.slnx -c Release
```

Expected: build succeeds (zero warnings, per existing `TreatWarningsAsErrors`). Check the computed version:

```bash
dotnet build src/Primitives/Primitives.csproj -c Release -getProperty:Version
```

Expected: `1.0.0` — MinVer's actual default when no `v*` tag exists yet anywhere in this checkout's history (confirmed empirically; MinVer does not add a prerelease/height suffix until there's at least one tag to count commits ahead of). This confirms MinVer is active before any tag exists; once Task 6 pushes `v0.0.1`, the same query will report exactly `0.0.1`.

- [ ] **Step 3: Stage and stop**

```bash
git -C Svartalfheim add Directory.Build.props
git -C Svartalfheim diff --cached
```

**STOP.** Per Svartalfheim's own CLAUDE.md, Buvy reviews in GitHub Desktop and commits/pushes himself. Wait for his confirmation before Task 4.

---

### Task 4: Add Svartalfheim's PR-gate caller and prove red/green

**Files:**
- Create: `Svartalfheim/.github/workflows/ci.yml`

**Interfaces:**
- Consumes: `NorseArchitecture/.github/.github/workflows/ci-build-test.yml@master` (Task 1).
- Produces: a GitHub Actions check on every PR against Svartalfheim's `master`, job id `build`.

- [ ] **Step 1: Write the caller workflow**

Create `Svartalfheim/.github/workflows/ci.yml`:

```yaml
name: CI

on:
  pull_request:
    branches: [master]

jobs:
  gate:
    uses: NorseArchitecture/.github/.github/workflows/ci-build-test.yml@master
```

The caller job is named `gate` (not `build`) to avoid a confusing stutter in the GitHub check name — the called job inside `ci-build-test.yml` is `build`, giving the required status check context `gate / build`. If both were named `build` the context would read `build / build`.

- [ ] **Step 2: Stage and stop**

```bash
git -C Svartalfheim add .github/workflows/ci.yml
git -C Svartalfheim diff --cached
```

**STOP.** Buvy commits this to a new branch (not directly to `master` — this is the change we want the PR gate to prove against itself) and pushes:

```bash
git -C Svartalfheim checkout -b ci/add-pr-gate
git -C Svartalfheim commit -m "Add CI PR gate"
git -C Svartalfheim push -u origin ci/add-pr-gate
```

- [ ] **Step 3: Open the PR and prove RED first**

Buvy opens the PR:

```bash
gh pr create -R NorseArchitecture/Svartalfheim --base master --head ci/add-pr-gate --title "Add CI PR gate" --body "Proving the new ci-build-test.yml reusable workflow."
```

Before checking the result, Buvy deliberately breaks one existing assertion to prove the gate actually catches a failure — e.g., in `Svartalfheim/tests/Primitives.Tests/`, flip one `ShouldBe(expected)` to an obviously wrong value, commit, and push to the same branch:

```bash
git -C Svartalfheim commit -am "Temporarily break a test to prove the gate catches it"
git -C Svartalfheim push
```

**STOP after each push — do not run these for him.** Once he confirms the broken commit is pushed, verify (read-only):

```bash
gh pr checks ci/add-pr-gate -R NorseArchitecture/Svartalfheim
```

Expected: the `build` check is failing. This is the RED proof — the gate caught a real broken test.

- [ ] **Step 4: Revert the break and prove GREEN**

Buvy reverts the deliberate breakage and pushes:

```bash
git -C Svartalfheim revert HEAD --no-edit
git -C Svartalfheim push
```

**STOP — wait for his confirmation**, then verify (read-only):

```bash
gh pr checks ci/add-pr-gate -R NorseArchitecture/Svartalfheim
```

Expected: the `build` check now passes. Record the **exact check name** shown (e.g. `build / build` or similar) — Task 5 needs this literal string.

- [ ] **Step 5: Do not merge yet**

Leave the PR open. Task 5 must land first, because the existing required-status-check name (`build`, no slash) almost certainly does not match what GitHub actually reports for a job that calls a reusable workflow (typically `<caller-job-id> / <called-job-id>`) — until Task 5 aligns it, the merge button may be falsely blocked or falsely unblocked.

---

### Task 5: Align the required status check name

**Files:**
- Modify: `../.github/scripts/carve-the-laws.ps1`

**Interfaces:**
- Consumes: the exact check-name string recorded in Task 4 Step 4.
- Produces: an updated "Law of the Aesir" ruleset on `NorseArchitecture/Svartalfheim` whose required status check matches reality.

- [ ] **Step 1: Confirm the real check name**

**Empirically confirmed 2026-06-25.** GitHub Actions reports the check as `{caller job} / {called job}` — the workflow `name:` field and the `(pull_request)` event suffix visible in the UI are decorations only and must not appear in the context string. With the caller job named `gate` and the called job `build`, the context is `gate / build`. Also confirmed: the source must be locked to `integration_id: 15368` (the GitHub Actions app) — without it, the ruleset UI shows the check waiting even when a check of the same name has succeeded, because any integration can report that name. Get the app ID from the check runs API rather than hardcoding speculatively:

```bash
gh api repos/NorseArchitecture/Svartalfheim/commits/$(git rev-parse HEAD)/check-runs \
  --jq '.check_runs[] | {name: .name, app_id: .app.id}'
```

Expected: `{"name":"gate / build","app_id":15368}`.

- [ ] **Step 2: Update the ruleset script**

In `../.github/scripts/carve-the-laws.ps1`, update the required status check entry to include both the confirmed context and the `integration_id` lock:

```powershell
			required_status_checks = @(
				@{ context = 'gate / build'; integration_id = 15368 }
			)
```

Update the comment block to record the empirical finding and the rationale for the `integration_id` lock.

- [ ] **Step 3: Stage and stop**

```bash
git -C ../.github add scripts/carve-the-laws.ps1
git -C ../.github diff --cached
```

**STOP.** Buvy commits/pushes directly to `.github`'s `master` (same direct-push situation as Tasks 1–2 — this repo has no PR gate of its own).

- [ ] **Step 4: Re-carve the law for Svartalfheim and verify**

Once pushed, Buvy runs (or directs the executor to run, since this is a read/write-to-ruleset operation via `gh api`, not a git commit — but still mutates live branch protection, so confirm with him first):

```powershell
./scripts/carve-the-laws.ps1 Svartalfheim
```

Verify (read-only):

```bash
gh ruleset list -R NorseArchitecture/Svartalfheim
gh api repos/NorseArchitecture/Svartalfheim/rulesets/<id> --jq '.rules[] | select(.type=="required_status_checks")'
```

Expected: the required check context now matches Task 4's real check name exactly.

- [ ] **Step 5: Merge the PR-gate PR**

Buvy merges `ci/add-pr-gate` now that the required check correctly gates it:

```bash
gh pr merge ci/add-pr-gate -R NorseArchitecture/Svartalfheim --squash
```

**Do not run this yourself.** Confirm afterward (read-only) with `gh pr view ci/add-pr-gate -R NorseArchitecture/Svartalfheim --json state`.

---

### Task 6: Add Svartalfheim's release caller and prove the full ceremony

**Files:**
- Create: `Svartalfheim/.github/workflows/release.yml`

**Interfaces:**
- Consumes: `NorseArchitecture/.github/.github/workflows/release-nuget.yml@master` (Task 2).
- Produces: on any `v*.*.*` tag push to Svartalfheim, a rebuilt, re-tested, CodeQL-scanned, SBOM-accompanied `Norse.Primitives` package on GitHub Packages plus a GitHub Release.

- [ ] **Step 1: Write the caller workflow**

Create `Svartalfheim/.github/workflows/release.yml`:

```yaml
name: Release

on:
  push:
    tags:
      - 'v*.*.*'

permissions:
  contents: write
  packages: write
  security-events: write

jobs:
  release:
    uses: NorseArchitecture/.github/.github/workflows/release-nuget.yml@master
```

The `permissions` block here is required even though `release-nuget.yml` declares its own — GitHub takes the more restrictive of caller and callee, so without this the callee's elevated permissions would be silently capped back down to read-only.

- [ ] **Step 2: Stage and stop**

```bash
git -C Svartalfheim add .github/workflows/release.yml
git -C Svartalfheim diff --cached
```

**STOP.** Buvy commits this directly to `master` (it should land via its own small PR through the now-working gate from Task 5, or he may judge a direct admin-bypass push acceptable for this one infra file — his call, not the executor's).

- [ ] **Step 3: Cut the first tag**

Once `release.yml` is on `master`, Buvy tags a release:

```bash
git -C Svartalfheim tag -a v0.0.1 -m "First CI-proven release"
git -C Svartalfheim push origin v0.0.1
```

**Do not run these yourself.**

- [ ] **Step 4: Verify the full ceremony (read-only)**

```bash
gh run list -R NorseArchitecture/Svartalfheim --workflow=release.yml --limit 1
gh run view -R NorseArchitecture/Svartalfheim <run-id>
```

Expected: `build-test`, `codeql`, and `pack-and-publish` all succeed. Then confirm the artifacts actually landed:

```bash
gh api orgs/NorseArchitecture/packages/nuget/Norse.Primitives/versions --jq '.[0].name'
gh release view v0.0.1 -R NorseArchitecture/Svartalfheim
```

Expected: the package version is `0.0.1` (MinVer read it straight from the tag — no other source of truth involved), and the release page lists both the `.nupkg` and `sbom.cyclonedx.json` as assets. These three things together — green run, published package, attached SBOM — are the "dwarvish receipts."

---

## Self-Review

**Spec coverage:** §2.1 scope matrix → honored (Svartalfheim only, per explicit narrowing; Bifrost/others deferred). §2.2 reusable workflow architecture → Tasks 1–2. §2.3 PR gate → Task 4. §2.4 merge-to-master (no extra step) → not re-implemented, nothing to do. §2.5 tag-is-the-version → Task 3 (MinVer). §2.6 NuGet release ceremony → Task 2 + Task 6. §2.7 Yggdrasil container path → explicitly out of scope, no task (correct — no Yggdrasil code exists to prove it against). §2.8 auth/tooling → covered inline in Tasks 1–2 (`GITHUB_TOKEN`, CodeQL, SBOM action).

**Placeholder scan:** no TBD/TODO; every code block is complete and runnable as written.

**Type/name consistency:** job id `build` (Task 1) is the same id referenced by Task 4's caller and re-confirmed in Task 5; `Norse.Primitives` (Task 6 verification) matches the `AssemblyName` pattern already in `Directory.Build.props`; tag pattern `v*.*.*` (Task 6's trigger) matches `MinVerTagPrefix=v` (Task 3) and the `v0.0.1` tag actually cut.
