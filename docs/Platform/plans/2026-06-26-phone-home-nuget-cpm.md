# Phone Home — NuGet CPM Auto-Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (default) or `superpowers:executing-plans` (separate-session fallback) to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Every coding task pairs with `superpowers:test-driven-development`.

**Goal:** After any stable NuGet realm release, automatically open an auto-merge PR in Yggdrasil that bumps the corresponding `<{Realm}Version>` property in `Directory.Packages.props`.

**Architecture:** A reusable `workflow_call` workflow (`phone-home-nuget.yml`) in `.github` mirrors the scatter pattern in reverse — each NuGet realm's `release.yml` calls it after `pack-and-publish` succeeds. The workflow checks out `.github` (to get the PowerShell script) and Yggdrasil (to edit and push), then opens or updates a PR on branch `update/cpm/{realm}` with auto-merge armed. Pre-release tags (containing `-`) are skipped at the job level and again in the script as a belt-and-suspenders guard.

**Tech Stack:** GitHub Actions (`workflow_call`), PowerShell Core (`pwsh`), MSBuild Central Package Management, `gh` CLI (bundled on GitHub-hosted runners).

## Global Constraints

- US English spelling in all code, comments, docs, and commit messages.
- Tabs for indentation in YAML and PowerShell (ecosystem exceptions: YAML is 2-space per `.editorconfig`).
- No automatic git commits — stage changes and show the diff; the human commits. This applies to every task below: `git add` the files, show `git diff --staged`, stop.
- No force-push to `master` on any repo.
- No secrets committed. All tokens reference GitHub Secrets by name only.
- Reusable workflow logic lives in `.github`; realm callers are thin stubs with no inline data.
- `-DryRun` switch must be present on every script invocation that writes to disk or network.
- `$ErrorActionPreference = 'Stop'` at the top of every PowerShell script.
- The `phone-home` job must declare `needs: [release]` so it never fires if the release ceremony fails.
- Pre-release tags (any version string containing `-`) must be skipped silently with `exit 0`.
- Hard-fail (throw) if `Directory.Packages.props` is missing or the `<{Realm}Version>` property is absent.

---

## File Map

| Action | Path | Responsibility |
|---|---|---|
| Create | `Yggdrasil/Directory.Packages.props` | CPM file; version properties + PackageVersion items; hand-maintained, automation updates values only |
| Create | `.github/.github/workflows/phone-home-nuget.yml` | Reusable `workflow_call`; pre-release guard; two checkouts; runs script |
| Create | `.github/scripts/phone-home-nuget.ps1` | Edits props file, commits, pushes branch, opens/updates PR, arms auto-merge |
| Modify | `Svartalfheim/.github/workflows/release.yml` | Append `phone-home` job |
| Create | `Asgard/.github/workflows/release.yml` | New; both `release` and `phone-home` jobs |
| Create | `Midgard/.github/workflows/release.yml` | New; both jobs |
| Create | `Urdarbrunnr/.github/workflows/release.yml` | New; both jobs |
| Create | `Ratatoskr/.github/workflows/release.yml` | New; both jobs |
| Create | `Himinbjorg/.github/workflows/release.yml` | New; both jobs |
| Create | `Heimdall/.github/workflows/release.yml` | New; both jobs |

---

## Task 1: Create Yggdrasil's `Directory.Packages.props`

**Files:**
- Create: `Yggdrasil/Directory.Packages.props`

**Context:** This file is hand-authored once. `ManagePackageVersionsCentrally=true` enables CPM for Yggdrasil. One `<{Realm}Version>` property per NuGet realm; all start at `0.0.0` — an invalid NuGet version that fails loudly if consumed before the first real release phones home. `PackageVersion` items reference these properties via MSBuild expansion so all packages from a given realm move together. Packages for realms that have no defined projects yet are omitted; they are added when those realms define their packages.

- [ ] **Step 1: Create the file**

`Yggdrasil/Directory.Packages.props`:
```xml
<Project>
	<PropertyGroup>
		<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
		<SvartalfheimVersion>0.0.0</SvartalfheimVersion>
		<AsgardVersion>0.0.0</AsgardVersion>
		<MidgardVersion>0.0.0</MidgardVersion>
		<UrdarbrunnrVersion>0.0.0</UrdarbrunnrVersion>
		<RatatoskrVersion>0.0.0</RatatoskrVersion>
		<HiminbjorgVersion>0.0.0</HiminbjorgVersion>
		<HeimdallVersion>0.0.0</HeimdallVersion>
	</PropertyGroup>
	<ItemGroup>
		<PackageVersion Include="Norse.Primitives" Version="$(SvartalfheimVersion)" />

		<PackageVersion Include="Norse.Abstractions.Backend"    Version="$(AsgardVersion)" />
		<PackageVersion Include="Norse.Abstractions.Components" Version="$(AsgardVersion)" />
		<PackageVersion Include="Norse.Abstractions.Contracts"  Version="$(AsgardVersion)" />
		<PackageVersion Include="Norse.Abstractions.Migrations" Version="$(AsgardVersion)" />
		<PackageVersion Include="Norse.Abstractions.Web.Server" Version="$(AsgardVersion)" />
		<PackageVersion Include="Norse.Abstractions.Worker"     Version="$(AsgardVersion)" />
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Verify it is well-formed XML**

```bash
pwsh -c "[xml](Get-Content Yggdrasil/Directory.Packages.props) | Out-Null; Write-Host 'XML valid'"
```

Expected: `XML valid`

- [ ] **Step 3: Stage and review**

```bash
git -C Yggdrasil add Directory.Packages.props
git -C Yggdrasil diff --staged
```

Expected: new file with the full CPM structure above. Stop here — human commits.

---

## Task 2: Add `phone-home-nuget.yml` reusable workflow

**Files:**
- Create: `.github/.github/workflows/phone-home-nuget.yml`

**Context:** In a `workflow_call`, `github.event.repository.name` resolves to the calling repo (e.g., `Svartalfheim`) and `github.ref_name` resolves to the tag that triggered the caller (e.g., `v0.0.2`). Both checkouts use `secrets.token` — `GITHUB_TOKEN` in a `workflow_call` context is scoped to the caller's repo and cannot read `.github` or write to Yggdrasil cross-repo. The caller's repo is never checked out.

- [ ] **Step 1: Create the workflow file**

`.github/.github/workflows/phone-home-nuget.yml`:
```yaml
name: Phone Home — NuGet CPM Update

# Composite actions cannot be called cross-repo from a reusable workflow — the
# runner checks out the caller's repository, so ./paths resolve there, not here.
# Scripts are fetched by explicitly checking out NorseArchitecture/.github.
# GITHUB_TOKEN in workflow_call is caller-scoped; secrets.token (SCATTER_PAT)
# covers cross-repo reads (.github) and writes (Yggdrasil).

on:
  workflow_call:
    secrets:
      token:
        required: true

jobs:
  update-cpm:
    if: ${{ !contains(github.ref_name, '-') }}
    runs-on: ubuntu-latest
    steps:
      - name: Checkout .github scripts
        uses: actions/checkout@v7
        with:
          repository: NorseArchitecture/.github
          token: ${{ secrets.token }}
          path: github-src

      - name: Checkout Yggdrasil
        uses: actions/checkout@v7
        with:
          repository: NorseArchitecture/Yggdrasil
          token: ${{ secrets.token }}
          path: yggdrasil

      - name: Configure git identity
        run: |
          git -C yggdrasil config user.name "github-actions[bot]"
          git -C yggdrasil config user.email "github-actions[bot]@users.noreply.github.com"

      - name: Phone home
        env:
          GH_TOKEN: ${{ secrets.token }}
          REALM: ${{ github.event.repository.name }}
          VERSION: ${{ github.ref_name }}
        run: pwsh github-src/scripts/phone-home-nuget.ps1 -Realm "$env:REALM" -Tag "$env:VERSION" -YggdrasilPath yggdrasil
```

- [ ] **Step 2: Validate YAML structure**

```bash
pwsh -c "
  \$raw = Get-Content .github/.github/workflows/phone-home-nuget.yml -Raw
  if (\$raw -match 'workflow_call' -and \$raw -match 'update-cpm' -and \$raw -match 'phone-home-nuget.ps1') {
    Write-Host 'YAML structure check passed'
  } else {
    throw 'Missing expected keys'
  }
"
```

Expected: `YAML structure check passed`

- [ ] **Step 3: Stage and review**

```bash
git -C .github add .github/workflows/phone-home-nuget.yml
git -C .github diff --staged
```

Expected: new file with the full workflow above. Stop — human commits.

---

## Task 3: Add `phone-home-nuget.ps1` script

**Files:**
- Create: `.github/scripts/phone-home-nuget.ps1`

**Context:** The script derives the property element name from the realm name by interpolating `$($Realm)Version` (PowerShell subexpression syntax avoids `$RealmVersion` being misread as a single variable). `Set-Content -NoNewline` prevents adding a trailing newline the original file doesn't have. Force-with-lease push means a faster re-release of the same realm updates the existing open PR rather than opening a duplicate.

- [ ] **Step 1: Write a DryRun invocation against the Yggdrasil props file to verify it finds the property** (test fixture already created in Task 1)

```bash
pwsh -c "
  # Simulate what the workflow passes
  \$env:REALM = 'Svartalfheim'
  \$env:TAG   = 'v0.0.2'
  # Script doesn't exist yet — this should fail
  pwsh .github/scripts/phone-home-nuget.ps1 -Realm Svartalfheim -Tag v0.0.2 -YggdrasilPath Yggdrasil -DryRun
" 2>&1 || true
```

Expected: error — script file not found. This is the failing test.

- [ ] **Step 2: Create the script**

`.github/scripts/phone-home-nuget.ps1`:
```powershell
#!/usr/bin/env pwsh
#
# phone-home-nuget.ps1
#
# Updates <{Realm}Version> in Yggdrasil/Directory.Packages.props and opens
# an auto-merge PR. Idempotent: force-pushes onto the existing branch so a
# faster re-release updates the open PR rather than opening a duplicate.
# Skips pre-release versions (any tag containing '-').
#
# Requirements:
#   GH_TOKEN env var — PAT with repo scope on NorseArchitecture/Yggdrasil
#   git user.name and user.email configured inside $YggdrasilPath (workflow step sets these)
#
# Usage:
#   pwsh scripts/phone-home-nuget.ps1 -Realm Svartalfheim -Tag v0.0.2 -YggdrasilPath ./yggdrasil
#   pwsh scripts/phone-home-nuget.ps1 -Realm Svartalfheim -Tag v0.0.2 -YggdrasilPath ./yggdrasil -DryRun

param(
    [Parameter(Mandatory)]
    [string]$Realm,

    [Parameter(Mandatory)]
    [string]$Tag,

    [Parameter(Mandatory)]
    [string]$YggdrasilPath,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$Org      = 'NorseArchitecture'
$Target   = 'Yggdrasil'
$Version  = $Tag.TrimStart('v')
$Branch   = "update/cpm/$($Realm.ToLower())"
$PropFile = Join-Path $YggdrasilPath 'Directory.Packages.props'

# Belt-and-suspenders: skip pre-release even if the workflow if: condition missed it
if ($Version.Contains('-')) {
    Write-Host "==> Pre-release $Version — skipping phone home."
    exit 0
}

if (-not (Test-Path $PropFile)) {
    throw "Directory.Packages.props not found at $PropFile. Create it in Yggdrasil before the first release."
}

$Content = Get-Content $PropFile -Raw
$Pattern = "<$($Realm)Version>[^<]*</$($Realm)Version>"
$Replace  = "<$($Realm)Version>$Version</$($Realm)Version>"

if ($Content -notmatch $Pattern) {
    throw "<$($Realm)Version> property not found in Directory.Packages.props. Add it before the first release."
}

$Updated = $Content -replace $Pattern, $Replace

if ($Content -eq $Updated) {
    Write-Host "==> $Realm already at $Version — nothing to do."
    exit 0
}

if ($DryRun) {
    Write-Host "[DRY RUN] Would update <$($Realm)Version> to $Version on branch $Branch → master in $Target."
    exit 0
}

Push-Location $YggdrasilPath
try {
    $RemoteBranchExists = git ls-remote --heads origin $Branch 2>$null
    if ($RemoteBranchExists) {
        git fetch origin $Branch --quiet
        git checkout -b $Branch "origin/$Branch" --quiet
    } else {
        git checkout -b $Branch --quiet
    }
    if ($LASTEXITCODE -ne 0) { throw "Branch checkout failed (exit $LASTEXITCODE)" }

    Set-Content $PropFile $Updated -NoNewline

    git add Directory.Packages.props
    git diff --cached --quiet
    if ($LASTEXITCODE -eq 0) {
        Write-Host "==> No effective change after edit — nothing to commit."
        exit 0
    }

    git commit -m "update: $Realm → $Version" --quiet
    if ($LASTEXITCODE -ne 0) { throw "git commit failed (exit $LASTEXITCODE)" }

    git push origin $Branch --force-with-lease --quiet
    if ($LASTEXITCODE -ne 0) { throw "git push failed (exit $LASTEXITCODE)" }

    $PrNumber = gh pr list `
        --repo "$Org/$Target" `
        --head $Branch `
        --state open `
        --json number `
        --jq '.[0].number'

    if (-not $PrNumber) {
        Write-Host "==> Opening PR..."
        $PrUrl = gh pr create `
            --repo "$Org/$Target" `
            --base master `
            --head $Branch `
            --title "update: $Realm $Version" `
            --body "Bumps ``<$($Realm)Version>`` to ``$Version`` in ``Directory.Packages.props``. Triggered by [$Org/$Realm@$Tag](https://github.com/$Org/$Realm/releases/tag/$Tag)."
        if ($LASTEXITCODE -ne 0) { throw "gh pr create failed (exit $LASTEXITCODE)" }
        Write-Host "==> PR: $PrUrl"

        gh pr merge $Branch --auto --merge --repo "$Org/$Target"
        if ($LASTEXITCODE -ne 0) { throw "gh pr merge --auto failed (exit $LASTEXITCODE)" }
        Write-Host "==> Auto-merge armed."
    } else {
        Write-Host "==> Updated existing PR #$PrNumber."
    }
} finally {
    Pop-Location
}

Write-Host "==> Done."
```

- [ ] **Step 3: DryRun test — stable version found**

```bash
pwsh .github/scripts/phone-home-nuget.ps1 -Realm Svartalfheim -Tag v0.0.2 -YggdrasilPath Yggdrasil -DryRun
```

Expected: `[DRY RUN] Would update <SvartalfheimVersion> to 0.0.2 on branch update/cpm/svartalfheim → master in Yggdrasil.`

- [ ] **Step 4: Pre-release guard test**

```bash
pwsh .github/scripts/phone-home-nuget.ps1 -Realm Svartalfheim -Tag v0.0.2-beta001 -YggdrasilPath Yggdrasil -DryRun
```

Expected: `==> Pre-release 0.0.2-beta001 — skipping phone home.` (exits 0)

- [ ] **Step 5: Missing property test**

```bash
$tmpDir = New-TemporaryFile | ForEach-Object { Remove-Item $_; New-Item -ItemType Directory -Path "$($_.FullName)-dir" }
'<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup></Project>' | Set-Content "$tmpDir/Directory.Packages.props"
pwsh .github/scripts/phone-home-nuget.ps1 -Realm Svartalfheim -Tag v0.0.2 -YggdrasilPath $tmpDir -DryRun 2>&1 || true
```

Expected: error message containing `<SvartalfheimVersion> property not found` (exits non-zero)

- [ ] **Step 6: Missing props file test**

```bash
pwsh .github/scripts/phone-home-nuget.ps1 -Realm Svartalfheim -Tag v0.0.2 -YggdrasilPath /tmp/does-not-exist -DryRun 2>&1 || true
```

Expected: error message containing `Directory.Packages.props not found` (exits non-zero)

- [ ] **Step 7: Idempotency test — already at target version**

Create a temp copy of the props file with the version already set, then confirm the script exits cleanly:

```bash
$tmpDir = New-TemporaryFile | ForEach-Object { Remove-Item $_; New-Item -ItemType Directory -Path "$($_.FullName)-dir" }
(Get-Content Yggdrasil/Directory.Packages.props -Raw) -replace '<SvartalfheimVersion>0\.0\.0</SvartalfheimVersion>', '<SvartalfheimVersion>0.0.2</SvartalfheimVersion>' | Set-Content "$tmpDir/Directory.Packages.props" -NoNewline
pwsh .github/scripts/phone-home-nuget.ps1 -Realm Svartalfheim -Tag v0.0.2 -YggdrasilPath $tmpDir -DryRun
```

Expected: `==> Svartalfheim already at 0.0.2 — nothing to do.` (exits 0)

- [ ] **Step 8: Stage and review**

```bash
git -C .github add scripts/phone-home-nuget.ps1
git -C .github diff --staged
```

Expected: new file with full script above. Stop — human commits.

---

## Task 4: Wire Svartalfheim's `release.yml`

**Files:**
- Modify: `Svartalfheim/.github/workflows/release.yml`

**Context:** `needs: [release]` blocks `phone-home` until all three jobs inside `release-nuget.yml` (build-test, codeql, pack-and-publish) complete successfully. The `phone-home` job itself inherits the `permissions` block from the workflow level. The `SCATTER_PAT` org-level secret provides the PAT with cross-repo write access; see Task 6 for secret audit.

- [ ] **Step 1: Add the `phone-home` job**

`Svartalfheim/.github/workflows/release.yml` — replace entire file:
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

  phone-home:
    needs: [release]
    uses: NorseArchitecture/.github/.github/workflows/phone-home-nuget.yml@master
    secrets:
      token: ${{ secrets.SCATTER_PAT }}
```

- [ ] **Step 2: Stage and review**

```bash
git -C Svartalfheim add .github/workflows/release.yml
git -C Svartalfheim diff --staged
```

Expected: only the `phone-home` job block added below the existing `release` job. Stop — human commits.

---

## Task 5: Create `release.yml` for the remaining six NuGet realms

**Files:**
- Create: `Asgard/.github/workflows/release.yml`
- Create: `Midgard/.github/workflows/release.yml`
- Create: `Urdarbrunnr/.github/workflows/release.yml`
- Create: `Ratatoskr/.github/workflows/release.yml`
- Create: `Himinbjorg/.github/workflows/release.yml`
- Create: `Heimdall/.github/workflows/release.yml`

**Context:** These realms have no `.github/workflows/` directory yet. The release.yml is identical in shape across all six — thin callers with both jobs. The workflow only fires on tag pushes, so creating it now causes no build activity until the realm has packages to release and someone pushes a tag. Create the `.github/workflows/` directory as needed.

- [ ] **Step 1: Create all six files**

For each of Asgard, Midgard, Urdarbrunnr, Ratatoskr, Himinbjorg, Heimdall — the content is identical:

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

  phone-home:
    needs: [release]
    uses: NorseArchitecture/.github/.github/workflows/phone-home-nuget.yml@master
    secrets:
      token: ${{ secrets.SCATTER_PAT }}
```

Create for each realm (run from the Bifrost workspace root):

```bash
for realm in Asgard Midgard Urdarbrunnr Ratatoskr Himinbjorg Heimdall; do
  mkdir -p "$realm/.github/workflows"
  cat > "$realm/.github/workflows/release.yml" << 'EOF'
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

  phone-home:
    needs: [release]
    uses: NorseArchitecture/.github/.github/workflows/phone-home-nuget.yml@master
    secrets:
      token: ${{ secrets.SCATTER_PAT }}
EOF
  echo "Created $realm/.github/workflows/release.yml"
done
```

- [ ] **Step 2: Stage and review all six**

```bash
for realm in Asgard Midgard Urdarbrunnr Ratatoskr Himinbjorg Heimdall; do
  git -C "$realm" add .github/workflows/release.yml
  echo "=== $realm ===" && git -C "$realm" diff --staged
done
```

Expected: six identical new files, one per realm. Stop — human commits each realm (or batches them — human decides).

---

## Task 6: Audit `SCATTER_PAT` secret availability

**Context:** The phone-home job in each realm's `release.yml` references `secrets.SCATTER_PAT`. This secret must be accessible as an org-level secret to every NuGet realm repo. Currently `SCATTER_PAT` is confirmed available in `NorseArchitecture/.github` (scatter uses it). Whether it's org-level (available to all repos) or repo-level (only `.github`) must be verified.

**This task is manual — no code to write.**

- [ ] **Step 1: Check secret scope in GitHub org settings**

Navigate to: `https://github.com/organizations/NorseArchitecture/settings/secrets/actions`

Look for `SCATTER_PAT` in the list.

- [ ] **Step 2a: If `SCATTER_PAT` is already org-level with access to all repos** → no action needed. Note this in a comment and move on.

- [ ] **Step 2b: If `SCATTER_PAT` is repo-level (only `.github`) or org-level but restricted to specific repos** → promote or expand:

  Navigate to the secret's settings. Change **Repository access** to **All repositories** (or add each NuGet realm explicitly: Svartalfheim, Asgard, Midgard, Urdarbrunnr, Ratatoskr, Himinbjorg, Heimdall).

- [ ] **Step 3: Confirm each NuGet realm can see the secret**

The fastest confirmation is the smoke test (Task 7) — if Svartalfheim's phone-home job can authenticate to Yggdrasil, the secret is wired correctly.

---

## Task 7: Smoke test — Svartalfheim `v0.0.2`

**Context:** Yggdrasil currently has no CI workflow and no branch protection configured via `carve-the-laws.ps1`. With no required status checks, `gh pr merge --auto --merge` will fire as soon as the PR is created (nothing to wait for). This is the correct smoke-test behavior — proving the full chain before Yggdrasil has a build. Once Yggdrasil has a `.slnx` and a `ci.yml`, the auto-merge gate will become meaningful.

**This task is manual — a human pushes the tag.**

- [ ] **Step 1: Confirm all prior tasks are committed and pushed**

```bash
git -C Yggdrasil   log --oneline -3
git -C .github     log --oneline -3
git -C Svartalfheim log --oneline -3
```

Expected: commits for `Directory.Packages.props`, `phone-home-nuget.yml`, `phone-home-nuget.ps1`, and the Svartalfheim `release.yml` update all visible.

- [ ] **Step 2: Push the smoke-test tag on Svartalfheim**

From the Bifrost workspace, in the Svartalfheim submodule:

```bash
git -C Svartalfheim tag -a v0.0.2 -m "smoke test: phone-home CPM machinery"
git -C Svartalfheim push origin v0.0.2
```

- [ ] **Step 3: Observe the release ceremony on GitHub**

Navigate to `https://github.com/NorseArchitecture/Svartalfheim/actions`.

Expected sequence:
1. `Release` workflow fires on tag `v0.0.2`.
2. `release` job (calls `release-nuget.yml`) completes: build-test → codeql → pack-and-publish → GitHub Release created, `Norse.Primitives 0.0.2` on GitHub Packages.
3. `phone-home` job fires after `release` succeeds. Workflow log shows: checkout `.github`, checkout Yggdrasil, script output `==> PR: https://github.com/NorseArchitecture/Yggdrasil/pull/N — auto-merge armed.`

- [ ] **Step 4: Confirm the PR on Yggdrasil**

Navigate to `https://github.com/NorseArchitecture/Yggdrasil/pulls`.

Expected:
- PR titled `update: Svartalfheim 0.0.2` on branch `update/cpm/svartalfheim`.
- PR body links back to the `v0.0.2` release.
- Auto-merge badge visible.
- PR merges automatically (no required checks → fires immediately).

- [ ] **Step 5: Confirm `Directory.Packages.props` on Yggdrasil master**

```bash
git -C Yggdrasil fetch origin master
git -C Yggdrasil show origin/master:Directory.Packages.props | grep SvartalfheimVersion
```

Expected: `<SvartalfheimVersion>0.0.2</SvartalfheimVersion>`

- [ ] **Step 6: Confirm idempotency — re-run would be a no-op**

```bash
pwsh .github/scripts/phone-home-nuget.ps1 -Realm Svartalfheim -Tag v0.0.2 -YggdrasilPath Yggdrasil -DryRun
```

Expected: `==> Svartalfheim already at 0.0.2 — nothing to do.`

Smoke test complete. The full chain is proven.
