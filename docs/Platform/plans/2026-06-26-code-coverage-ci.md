# Code Coverage CI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (default) or `superpowers:executing-plans` (separate-session fallback) to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add branch coverage collection, reporting, and hard-gate threshold enforcement to the shared CI workflow, proven in Svartalfheim and ready to propagate to any realm.

**Architecture:** `Microsoft.Testing.Extensions.CodeCoverage` collects Cobertura XML natively via `dotnet test --coverage`; `ReportGenerator` converts it to a GitHub-flavoured Markdown report posted as a sticky PR comment and written to the step summary; a bash threshold check reading `Summary.json` fails the job if branch coverage falls below `max(FLOOR=60, realm-specified-threshold)`. The org floor is a constant in one place — the enforcement step in the shared workflow.

**Tech Stack:** .NET 11 preview · xUnit v3 + Microsoft.Testing.Platform · `Microsoft.Testing.Extensions.CodeCoverage 18.*` · `dotnet-reportgenerator-globaltool 5.*` · `marocchino/sticky-pull-request-comment@v2` · GitHub Actions `workflow_call` inputs.

## Global Constraints

- US English spelling in all identifiers, files, messages, and commit copy.
- **No automatic git commits** — stage with `git add` and show the diff; the human commits in GitHub Desktop. Hard rule from CLAUDE.md in both Bifrost and Svartalfheim — this applies even though this skill's template includes commit steps.
- **Relative paths only** — all paths in commands are relative to the Bifrost workspace root (`./Svartalfheim`, `../.github`). No machine-local absolute paths.
- `FLOOR=60` lives in exactly one place: the `Enforce branch coverage threshold` step in `ci-build-test.yml`.
- `minimum_coverage` input default is `0` — the floor, not the default, is the enforced minimum.
- Tabs for indentation in `.props` files; 2-space for YAML (ecosystem convention per `.editorconfig`).
- All package version wildcards (`17.*`, `5.*`, `2.*`) match the patterns already established in the realm.

---

## File Map

| File | Repo | Action |
|------|------|--------|
| `./Svartalfheim/tests/Directory.Build.props` | Svartalfheim | Modify — add `Microsoft.Testing.Extensions.CodeCoverage` package reference |
| `../.github/.github/workflows/ci-build-test.yml` | `.github` | Modify — add `minimum_coverage` input, `permissions`, and five new steps |
| `./Svartalfheim/.github/workflows/ci.yml` | Svartalfheim | Modify — add `permissions` and `with: minimum_coverage: <baseline>` |

---

### Task 1: Add coverage package to Svartalfheim and measure the branch coverage baseline

**Files:**
- Modify: `./Svartalfheim/tests/Directory.Build.props`

**Interfaces:**
- Produces: the measured branch coverage percentage that becomes `minimum_coverage` in Task 3. Record it before cleaning up.

- [ ] **Step 1: Add `Microsoft.Testing.Extensions.CodeCoverage` to `tests/Directory.Build.props`**

The file currently has no coverage package. Add the reference (maintain alphabetical order within the `ItemGroup`):

```xml
<Project>
	<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
	<PropertyGroup>
		<IsPackable>false</IsPackable>
		<IsTestProject>true</IsTestProject>
		<NoWarn>$(NoWarn);CA1812;CA1859;CS1591;IDE0051</NoWarn>
		<OutputType>Exe</OutputType>
		<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Microsoft.Testing.Extensions.CodeCoverage" Version="18.*" />
		<PackageReference Include="Microsoft.Testing.Platform.MSBuild" Version="2.*" />
		<PackageReference Include="Shouldly" Version="4.*" />
		<PackageReference Include="xunit.v3.mtp-v2" Version="3.*" />
		<Using Include="Shouldly" />
		<Using Include="Xunit" />
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Restore and build to confirm the package resolves**

Run from the Bifrost workspace root:

```
dotnet restore ./Svartalfheim/Svartalfheim.slnx
dotnet build ./Svartalfheim/Svartalfheim.slnx --no-restore -c Release
```

Expected: exits 0 with zero warnings and zero errors.

- [ ] **Step 3: Run tests with coverage collection**

```
dotnet test ./Svartalfheim/Svartalfheim.slnx --no-build -c Release --coverage --coverage-output-format cobertura --coverage-output coverage.xml
```

Expected: tests pass and `coverage.xml` (or `coverage_1.xml` if MTP appends a counter) appears in the Svartalfheim repo root. Verify:

```
ls ./Svartalfheim/coverage*.xml
```

Expected: at least one file listed.

- [ ] **Step 4: Install ReportGenerator globally**

```
dotnet tool install -g dotnet-reportgenerator-globaltool --version 5.*
```

If already installed, update instead:

```
dotnet tool update -g dotnet-reportgenerator-globaltool --version 5.*
```

Verify:

```
reportgenerator --version
```

Expected: prints a version string beginning with `5.`.

- [ ] **Step 5: Generate the report and record the baseline**

Run from the Bifrost workspace root:

```
reportgenerator -reports:./Svartalfheim/coverage*.xml -targetdir:./Svartalfheim/coverage-report -reporttypes:"MarkdownSummaryGithub;JsonSummary"
```

Read the branch coverage:

```
jq '.summary.branchcoverage' ./Svartalfheim/coverage-report/Summary.json
```

Expected: a number between 0 and 100, e.g. `87.5`.

**Record this number now.** Round it DOWN to the nearest 5 (e.g. `87.5` → `85`; `90.0` → `90`; `83.3` → `80`). This rounded value is the `minimum_coverage` argument for Task 3 Step 1.

- [ ] **Step 6: Clean up local coverage artefacts**

```
rm -rf ./Svartalfheim/coverage*.xml ./Svartalfheim/coverage-report
```

These artefacts are not committed. The `.gitignore` already excludes `coverage*.xml`; add `coverage-report/` if it is not already present:

```
grep -q "coverage-report" ./Svartalfheim/.gitignore || echo "coverage-report/" >> ./Svartalfheim/.gitignore
```

- [ ] **Step 7: Stage and show the diff**

```
git -C ./Svartalfheim add tests/Directory.Build.props .gitignore
git -C ./Svartalfheim diff --staged
```

Expected: diff shows only the added `PackageReference` line in `tests/Directory.Build.props` (and the `coverage-report/` line in `.gitignore` if it was absent). **Stop here — the human commits.**

---

### Task 2: Update the shared CI workflow with coverage steps

**Files:**
- Modify: `../.github/.github/workflows/ci-build-test.yml`

**Interfaces:**
- Consumes: nothing from other tasks
- Produces: a reusable workflow accepting `minimum_coverage: number (default 0)` and enforcing `max(60, minimum_coverage)` branch coverage; posts a sticky PR comment and writes the step summary

- [ ] **Step 1: Replace `ci-build-test.yml` with the full updated workflow**

```yaml
name: CI Build & Test

on:
  workflow_call:
    inputs:
      minimum_coverage:
        description: 'Realm branch coverage minimum (0–100); values below the org floor are raised automatically'
        type: number
        default: 0

jobs:
  build:
    runs-on: ubuntu-latest
    permissions:
      pull-requests: write
    steps:
      - name: Checkout
        uses: actions/checkout@v7
        with:
          fetch-depth: 0

      - name: Setup .NET
        uses: actions/setup-dotnet@v5
        with:
          dotnet-version: "11.0.x"
          dotnet-quality: "preview"

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore -c Release

      - name: Test
        run: dotnet test --no-build -c Release --coverage --coverage-output-format cobertura --coverage-output coverage.xml

      - name: Install ReportGenerator
        run: dotnet tool install -g dotnet-reportgenerator-globaltool --version 5.*

      - name: Generate coverage report
        run: |
          reportgenerator \
            -reports:coverage*.xml \
            -targetdir:./coverage-report \
            -reporttypes:"MarkdownSummaryGithub;JsonSummary"

      - name: Write coverage summary
        run: cat ./coverage-report/SummaryGithub.md >> $GITHUB_STEP_SUMMARY

      - name: Post coverage comment
        if: github.event_name == 'pull_request'
        continue-on-error: true
        uses: marocchino/sticky-pull-request-comment@v2
        with:
          header: coverage
          path: ./coverage-report/SummaryGithub.md

      - name: Enforce branch coverage threshold
        run: |
          BRANCH=$(jq '.summary.branchcoverage' ./coverage-report/Summary.json)
          FLOOR=60
          REALM=${{ inputs.minimum_coverage }}
          THRESHOLD=$(echo "$FLOOR $REALM" | awk '{print ($1 > $2) ? $1 : $2}')
          echo "Branch coverage: ${BRANCH}%  |  Required: ${THRESHOLD}%  (floor: ${FLOOR}%, realm: ${REALM}%)"
          if (( $(echo "$BRANCH < $THRESHOLD" | bc -l) )); then
            echo "::error::Branch coverage ${BRANCH}% is below the required ${THRESHOLD}%"
            exit 1
          fi
```

- [ ] **Step 2: Verify the YAML is well-formed**

Run from the Bifrost workspace root:

```
python3 -c "import yaml; yaml.safe_load(open('../.github/.github/workflows/ci-build-test.yml'))"
```

Expected: no output (parse succeeds, no exception).

- [ ] **Step 3: Stage and show the diff**

```
git -C ../.github add .github/workflows/ci-build-test.yml
git -C ../.github diff --staged
```

Expected: diff shows the new `on.workflow_call.inputs` block, `permissions: pull-requests: write` on the job, the modified Test step, and the five new steps (Install ReportGenerator, Generate coverage report, Write coverage summary, Post coverage comment, Enforce branch coverage threshold). **Stop here — the human commits.**

---

### Task 3: Wire Svartalfheim's CI caller with the measured threshold

**Files:**
- Modify: `./Svartalfheim/.github/workflows/ci.yml`

**Interfaces:**
- Consumes: the rounded-down baseline value from Task 1 Step 5
- Produces: a `ci.yml` that declares `pull-requests: write` and passes the measured threshold to the shared workflow

- [ ] **Step 1: Replace `ci.yml` with the updated caller**

Substitute `<BASELINE>` with the rounded-down value recorded in Task 1 Step 5:

```yaml
name: CI

on:
  pull_request:
    branches: [master]

permissions:
  pull-requests: write

jobs:
  gate:
    uses: NorseArchitecture/.github/.github/workflows/ci-build-test.yml@master
    with:
      minimum_coverage: <BASELINE>
```

- [ ] **Step 2: Verify the YAML is well-formed**

```
python3 -c "import yaml; yaml.safe_load(open('./Svartalfheim/.github/workflows/ci.yml'))"
```

Expected: no output.

- [ ] **Step 3: Stage and show the diff**

```
git -C ./Svartalfheim add .github/workflows/ci.yml
git -C ./Svartalfheim diff --staged
```

Expected: diff shows the new top-level `permissions: pull-requests: write` block and the `with: minimum_coverage: <BASELINE>` line on the `gate` job. **Stop here — the human commits.**

---

## After All Three Tasks

Each repo needs its own commit and PR:

1. **`.github` repo** — commit the `ci-build-test.yml` change and merge to `master` first; it must be live at `@master` before the Svartalfheim PR runs CI.
2. **Svartalfheim repo** — commit `tests/Directory.Build.props`, `.gitignore`, and `.github/workflows/ci.yml` together and open a PR; the first CI run confirms coverage collection, reporting, and threshold enforcement all wire up correctly.

The step summary and sticky PR comment are only visible after the Svartalfheim PR CI run completes. If branch coverage is at or above the baseline threshold, the gate passes and the PR is green. If it fails for any reason, check the `Enforce branch coverage threshold` step log — it prints the measured branch coverage, the floor, the realm value, and the effective threshold on a single line.
