# Platform Config Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (default) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Automate propagation of canonical platform config files from `.github/config/` to every realm as auto-merge PRs triggered on push.

**Architecture:** Canonical files live in `.github/config/` mirroring their target path in each realm. A PowerShell manifest maps realms to file groups. A GitHub Actions workflow fires on `config/**` changes and runs `scatter-the-runes.ps1`, which clones each realm, copies files, opens a PR, and arms auto-merge. Happy-path cost: zero human clicks after the initial push.

**Tech Stack:** PowerShell 7+ (`pwsh`), `gh` CLI, GitHub Actions, PowerShell Data Files (`.psd1`), `git` CLI.

## Global Constraints

- Working repo for all new files: `NorseArchitecture/.github` (local path: `../../../.github` relative to this plan, or `/home/buvy/code/NorseArchitecture/.github`)
- Org: `NorseArchitecture`
- Sync branch name (fixed, per realm): `sync/platform-config`
- PAT secret name: `SCATTER_PAT` (org-level secret)
- Auto-merge method: `--merge` (not squash, not rebase)
- Workflow trigger path filter: `config/**`
- Failure behavior: attempt every realm, collect failures, report at end, exit non-zero — never abort early
- `$ErrorActionPreference = 'Stop'` throughout the script
- Canonical source for existing files: `../Bifrost/Svartalfheim/` (spot-checked identical to Asgard on all files except `.gitignore`, which Svartalfheim has the more complete version of)
- US English spelling in all code, comments, and commit messages

---

## File Map

**Create in `NorseArchitecture/.github`:**

| Path | Description |
|---|---|
| `config/manifest.psd1` | Realm-to-group mapping |
| `config/.editorconfig` | Canonical editor config (copy from Svartalfheim) |
| `config/.gitattributes` | Canonical git attributes (copy from Svartalfheim) |
| `config/.gitignore` | Canonical gitignore (copy from Svartalfheim — most complete) |
| `config/Directory.Build.props` | Canonical root MSBuild props (copy from Svartalfheim) |
| `config/global.json` | Canonical SDK pin (copy from Svartalfheim) |
| `config/LICENSE` | Canonical license (copy from Svartalfheim) |
| `config/src/Directory.Build.props` | Canonical NuGet src props (copy from Svartalfheim) |
| `config/tests/Directory.Build.props` | Canonical NuGet test props (copy from Svartalfheim) |
| `scripts/scatter-the-runes.ps1` | Fan-out script |
| `.github/workflows/scatter-the-runes.yml` | Actions trigger |

**Modify in `NorseArchitecture/.github`:**

| Path | Change |
|---|---|
| `scripts/carve-the-laws.ps1:106` | Add `-F allow_auto_merge=true` to the existing PATCH call |

---

## Task 1: Wire `allow_auto_merge` into `carve-the-laws.ps1`

`gh pr merge --auto` requires the target repo to have auto-merge enabled. `carve-the-laws.ps1` already PATCHes each repo's settings; add the flag there so a single re-run wires every realm.

**Files:**
- Modify: `scripts/carve-the-laws.ps1` (lines 105–106)

- [ ] **Step 1: Open `carve-the-laws.ps1` and locate the PATCH call**

Find these two lines (currently at 105–106):
```powershell
	gh api --method PATCH "repos/$Org/$Repo" `
		-F delete_branch_on_merge=true | Out-Null
```

- [ ] **Step 2: Add `allow_auto_merge`**

Replace those two lines with:
```powershell
	gh api --method PATCH "repos/$Org/$Repo" `
		-F delete_branch_on_merge=true `
		-F allow_auto_merge=true | Out-Null
```

- [ ] **Step 3: Verify the script still parses cleanly**

Run from inside the `.github` repo:
```powershell
pwsh -NoProfile -Command "& { . './scripts/carve-the-laws.ps1' -WhatIf }" 2>&1 | Select-Object -First 5
```
Expected: either a `WhatIf` preview or a complaint about missing `gh` auth — not a parse error.

Actually PowerShell doesn't support `-WhatIf` on scripts without explicit support. Instead verify parse only:
```powershell
pwsh -NoProfile -File ./scripts/carve-the-laws.ps1 --% --help 2>&1; echo "exit: $LASTEXITCODE"
```
Expected: script starts, then fails on `gh auth` (if not authenticated) or shows usage — either way, no syntax error exit.

Simpler parse check:
```powershell
pwsh -NoProfile -Command "Get-Content ./scripts/carve-the-laws.ps1 | Out-Null; [System.Management.Automation.Language.Parser]::ParseFile('./scripts/carve-the-laws.ps1', [ref]$null, [ref]$errors); echo \"Parse errors: $($errors.Count)\""
```
Expected output: `Parse errors: 0`

- [ ] **Step 4: Commit**

```bash
git add scripts/carve-the-laws.ps1
git commit -m "feat: enable allow_auto_merge on all realm repos"
```

- [ ] **Step 5: Run `carve-the-laws.ps1` to propagate the setting**

This is a manual step requiring `gh` auth. Run from the `.github` repo:
```
! pwsh scripts/carve-the-laws.ps1
```
Expected: `==> NorseArchitecture/Asgard ... Repo settings applied. ... The laws are carved.`

The `allow_auto_merge=true` PATCH is idempotent — re-running is safe.

---

## Task 2: Scaffold canonical config files

Copy the proven files from Svartalfheim into `.github/config/`. These are the source of truth going forward; the copy direction is Svartalfheim → `.github`, never the reverse.

**Files:**
- Create: `config/.editorconfig`, `config/.gitattributes`, `config/.gitignore`, `config/Directory.Build.props`, `config/global.json`, `config/LICENSE`, `config/src/Directory.Build.props`, `config/tests/Directory.Build.props`

All commands run from the `.github` repo root (`/home/buvy/code/NorseArchitecture/.github`).

- [ ] **Step 1: Create the directory structure**

```bash
mkdir -p config/src config/tests
```

- [ ] **Step 2: Copy the root-level files**

```bash
cp ../Bifrost/Svartalfheim/.editorconfig      config/.editorconfig
cp ../Bifrost/Svartalfheim/.gitattributes     config/.gitattributes
cp ../Bifrost/Svartalfheim/.gitignore         config/.gitignore
cp ../Bifrost/Svartalfheim/Directory.Build.props  config/Directory.Build.props
cp ../Bifrost/Svartalfheim/global.json        config/global.json
cp ../Bifrost/Svartalfheim/LICENSE            config/LICENSE
```

- [ ] **Step 3: Copy the subdirectory props files**

```bash
cp ../Bifrost/Svartalfheim/src/Directory.Build.props    config/src/Directory.Build.props
cp ../Bifrost/Svartalfheim/tests/Directory.Build.props  config/tests/Directory.Build.props
```

- [ ] **Step 4: Verify all 8 files exist**

```bash
find config -type f | sort
```
Expected (9 files including the manifest placeholder that does not exist yet — 8 until Task 3):
```
config/.editorconfig
config/.gitattributes
config/.gitignore
config/Directory.Build.props
config/global.json
config/LICENSE
config/src/Directory.Build.props
config/tests/Directory.Build.props
```

- [ ] **Step 5: Spot-check that the copy was faithful**

```bash
diff config/.editorconfig ../Bifrost/Svartalfheim/.editorconfig && echo "OK"
diff config/.gitignore    ../Bifrost/Svartalfheim/.gitignore    && echo "OK"
```
Expected: `OK` for both (no diff output).

- [ ] **Step 6: Commit**

```bash
git add config/
git commit -m "feat: scaffold canonical platform config files"
```

---

## Task 3: Create `manifest.psd1`

The manifest is the single file that controls which realms receive which files. All future realm additions and file-group changes land here.

**Files:**
- Create: `config/manifest.psd1`

- [ ] **Step 1: Write `config/manifest.psd1`**

```powershell
# manifest.psd1 — Platform config sync manifest
#
# Groups define collections of files; Realms list which groups they receive.
# Files are deduplicated across groups — a realm assigned 'universal' does not
# also need 'git' (universal already contains those files).
#
# Reserved slots (uncomment when UseProjectReferences feature lands):
#   nuget: 'src/Directory.Build.targets' and 'tests/Directory.Build.targets'
#   Place the canonical .targets files at config/src/ and config/tests/ first.
@{
	Groups = @{
		# Git hygiene only — repos without a .NET build
		git       = @(
			'.gitattributes'
			'.gitignore'
		)
		# Full .NET platform baseline
		universal = @(
			'.editorconfig'
			'.gitattributes'
			'.gitignore'
			'global.json'
			'LICENSE'
		)
		# Root MSBuild props — repos with a .NET build but not shipping to NuGet
		dotnet    = @(
			'Directory.Build.props'
		)
		# NuGet packaging props — repos that ship NuGet packages
		nuget     = @(
			'src/Directory.Build.props'
			'tests/Directory.Build.props'
			# 'src/Directory.Build.targets'   # UseProjectReferences — pending
			# 'tests/Directory.Build.targets' # UseProjectReferences — pending
		)
	}
	Realms = @{
		# NuGet-shipping platform realms
		Svartalfheim = @('universal', 'dotnet', 'nuget')
		Asgard       = @('universal', 'dotnet', 'nuget')
		Midgard      = @('universal', 'dotnet', 'nuget')
		Urdarbrunnr  = @('universal', 'dotnet', 'nuget')
		Ratatoskr    = @('universal', 'dotnet', 'nuget')
		Heimdall     = @('universal', 'dotnet', 'nuget')
		Himinbjorg   = @('universal', 'dotnet', 'nuget')
		# Runtime host — universal + dotnet; owns its own src/ and tests/ props
		# (no IsAotCompatible=true, uses CPM — incompatible with nuget group files)
		Yggdrasil    = @('universal', 'dotnet')
		# Aspire composition root — universal only; owns its own minimal host props
		Bifrost      = @('universal')
		# Design system — no .NET tooling; crafts its own .editorconfig
		Nagalfar     = @('git')
		# Docs and proofs of concept — git hygiene only
		Glitnir      = @('git')
	}
}
```

- [ ] **Step 2: Verify the manifest loads cleanly in PowerShell**

```powershell
pwsh -NoProfile -Command "
  \$m = Import-PowerShellDataFile './config/manifest.psd1'
  Write-Host 'Groups:' \$m.Groups.Keys
  Write-Host 'Realms:' \$m.Realms.Keys
  Write-Host 'Svartalfheim files:' (\$m.Realms.Svartalfheim | ForEach-Object { \$m.Groups[\$_] } | Sort-Object -Unique)
"
```
Expected output (order may vary):
```
Groups: git universal dotnet nuget
Realms: Svartalfheim Asgard Midgard Urdarbrunnr Ratatoskr Heimdall Himinbjorg Yggdrasil Bifrost Nagalfar Glitnir
Svartalfheim files: .editorconfig .gitattributes .gitignore Directory.Build.props LICENSE global.json src/Directory.Build.props tests/Directory.Build.props
```

- [ ] **Step 3: Commit**

```bash
git add config/manifest.psd1
git commit -m "feat: add platform config sync manifest"
```

---

## Task 4: Write `scatter-the-runes.ps1`

The core script. Written DryRun-first: implement just enough to verify manifest loading and file-list computation before adding the GitHub interactions.

**Files:**
- Create: `scripts/scatter-the-runes.ps1`

- [ ] **Step 1: Write the DryRun-capable script**

Create `scripts/scatter-the-runes.ps1`:

```powershell
#!/usr/bin/env pwsh
#
# scatter-the-runes.ps1
#
# Fans canonical config files from config/ to every realm in the manifest as
# auto-merge PRs. Idempotent: re-running pushes a new commit onto any existing
# sync/platform-config branch, updating open PRs without creating duplicates.
#
# Requirements:
#   GH_TOKEN — PAT with repo scope (set in CI via SCATTER_PAT secret;
#              locally via `gh auth login` or env var)
#   git user.name and user.email configured (the workflow step sets these)
#
# Usage:
#   pwsh scripts/scatter-the-runes.ps1                  # all realms
#   pwsh scripts/scatter-the-runes.ps1 Svartalfheim     # one realm
#   pwsh scripts/scatter-the-runes.ps1 -DryRun          # print plan, no writes

param(
	[Parameter(ValueFromRemainingArguments)]
	[string[]]$Realms,
	[switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$Org       = 'NorseArchitecture'
$Branch    = 'sync/platform-config'
$ConfigDir = Join-Path $PSScriptRoot '../config'
$Manifest  = Import-PowerShellDataFile (Join-Path $ConfigDir 'manifest.psd1')

function Get-RealmFiles {
	param([string]$RealmName)
	$Files = [System.Collections.Generic.SortedSet[string]]::new(
		[System.StringComparer]::OrdinalIgnoreCase)
	foreach ($GroupName in $Manifest.Realms[$RealmName]) {
		foreach ($File in $Manifest.Groups[$GroupName]) {
			[void]$Files.Add($File)
		}
	}
	@($Files)
}

$TargetRealms = if ($Realms) { $Realms } else { $Manifest.Realms.Keys | Sort-Object }
$Failures     = @()

foreach ($Realm in $TargetRealms) {
	if (-not $Manifest.Realms.ContainsKey($Realm)) {
		Write-Warning "==> $Realm not in manifest — skipping"
		continue
	}

	$Files = Get-RealmFiles $Realm
	Write-Host "==> $Org/$Realm ($($Files.Count) files)"

	if ($DryRun) {
		Write-Host "    [DRY RUN] Would sync: $($Files -join ', ')"
		continue
	}

	$TempDir = Join-Path ([System.IO.Path]::GetTempPath()) "scatter-$Realm-$(New-Guid)"

	try {
		Write-Host '    Cloning...'
		gh repo clone "$Org/$Realm" $TempDir -- --depth 1 --quiet
		if ($LASTEXITCODE -ne 0) { throw "gh repo clone failed (exit $LASTEXITCODE)" }

		Push-Location $TempDir

		$RemoteBranchExists = git ls-remote --heads origin $Branch 2>$null
		if ($RemoteBranchExists) {
			git checkout -b $Branch "origin/$Branch" --quiet
		} else {
			git checkout -b $Branch --quiet
		}
		if ($LASTEXITCODE -ne 0) { throw "branch checkout failed (exit $LASTEXITCODE)" }

		foreach ($File in $Files) {
			$Source  = Join-Path $ConfigDir $File
			$Dest    = Join-Path $TempDir $File
			$DestDir = Split-Path -Parent $Dest
			if (-not (Test-Path $DestDir)) {
				New-Item -ItemType Directory -Path $DestDir | Out-Null
			}
			Copy-Item -Path $Source -Destination $Dest -Force
		}

		git add --all
		git diff --cached --quiet
		if ($LASTEXITCODE -eq 0) {
			Write-Host '    No changes — skipping.'
			continue
		}

		git commit -m 'sync: platform config from .github' --quiet
		if ($LASTEXITCODE -ne 0) { throw "git commit failed (exit $LASTEXITCODE)" }

		git push origin $Branch --force-with-lease --quiet
		if ($LASTEXITCODE -ne 0) { throw "git push failed (exit $LASTEXITCODE)" }

		$PrNumber = gh pr list `
			--repo "$Org/$Realm" `
			--head $Branch `
			--state open `
			--json number `
			--jq '.[0].number'

		if (-not $PrNumber) {
			Write-Host '    Opening PR...'
			$PrUrl = gh pr create `
				--repo "$Org/$Realm" `
				--base master `
				--head $Branch `
				--title 'sync: platform config from .github' `
				--body 'Automated sync of canonical platform config files from the [.github](https://github.com/NorseArchitecture/.github) repo. Managed by ``config/manifest.psd1``.'
			if ($LASTEXITCODE -ne 0) { throw "gh pr create failed (exit $LASTEXITCODE)" }
			Write-Host "    PR: $PrUrl"

			gh pr merge $Branch --auto --merge --repo "$Org/$Realm"
			if ($LASTEXITCODE -ne 0) { throw "gh pr merge --auto failed (exit $LASTEXITCODE)" }
			Write-Host '    Auto-merge armed.'
		} else {
			Write-Host "    Updated existing PR #$PrNumber."
		}

		Write-Host '    Done.'

	} catch {
		Write-Error -ErrorAction Continue "    FAILED: $_"
		$Failures += $Realm
	} finally {
		if ((Get-Location).Path -eq $TempDir) { Pop-Location }
		Remove-Item $TempDir -Recurse -Force -ErrorAction SilentlyContinue
	}
}

Write-Host
if ($Failures.Count -gt 0) {
	Write-Error -ErrorAction Continue "The runes were not scattered in: $($Failures -join ' ')"
	exit 1
}

Write-Host 'The runes are scattered.'
```

- [ ] **Step 2: Parse-check the script**

```powershell
pwsh -NoProfile -Command "
  \$errors = \$null
  [System.Management.Automation.Language.Parser]::ParseFile(
    './scripts/scatter-the-runes.ps1', [ref]\$null, [ref]\$errors)
  Write-Host \"Parse errors: \$(\$errors.Count)\"
"
```
Expected: `Parse errors: 0`

- [ ] **Step 3: Run DryRun to verify manifest loading and file-list computation**

```powershell
pwsh scripts/scatter-the-runes.ps1 -DryRun
```

Expected output (order of realms is alphabetical; files within a realm are sorted):
```
==> NorseArchitecture/Asgard (8 files)
    [DRY RUN] Would sync: .editorconfig, .gitattributes, .gitignore, Directory.Build.props, global.json, LICENSE, src/Directory.Build.props, tests/Directory.Build.props
==> NorseArchitecture/Bifrost (5 files)
    [DRY RUN] Would sync: .editorconfig, .gitattributes, .gitignore, global.json, LICENSE
==> NorseArchitecture/Glitnir (2 files)
    [DRY RUN] Would sync: .gitattributes, .gitignore
==> NorseArchitecture/Heimdall (8 files)
    [DRY RUN] Would sync: .editorconfig, .gitattributes, .gitignore, Directory.Build.props, global.json, LICENSE, src/Directory.Build.props, tests/Directory.Build.props
==> NorseArchitecture/Himinbjorg (8 files)
    [DRY RUN] Would sync: .editorconfig, .gitattributes, .gitignore, Directory.Build.props, global.json, LICENSE, src/Directory.Build.props, tests/Directory.Build.props
==> NorseArchitecture/Midgard (8 files)
    [DRY RUN] Would sync: .editorconfig, .gitattributes, .gitignore, Directory.Build.props, global.json, LICENSE, src/Directory.Build.props, tests/Directory.Build.props
==> NorseArchitecture/Nagalfar (2 files)
    [DRY RUN] Would sync: .gitattributes, .gitignore
==> NorseArchitecture/Ratatoskr (8 files)
    [DRY RUN] Would sync: .editorconfig, .gitattributes, .gitignore, Directory.Build.props, global.json, LICENSE, src/Directory.Build.props, tests/Directory.Build.props
==> NorseArchitecture/Svartalfheim (8 files)
    [DRY RUN] Would sync: .editorconfig, .gitattributes, .gitignore, Directory.Build.props, global.json, LICENSE, src/Directory.Build.props, tests/Directory.Build.props
==> NorseArchitecture/Urdarbrunnr (8 files)
    [DRY RUN] Would sync: .editorconfig, .gitattributes, .gitignore, Directory.Build.props, global.json, LICENSE, src/Directory.Build.props, tests/Directory.Build.props
==> NorseArchitecture/Yggdrasil (6 files)
    [DRY RUN] Would sync: .editorconfig, .gitattributes, .gitignore, Directory.Build.props, global.json, LICENSE

The runes are scattered.
```

If any realm shows unexpected files or counts, check `config/manifest.psd1` and fix before proceeding.

- [ ] **Step 4: Commit**

```bash
git add scripts/scatter-the-runes.ps1
git commit -m "feat: add scatter-the-runes script"
```

---

## Task 5: Write `scatter-the-runes.yml` workflow

Wires the GitHub Actions trigger: fires automatically on any push to `master` that changes a file under `config/`.

**Files:**
- Create: `.github/workflows/scatter-the-runes.yml`

- [ ] **Step 1: Write `.github/workflows/scatter-the-runes.yml`**

```yaml
name: Scatter the Runes

on:
  push:
    branches: [master]
    paths:
      - 'config/**'

jobs:
  scatter:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Configure git identity
        run: |
          git config --global user.email "github-actions[bot]@users.noreply.github.com"
          git config --global user.name "github-actions[bot]"

      - name: Configure git credentials
        env:
          GH_TOKEN: ${{ secrets.SCATTER_PAT }}
        run: gh auth setup-git

      - name: Scatter the runes
        env:
          GH_TOKEN: ${{ secrets.SCATTER_PAT }}
        run: pwsh scripts/scatter-the-runes.ps1
```

Notes on the steps:
- `actions/checkout@v4` checks out the `.github` repo so `config/` and `scripts/` are available
- `Configure git identity` sets the author for the sync commits that appear in each realm's history
- `gh auth setup-git` configures git to use the `GH_TOKEN` credential for all `github.com` operations — required for `git push` across repos
- `GH_TOKEN` is set on both credential config and scatter steps; both need it

- [ ] **Step 2: Verify the YAML parses cleanly**

```bash
python3 -c "import yaml; yaml.safe_load(open('.github/workflows/scatter-the-runes.yml'))" && echo "YAML OK"
```
Expected: `YAML OK`

If `python3` is unavailable:
```bash
pwsh -Command "
  \$content = Get-Content '.github/workflows/scatter-the-runes.yml' -Raw
  Write-Host \"File length: \$(\$content.Length) chars — present and non-empty\"
"
```

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/scatter-the-runes.yml
git commit -m "feat: add scatter-the-runes GitHub Actions workflow"
```

---

## Task 6: Create `SCATTER_PAT` org secret (manual)

The workflow needs cross-repo write access. This step is manual — it requires browser access to GitHub settings.

**Prerequisites:** Tasks 1–5 must be merged or present on `master` of `.github` before the workflow can fire.

- [ ] **Step 1: Create a Personal Access Token**

1. Go to **github.com → your profile → Settings → Developer settings → Personal access tokens → Tokens (classic)**
2. Click **Generate new token (classic)**
3. Note: `SCATTER_PAT — NorseArchitecture platform config sync`
4. Expiration: set a rotation reminder (90 days or 1 year — your call)
5. Scopes: check `repo` (full repo access — needed for Contents write + Pull requests write across all realm repos)
6. Click **Generate token** and copy the value

- [ ] **Step 2: Add the token as an org-level Actions secret**

1. Go to **github.com/organizations/NorseArchitecture/settings/secrets/actions**
2. Click **New organization secret**
3. Name: `SCATTER_PAT`
4. Value: paste the token
5. Repository access: **All repositories** (so it is available in the `.github` repo's workflow)
6. Click **Add secret**

- [ ] **Step 3: Verify the secret is visible to the `.github` repo**

Go to `github.com/NorseArchitecture/.github/settings/secrets/actions` — `SCATTER_PAT` should appear in the list of available secrets.

---

## Task 7: Integration smoke test

Validate the full end-to-end pipeline: push a config change, watch PRs appear with auto-merge armed, verify idempotency.

- [ ] **Step 1: Push Tasks 1–5 to `master` if not already done**

All five commits should be on `master` of `.github` before this step. Confirm:
```bash
git log --oneline -5
```
Expected (newest first):
```
<sha> feat: add scatter-the-runes GitHub Actions workflow
<sha> feat: add scatter-the-runes script
<sha> feat: add platform config sync manifest
<sha> feat: scaffold canonical platform config files
<sha> feat: enable allow_auto_merge on all realm repos
```

- [ ] **Step 2: Trigger the workflow with a trivial config change**

Add a trailing newline to `config/.editorconfig` (or a harmless comment to `config/manifest.psd1`):
```bash
echo "" >> config/.editorconfig
git add config/.editorconfig
git commit -m "test: trigger scatter-the-runes smoke test"
git push
```

- [ ] **Step 3: Watch the workflow run**

Go to `github.com/NorseArchitecture/.github/actions`. The `Scatter the Runes` workflow should appear running. Monitor until it completes.

Expected result: green check, `The runes are scattered.` in the log.

- [ ] **Step 4: Verify PRs appeared in every realm**

Run from anywhere with `gh` auth:
```bash
gh pr list --repo NorseArchitecture/Svartalfheim --head sync/platform-config --state open
gh pr list --repo NorseArchitecture/Asgard        --head sync/platform-config --state open
gh pr list --repo NorseArchitecture/Glitnir       --head sync/platform-config --state open
```
Each should show one open PR titled `sync: platform config from .github` with auto-merge enabled (visible in the PR UI as "Auto-merge enabled").

For Svartalfheim and realms with identical files: the PR diff should show only the trailing newline added in Step 2.
For Asgard: the PR diff should also show `coverage-report/` being added to `.gitignore` (known bootstrap drift — expected, not a bug).

- [ ] **Step 5: Revert the trivial change and push**

```bash
git revert HEAD --no-edit
git push
```

Expected: the workflow fires again. Since all realm repos now have the trailing newline on `sync/platform-config`, pushing the revert means the canonical `.editorconfig` no longer has the newline. The script pushes a new commit to each realm's `sync/platform-config` branch. No new PRs are created; the existing PRs get a second commit (the revert). Verify:

```bash
gh pr view --repo NorseArchitecture/Svartalfheim --web
```
The PR should show two commits: the sync commit and the sync revert commit.

- [ ] **Step 6: Verify idempotency — second push with no changes produces no work**

Make a commit that touches a file NOT in `config/**`:
```bash
echo "# no-op" >> README.md
git add README.md
git commit -m "test: verify workflow path filter"
git push
```
Expected: the `Scatter the Runes` workflow does **not** appear in the Actions run list — the `paths: ['config/**']` filter suppressed it.

- [ ] **Step 7: Clean up test commits from `.github` history (optional)**

If the test commits are noise, squash or revert them before merging the smoke-test results. The realm PRs themselves are real and should be left to merge normally through CI.

---

## Notes on Failure Triage

If a realm's CI fails and auto-merge does not fire:

**Realm-side fix:** The canonical config is correct; the realm has a test or build assumption that conflicts. Push a fix directly to the realm's `sync/platform-config` branch. CI re-runs; auto-merge fires when it passes.

**Config-side fix:** The canonical config introduced the problem. Fix it in `.github/config/` and push to master. The workflow fires again, pushes a new commit to every open `sync/platform-config` branch, and CI re-runs across all affected realms.

**`--force-with-lease` rejection:** If someone has manually pushed commits to a realm's `sync/platform-config` branch after the automation last synced it, `git push --force-with-lease` will fail. This is intentional — the automation will not silently overwrite manual work. Delete the branch on the realm (`gh api --method DELETE repos/NorseArchitecture/$Realm/git/refs/heads/sync/platform-config`) and trigger a new scatter run.
