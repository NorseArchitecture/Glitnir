# Code Coverage CI Design

**Date:** 2026-06-26
**Scope:** Platform-wide — proven in Svartalfheim, propagated realm by realm
**Status:** Approved, ready for implementation

---

## 1. Purpose

Add branch coverage collection, reporting, and threshold enforcement to the shared CI workflow. Every realm that calls `ci-build-test.yml` picks up coverage automatically. Each realm sets its own threshold; the org floor prevents any realm from silently setting it to zero.

---

## 2. Architecture Overview

Two repos change. Nothing else.

| Repo | Change |
|------|--------|
| `NorseArchitecture/.github` | `ci-build-test.yml` gains a `minimum_coverage` input, coverage collection flags on the Test step, three reporting steps, and a hard-gate enforcement step |
| `NorseArchitecture/Svartalfheim` | `tests/Directory.Build.props` gains one package reference; `.github/workflows/ci.yml` passes its threshold and declares `pull-requests: write` |

When another realm is ready to adopt coverage, it makes the same two changes in its own repos. The shared workflow already supports it.

---

## 3. Workflow Input Model

### Shared workflow (`ci-build-test.yml`)

```yaml
on:
  workflow_call:
    inputs:
      minimum_coverage:
        description: 'Realm branch coverage minimum (0–100); values below the org floor are raised automatically'
        type: number
        default: 0
```

The input default is `0` — the org floor in the enforcement step is the single source of truth for the minimum value. Setting the default to the floor value would create two authoritative locations.

### Org floor

`FLOOR=60` is a constant in the enforcement step of `ci-build-test.yml`. This is the only place the number lives. Change it there to update the platform-wide minimum.

### Realm caller (`ci.yml`)

```yaml
permissions:
  pull-requests: write

jobs:
  gate:
    uses: NorseArchitecture/.github/.github/workflows/ci-build-test.yml@master
    with:
      minimum_coverage: 80
```

Effective threshold = `max(FLOOR, minimum_coverage)`. A realm passing `25` gets `60`. A realm passing `80` gets `80`. A realm passing nothing gets `60`.

**Svartalfheim's threshold** is not pinned in this spec — it is set to the actual baseline measured on the first CI run, rounded down to the nearest 5. Measure first, enforce second.

---

## 4. Coverage Collection

### Package reference

Added to `tests/Directory.Build.props` (hoisted, not per-csproj):

```xml
<PackageReference Include="Microsoft.Testing.Extensions.CodeCoverage" Version="18.*" />
```

This is the official Microsoft coverage extension for Microsoft.Testing.Platform. No VSTest DataCollector, no `.runsettings`, no separate collection tool.

### Test step

```yaml
- name: Test
  run: dotnet test --no-build -c Release --coverage --coverage-output-format cobertura --coverage-output coverage.xml
```

`--coverage` activates the MTP extension. `--coverage-output-format cobertura` produces Cobertura XML directly. When a solution has multiple test projects, MTP appends a counter (`coverage_1.xml`, `coverage_2.xml`); downstream ReportGenerator uses a glob (`coverage*.xml`) — multi-project safe from day one.

---

## 5. Reporting Pipeline

Steps execute in this order after Test: install tool → generate report → write step summary → post PR comment → enforce threshold.

The PR comment always posts before the threshold gate fires — a failing PR gets both the coverage numbers in the comment and a clear error on the job.

### Install ReportGenerator

```yaml
- name: Install ReportGenerator
  run: dotnet tool install -g dotnet-reportgenerator-globaltool --version 5.*
```

### Generate report

Produces two outputs: GitHub-flavoured Markdown for the PR comment/step summary, and a JSON summary for the threshold check.

```yaml
- name: Generate coverage report
  run: |
    reportgenerator \
      -reports:coverage*.xml \
      -targetdir:./coverage-report \
      -reporttypes:"MarkdownSummaryGithub;JsonSummary"
```

### Write step summary

Always visible in the workflow run details regardless of pass/fail.

```yaml
- name: Write coverage summary
  run: cat ./coverage-report/SummaryGithub.md >> $GITHUB_STEP_SUMMARY
```

### Post PR comment

Sticky — updates in place on each push rather than flooding the PR timeline. `continue-on-error: true` because fork PRs do not carry `pull-requests: write` and this must not prevent the threshold gate from running.

```yaml
- name: Post coverage comment
  if: github.event_name == 'pull_request'
  continue-on-error: true
  uses: marocchino/sticky-pull-request-comment@v2
  with:
    header: coverage
    path: ./coverage-report/SummaryGithub.md
```

---

## 6. Threshold Enforcement

Hard gate — fails the job, blocks PR merge. Runs last so the PR comment always lands first.

```yaml
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

`jq`, `bc`, and `awk` are present on `ubuntu-latest` — no installs. The `::error::` annotation surfaces the message directly in the GitHub Actions UI. The echo line shows floor and realm values so it is immediately obvious which one won.

---

## 7. Shared Workflow Job Permissions

The job in `ci-build-test.yml` declares `pull-requests: write` to support the sticky PR comment. In GitHub Actions reusable workflows, the `GITHUB_TOKEN` permissions are set by the caller — the reusable workflow cannot escalate beyond what the caller grants. Both sides must declare the permission.

```yaml
jobs:
  build:
    runs-on: ubuntu-latest
    permissions:
      pull-requests: write
```

---

## 8. Realm Adoption Pattern

When a new realm is ready to add coverage:

1. Add `Microsoft.Testing.Extensions.CodeCoverage` to the realm's `tests/Directory.Build.props`.
2. Add `permissions: pull-requests: write` and `with: minimum_coverage: <N>` to the realm's `ci.yml`.
3. Merge to master, observe the first CI run, note the actual branch coverage, set `minimum_coverage` to that baseline rounded down to the nearest 5, and merge again.

Step 3 is the ratchet: start at the baseline, tighten incrementally. Never set a threshold above the current baseline on the first run — that is a broken build with no path to green.
