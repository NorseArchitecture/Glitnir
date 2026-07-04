# Realm Classification by Exception Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the hardcoded realm-name lists in `manifest.psd1` and `carve-the-laws.ps1` with live `gh repo list` discovery plus a small manage-by-exception map, so onboarding a new default (NuGet-shipping, gated) realm requires zero edits to either script.

**Architecture:** Both scripts dot-source a new shared helper (`scripts/lib/realm-classification.ps1`) that discovers org repos live and classifies each one — file groups for scatter, gate status for carve — by checking a single `Exceptions` map in `manifest.psd1`. Absence from that map means "default realm."

**Tech Stack:** PowerShell 7+ (`pwsh`), GitHub CLI (`gh`), `manifest.psd1` (PowerShell data file).

## Global Constraints

- Repo: `NorseArchitecture/.github` (Ginnungagap) — all file paths below are relative to its root.
- Tabs for indentation (platform-wide convention, `~/.claude/CLAUDE.md`).
- Scripts must remain idempotent — safe to re-run with no side effects when nothing changed (existing guarantee, must not regress).
- No automatic git commits — stage changes, human commits (Bifrost CLAUDE.md §6).
- `gh` calls require `GH_TOKEN`/`SCATTER_PAT` with `repo` scope **plus `read:org`** (new requirement introduced by this plan — document it in both scripts' header comments).
- No unit-test framework (Pester or otherwise) exists anywhere in this repo today. Verification for the pure classification functions uses ephemeral assertion scripts run via the shell during implementation (not committed) — introducing a permanent test framework for two small functions in an ops-script repo is out of scope. Verification for live-discovery behavior is a real run against the live `NorseArchitecture` org, per spec §9.

---

### Task 1: Rewrite `config/manifest.psd1` with the exception-based schema

**Files:**
- Modify: `config/manifest.psd1` (full rewrite)

**Interfaces:**
- Produces: `$Manifest.Groups` (unchanged shape), `$Manifest.DefaultGroups` (`string[]`), `$Manifest.ScatterExcludes` (`string[]`), `$Manifest.Exceptions` (`Hashtable<string, Hashtable>` — each value may contain `Groups` (`string[]`) and/or `Gated` (`bool`) keys).

- [ ] **Step 1: Replace the file contents**

Replace the entire contents of `config/manifest.psd1` with:

```powershell
# manifest.psd1 — Platform config sync manifest
#
# Groups define collections of files. Realm classification is by exception:
# any repo in the NorseArchitecture org not listed in Exceptions is a default
# realm — full NuGet-shipping group set, gated CI. Files are deduplicated
# across groups — a realm assigned 'universal' does not also need 'git'
# (universal already contains those files).

@{
	Groups = @{
		# Git hygiene only — repos without a .NET build
		git         = @(
			'.gitattributes'
			'.gitignore'
		)
		# Full .NET platform baseline
		universal   = @(
			'.editorconfig'
			'.gitattributes'
			'.gitignore'
			'LICENSE'
			'nuget.config'
		)
		# Shared SDK pin — separate from 'universal' so a realm can own its
		# own global.json (e.g. Bifrost layers a local msbuild-sdks entry)
		sdk         = @(
			'global.json'
		)
		# Root MSBuild props — repos with a .NET build but not shipping to NuGet
		dotnet      = @(
			'Directory.Build.props'
		)
		# NuGet packaging props — repos that ship NuGet packages. tests/Directory.Build.targets
		# lives here too (not in 'tests' below): same audience as src/Directory.Build.targets for
		# the same reason — both exist solely to resolve NorseRef (and now the generator-analyzer
		# strip target) via Bifrost's root Directory.Build.targets, which only matters for realms
		# that ship and consume NuGet packages across the platform.
		nuget       = @(
			'src/Directory.Build.props'
			'src/Directory.Build.targets'
			'tests/Directory.Build.targets'
			'.github/workflows/release.yml'
		)
		# Test project MSBuild props — repos with a .NET build and tests
		tests       = @(
			'tests/Directory.Build.props'
		)
		# CI workflows — all realms including Bifrost
		ci          = @(
			'.github/workflows/auto-approve.yml'
		)
		# Platform workflows — all realms except Bifrost (update-bifrost must not run in Bifrost)
		workflows   = @(
			'.github/workflows/update-bifrost.yml'
		)
	}
	# Default group set for any repo not named in Exceptions below.
	DefaultGroups   = @('universal', 'sdk', 'dotnet', 'nuget', 'tests', 'ci', 'workflows')
	# Repos scatter must never sync into — source of the config, not a consumer.
	ScatterExcludes = @('.github')
	# Anything NOT listed here is a default realm: ships to NuGet, full group
	# set, gated CI. Exception entries declare only the fields that differ
	# from default — an absent field falls back to DefaultGroups / Gated=$true.
	Exceptions      = @{
		# Runtime host — universal + dotnet + tests (props only, no 'nuget'); owns its own
		# src/Directory.Build.targets and tests/Directory.Build.targets (no IsAotCompatible=true,
		# uses CPM — incompatible with 'nuget' group files. See
		# ../Bifrost/Glitnir/docs/Platform/specs/2026-07-01-norseref-generator-forwarding-design.md)
		Yggdrasil = @{
			Groups = @('universal', 'sdk', 'dotnet', 'tests', 'ci', 'workflows')
		}
		# Aspire composition root — universal only; owns its own global.json
		# (local msbuild-sdks entry for Microsoft.Build.NoTargets, used by Glitnir's
		# doc-glob project since Glitnir has no global.json of its own). Ungated —
		# no gate / build CI check exists for an Aspire AppHost.
		Bifrost   = @{
			Groups = @('universal', 'ci')
			Gated  = $false
		}
		# Design system — no .NET tooling; crafts its own .editorconfig. Ungated.
		Naglfar   = @{
			Groups = @('git', 'ci', 'workflows')
			Gated  = $false
		}
		# Docs and proofs of concept — git hygiene only. Ungated.
		Glitnir   = @{
			Groups = @('git', 'ci', 'workflows')
			Gated  = $false
		}
		# Source of the canonical config — scatter excludes it outright (see
		# ScatterExcludes above); only its Gated classification is relevant here.
		'.github' = @{
			Gated = $false
		}
	}
}
```

- [ ] **Step 2: Verify the file still parses as valid PowerShell data**

Run: `pwsh -Command "Import-PowerShellDataFile config/manifest.psd1 | Format-List"`
Expected: prints `Groups`, `DefaultGroups`, `ScatterExcludes`, `Exceptions` — no parse error.

- [ ] **Step 3: Commit**

```bash
git add config/manifest.psd1
git commit -m "config: switch manifest to exception-based realm classification"
```

---

### Task 2: Create the shared classification helper

**Files:**
- Create: `scripts/lib/realm-classification.ps1`

**Interfaces:**
- Consumes: `$Manifest` (the hashtable produced by `Import-PowerShellDataFile config/manifest.psd1`, per Task 1's shape).
- Produces:
  - `Get-OrgRepos -Org <string> [-Limit <int> = 200]` → `string[]` of non-archived repo names in the org.
  - `Get-RealmGroups -Manifest <hashtable> -Realm <string>` → `string[]` of group names.
  - `Get-RealmGated -Manifest <hashtable> -Realm <string>` → `bool`.

- [ ] **Step 1: Write a failing ephemeral verification script**

Create a scratch file (not part of the repo) at `/tmp/verify-classification.ps1`:

```powershell
$ErrorActionPreference = 'Stop'

$Manifest = @{
	DefaultGroups   = @('universal', 'sdk', 'dotnet', 'nuget', 'tests', 'ci', 'workflows')
	ScatterExcludes = @('.github')
	Exceptions      = @{
		Yggdrasil = @{ Groups = @('universal', 'sdk', 'dotnet', 'tests', 'ci', 'workflows') }
		Bifrost   = @{ Groups = @('universal', 'ci'); Gated = $false }
		'.github' = @{ Gated = $false }
	}
}

. ./scripts/lib/realm-classification.ps1

# Default realm — not in Exceptions at all.
$g = Get-RealmGroups -Manifest $Manifest -Realm 'Asgard'
if (($g -join ',') -ne ($Manifest.DefaultGroups -join ',')) { throw "FAIL: Asgard groups = $($g -join ',')" }
$gated = Get-RealmGated -Manifest $Manifest -Realm 'Asgard'
if ($gated -ne $true) { throw "FAIL: Asgard gated = $gated" }

# Exception overriding Groups only — Gated must still default to $true.
$g = Get-RealmGroups -Manifest $Manifest -Realm 'Yggdrasil'
if (($g -join ',') -ne 'universal,sdk,dotnet,tests,ci,workflows') { throw "FAIL: Yggdrasil groups = $($g -join ',')" }
$gated = Get-RealmGated -Manifest $Manifest -Realm 'Yggdrasil'
if ($gated -ne $true) { throw "FAIL: Yggdrasil gated = $gated" }

# Exception overriding both fields.
$gated = Get-RealmGated -Manifest $Manifest -Realm 'Bifrost'
if ($gated -ne $false) { throw "FAIL: Bifrost gated = $gated" }

# Exception overriding Gated=$false only — must not be mistaken for "no override" via truthy check.
$gated = Get-RealmGated -Manifest $Manifest -Realm '.github'
if ($gated -ne $false) { throw "FAIL: .github gated = $gated (truthy-check bug if this is `$true)" }

Write-Host 'ALL ASSERTIONS PASSED'
```

- [ ] **Step 2: Run it to confirm it fails**

`cd` to the `.github` repo root first (the dot-source path is relative to the working directory), then run: `pwsh /tmp/verify-classification.ps1`
Expected: FAIL — dot-source error, `realm-classification.ps1` does not exist yet.

- [ ] **Step 3: Implement the helper**

Create `scripts/lib/realm-classification.ps1`:

```powershell
#!/usr/bin/env pwsh
#
# realm-classification.ps1
#
# Shared discovery and classification helpers for scatter-the-runes.ps1 and
# carve-the-laws.ps1. Dot-sourced by both — classification logic lives once
# so the two scripts cannot drift on what "default realm" means.
#
# Get-OrgRepos requires `gh` authenticated with at least `read:org` scope
# (in addition to whatever repo/PR scopes the calling script already needs).

function Get-OrgRepos {
	param(
		[Parameter(Mandatory)]
		[string]$Org,
		[int]$Limit = 200
	)

	$Json = gh repo list $Org --json name,isArchived --limit $Limit
	if ($LASTEXITCODE -ne 0) { throw "gh repo list failed (exit $LASTEXITCODE)" }

	$Repos = $Json | ConvertFrom-Json
	if ($Repos.Count -ge $Limit) {
		throw "gh repo list returned $($Repos.Count) repos at limit $Limit — likely truncated, raise -Limit."
	}

	@($Repos | Where-Object { -not $_.isArchived } | ForEach-Object Name)
}

function Get-RealmGroups {
	param(
		[Parameter(Mandatory)]
		$Manifest,
		[Parameter(Mandatory)]
		[string]$Realm
	)

	$Exception = $Manifest.Exceptions[$Realm]
	if ($Exception -and $Exception.ContainsKey('Groups')) { $Exception.Groups }
	else { $Manifest.DefaultGroups }
}

function Get-RealmGated {
	param(
		[Parameter(Mandatory)]
		$Manifest,
		[Parameter(Mandatory)]
		[string]$Realm
	)

	$Exception = $Manifest.Exceptions[$Realm]
	if ($Exception -and $Exception.ContainsKey('Gated')) { $Exception.Gated }
	else { $true }
}
```

- [ ] **Step 4: Run the verification script again to confirm it passes**

Run: `pwsh /tmp/verify-classification.ps1`
Expected: `ALL ASSERTIONS PASSED`

- [ ] **Step 5: Delete the scratch file and commit the helper**

```bash
rm /tmp/verify-classification.ps1
git add scripts/lib/realm-classification.ps1
git commit -m "scripts: add shared realm-classification helper"
```

---

### Task 3: Wire `scatter-the-runes.ps1` to live discovery + classification

**Files:**
- Modify: `scripts/scatter-the-runes.ps1` (full rewrite)

**Interfaces:**
- Consumes: `Get-OrgRepos`, `Get-RealmGroups` from `scripts/lib/realm-classification.ps1` (Task 2); `$Manifest.ScatterExcludes`, `$Manifest.Exceptions` from `config/manifest.psd1` (Task 1).

- [ ] **Step 1: Replace the file contents**

Replace the entire contents of `scripts/scatter-the-runes.ps1` with:

```powershell
#!/usr/bin/env pwsh
#
# scatter-the-runes.ps1
#
# Fans canonical config files from config/ to every realm in the org as
# auto-merge PRs. Idempotent: re-running pushes a new commit onto any existing
# sync/platform-config branch, updating open PRs without creating duplicates.
#
# Realm discovery is live (gh repo list) and classification is by exception —
# see config/manifest.psd1. Onboarding a new default realm needs no edits here.
#
# Requirements:
#   GH_TOKEN — PAT with repo scope + read:org (set in CI via SCATTER_PAT secret;
#              locally via `gh auth login` or env var)
#   git user.name and user.email configured (the workflow step sets these)
#
# Usage:
#   pwsh scripts/scatter-the-runes.ps1                  # all discovered realms
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

. (Join-Path $PSScriptRoot 'lib/realm-classification.ps1')

function Get-RealmFiles {
	param([string]$RealmName)
	$Files = [System.Collections.Generic.SortedSet[string]]::new(
		[System.StringComparer]::OrdinalIgnoreCase)
	foreach ($GroupName in (Get-RealmGroups $Manifest $RealmName)) {
		foreach ($File in $Manifest.Groups[$GroupName]) {
			[void]$Files.Add($File)
		}
	}
	@($Files)
}

$DiscoveredRepos = Get-OrgRepos $Org

if ($Realms) {
	$UnknownRealms = $Realms | Where-Object { $_ -notin $DiscoveredRepos }
	foreach ($Unknown in $UnknownRealms) {
		Write-Warning "==> $Unknown not found in $Org — skipping"
	}
	$TargetRealms = $Realms | Where-Object { $_ -in $DiscoveredRepos }
} else {
	$TargetRealms = $DiscoveredRepos | Where-Object { $_ -notin $Manifest.ScatterExcludes } | Sort-Object
}

$Failures = @()

foreach ($Realm in $TargetRealms) {
	$Files          = Get-RealmFiles $Realm
	$Classification = if ($Manifest.Exceptions.ContainsKey($Realm)) { 'exception' } else { 'default' }
	Write-Host "==> $Org/$Realm ($Classification, $($Files.Count) files)"

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
			git config remote.origin.fetch '+refs/heads/*:refs/remotes/origin/*'
			git fetch origin $Branch --quiet
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

		git commit -m 'sync: platform config from Ginnungagap' --quiet
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
				--title 'sync: platform config from Ginnungagap' `
				--body 'Automated sync of canonical platform config files from [Ginnungagap](https://github.com/NorseArchitecture/.github). Managed by ``config/manifest.psd1``.'
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

- [ ] **Step 2: Dry-run against the live org and check classification**

Run: `pwsh scripts/scatter-the-runes.ps1 -DryRun`
Expected: exactly the realms in `Bifrost/.gitmodules` minus none (no `.github` line printed at all), each tagged `(default, 7 files)` except `Yggdrasil (exception, 6 files)`, `Bifrost (exception, 2 files)`, `Naglfar (exception, 3 files)`, `Glitnir (exception, 3 files)`. Compare this list against today's `manifest.psd1` `Realms` table (Task 1's `git show HEAD~1:config/manifest.psd1` for reference) to confirm no realm's file set changed.

- [ ] **Step 3: Commit**

```bash
git add scripts/scatter-the-runes.ps1
git commit -m "scripts: scatter-the-runes discovers realms live, classifies by exception"
```

---

### Task 4: Wire `carve-the-laws.ps1` to live discovery + classification

**Files:**
- Modify: `scripts/carve-the-laws.ps1` (full rewrite)

**Interfaces:**
- Consumes: `Get-OrgRepos`, `Get-RealmGated` from `scripts/lib/realm-classification.ps1` (Task 2); `$Manifest.Exceptions` from `config/manifest.psd1` (Task 1).

- [ ] **Step 1: Replace the file contents**

Replace the entire contents of `scripts/carve-the-laws.ps1` with:

```powershell
#!/usr/bin/env pwsh
#
# carve-the-laws.ps1
#
# Applies the "Law of the Æsir" branch ruleset to every repository in the
# Norse Architecture organization. Idempotent: if a ruleset with the same
# name already exists on a repo, it is updated in place; otherwise created.
#
# Repo discovery is live (gh repo list) and gate classification is by
# exception — see config/manifest.psd1. Onboarding a new default (gated)
# realm needs no edits here.
#
# Requirements:
#   - gh CLI authenticated with an account that has admin on the repos,
#     plus read:org (gh auth status to verify)
#
# Usage:
#   ./carve-the-laws.ps1           # apply to all discovered repos
#   ./carve-the-laws.ps1 Asgard    # apply to a single repo
#
param(
	[Parameter(ValueFromRemainingArguments)]
	[string[]]$Repos
)

$ErrorActionPreference = 'Stop'

$Org         = 'NorseArchitecture'
$RulesetName = 'Law of the Æsir'
$ConfigDir   = Join-Path $PSScriptRoot '../config'
$Manifest    = Import-PowerShellDataFile (Join-Path $ConfigDir 'manifest.psd1')

. (Join-Path $PSScriptRoot 'lib/realm-classification.ps1')

$DiscoveredRepos = Get-OrgRepos $Org

if ($Repos) {
	$UnknownRepos = $Repos | Where-Object { $_ -notin $DiscoveredRepos }
	foreach ($Unknown in $UnknownRepos) {
		Write-Warning "==> $Unknown not found in $Org — skipping"
	}
	$Repos = $Repos | Where-Object { $_ -in $DiscoveredRepos }
} else {
	$Repos = $DiscoveredRepos | Sort-Object
}

# ---------------------------------------------------------------------------
# Builds the ruleset JSON for a given repo.
#
# Notes on the choices encoded here:
#   - required_approving_review_count: 1 — one approval required. Platform
#     PRs (sync/platform-config, update/cpm/*) are auto-approved by the
#     auto-approve.yml workflow via GITHUB_TOKEN; human PRs require a real
#     review. Ungated repos use this in place of a CI gate.
#   - bypass_actors actor_id 5 = Repository admin role. bypass_mode
#     "always" lets an admin push directly in an emergency; change to
#     "pull_request" to allow bypass only through a PR.
#   - required_status_checks context "gate / build" (integration_id 15368)
#     was confirmed empirically 2026-06-25: GitHub Actions (app 15368) reports
#     the check as "{caller job} / {called job}" — the workflow name and event
#     suffix are UI decorations only. Locking to integration_id 15368 prevents
#     a non-Actions source from satisfying the gate with a spoofed context name.
#   - deletion + non_fast_forward: nobody deletes or force-pushes the
#     default branch. Including you. Especially at 2 AM.
# ---------------------------------------------------------------------------
function New-Ruleset {
	param([bool]$Gated)

	$Rules = @(
		@{ type = 'deletion' }
		@{ type = 'non_fast_forward' }
		@{
			type       = 'pull_request'
			parameters = @{
				required_approving_review_count = 1
				dismiss_stale_reviews_on_push   = $true
				require_code_owner_review        = $false
				require_last_push_approval       = $false
				required_review_thread_resolution = $true
			}
		}
	)

	if ($Gated) {
		$Rules += @{
			type       = 'required_status_checks'
			parameters = @{
				strict_required_status_checks_policy = $true
				required_status_checks               = @(
					@{ context = 'gate / build'; integration_id = 15368 }
				)
			}
		}
	}

	@{
		name        = $RulesetName
		target      = 'branch'
		enforcement = 'active'
		conditions  = @{
			ref_name = @{
				include = @('~DEFAULT_BRANCH')
				exclude = @()
			}
		}
		bypass_actors = @(
			@{
				actor_id   = 5
				actor_type = 'RepositoryRole'
				bypass_mode = 'always'
			}
		)
		rules = $Rules
	} | ConvertTo-Json -Depth 10
}

# ---------------------------------------------------------------------------
# Apply: repo settings, then ruleset.
# ---------------------------------------------------------------------------
$Failures = @()

foreach ($Repo in $Repos) {
	Write-Host "==> $Org/$Repo"

	$Gated   = Get-RealmGated $Manifest $Repo
	$Ruleset = New-Ruleset -Gated $Gated
	$Gate    = if ($Gated) { 'gated' } else { 'ungated' }

	# Repo settings — idempotent PATCH; safe to run repeatedly.
	Write-Host "    Applying repo settings ($Gate)..."
	gh api --method PATCH "repos/$Org/$Repo" `
		-F delete_branch_on_merge=true `
		-F allow_auto_merge=true | Out-Null
	if ($LASTEXITCODE -eq 0) {
		Write-Host '    Repo settings applied.'
	} else {
		Write-Error -ErrorAction Continue '    FAILED to apply repo settings.'
		$Failures += $Repo
		continue
	}

	# Workflow permissions — allow GITHUB_TOKEN to approve PRs (required by auto-approve.yml).
	gh api --method PUT "repos/$Org/$Repo/actions/permissions/workflow" `
		-F can_approve_pull_request_reviews=true | Out-Null
	if ($LASTEXITCODE -eq 0) {
		Write-Host '    Workflow permissions applied.'
	} else {
		Write-Error -ErrorAction Continue '    FAILED to apply workflow permissions.'
		$Failures += $Repo
		continue
	}

	$ExistingId = gh api "repos/$Org/$Repo/rulesets" `
		--jq ".[] | select(.name == `"$RulesetName`") | .id" 2>$null
	if ($LASTEXITCODE -ne 0) {
		$ExistingId = $null
	}

	if ($ExistingId) {
		Write-Host "    Law already carved (ruleset $ExistingId) — re-inscribing..."
		$Ruleset | gh api --method PUT "repos/$Org/$Repo/rulesets/$ExistingId" --input - | Out-Null
		if ($LASTEXITCODE -eq 0) {
			Write-Host '    Updated.'
		} else {
			Write-Error -ErrorAction Continue '    FAILED to update.'
			$Failures += $Repo
		}
	} else {
		Write-Host '    Carving the law anew...'
		$Ruleset | gh api --method POST "repos/$Org/$Repo/rulesets" --input - | Out-Null
		if ($LASTEXITCODE -eq 0) {
			Write-Host '    Created.'
		} else {
			Write-Error -ErrorAction Continue '    FAILED to create.'
			$Failures += $Repo
		}
	}
}

Write-Host
if ($Failures.Count -gt 0) {
	Write-Error -ErrorAction Continue "The Aesir were defied in: $($Failures -join ' ')"
	exit 1
}

Write-Host 'The laws are carved. Verify with:'
Write-Host "  gh ruleset list -R $Org/Asgard"
```

- [ ] **Step 2: Run it against the live org and check classification**

Run: `pwsh scripts/carve-the-laws.ps1`
Expected: every repo in the org is processed (including `.github`), each printed as `(gated)` or `(ungated)`. Cross-check the printed set: `.github`, `Bifrost`, `Naglfar`, `Glitnir` → `ungated`; every other repo (`Yggdrasil` included) → `gated`. This matches today's `$GatedRepos`/`$UngatedRepos` split exactly — the script is idempotent, so this is safe to run even though it's a real API call.

- [ ] **Step 3: Commit**

```bash
git add scripts/carve-the-laws.ps1
git commit -m "scripts: carve-the-laws discovers repos live, classifies gate by exception"
```

---

### Task 5: Final cross-check against the known-good baseline

**Files:** none (verification only)

**Interfaces:** none — this task only runs Tasks 3 and 4's scripts together and compares output.

- [ ] **Step 1: Run scatter in dry-run mode and capture output**

Run: `pwsh scripts/scatter-the-runes.ps1 -DryRun | tee /tmp/scatter-after.txt`

- [ ] **Step 2: Confirm the realm set and classification matches the pre-change baseline**

Compare `/tmp/scatter-after.txt` against the realm list in `Bifrost/.gitmodules` (13 submodules) plus the removed `Realms` table from `config/manifest.psd1`'s prior version (`git show HEAD~4:config/manifest.psd1`, adjusting the offset to whichever commit preceded Task 1). Expected: identical realm names, identical file-group membership per realm, `.github` never appears.

- [ ] **Step 3: Run carve and confirm the gated/ungated split is unchanged**

Run: `pwsh scripts/carve-the-laws.ps1`
Expected: same gated set (9 default realms + Yggdrasil) and same ungated set (`.github`, Bifrost, Naglfar, Glitnir) as documented in spec §9 and confirmed in Task 4 Step 2.

- [ ] **Step 4: No commit needed — this task is verification only**

If both outputs match the baseline, the refactor is complete and behavior-preserving. If either script's classification of any repo differs from the pre-change baseline, stop and fix the `Exceptions` map in `config/manifest.psd1` (Task 1) before proceeding — do not adjust the scripts to "explain away" a mismatch.
