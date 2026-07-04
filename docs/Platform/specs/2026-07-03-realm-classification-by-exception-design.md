# Realm Classification by Exception — Design

**Date:** 2026-07-03
**Scope:** `.github` repo — `config/manifest.psd1`, `scripts/scatter-the-runes.ps1`, `scripts/carve-the-laws.ps1`
**Status:** Spec — pending implementation plan

---

## 1. Problem

Onboarding Mímir and Mímisbrunnr required editing the same fact — "this realm ships to NuGet, is gated, gets the full config file set" — in two separate hardcoded places: the `Realms` table in `manifest.psd1` (repeating the identical 7-group list verbatim for every default realm) and the `$GatedRepos` hashset in `carve-the-laws.ps1`. Neither script shares a source of truth, and both enumerate realm names explicitly rather than discovering them. Every new default realm costs two edits in two files for information that's true by default.

---

## 2. Chosen Approach

**Live org discovery + manage-by-exception.**

Both scripts call `gh repo list NorseArchitecture` at runtime instead of iterating a hardcoded name list. A realm is classified as **default** (ships to NuGet, full file-group set, gated CI) unless it appears in a small `Exceptions` map in `manifest.psd1`, in which case only the fields that differ from default are declared. Onboarding a new default realm (create the GitHub repo, submodule it into Bifrost) requires **zero edits** to `manifest.psd1` or either script — it's picked up automatically on the next run.

This was chosen over maintaining a single explicit `DefaultRealms` array (one-edit-per-realm, still enumerated) because the stated goal is zero-touch onboarding for the common case: only genuine exceptions — Yggdrasil, Bifrost, Naglfar, Glitnir, and `.github` itself — are declared anywhere.

---

## 3. `manifest.psd1` Schema

The `Groups` section (file-group definitions) is unchanged. The `Realms` table is replaced:

```powershell
DefaultGroups = @('universal', 'sdk', 'dotnet', 'nuget', 'tests', 'ci', 'workflows')

# Repos scatter must never sync into — source of the config, not a consumer.
ScatterExcludes = @('.github')

# Anything NOT listed here is a default realm: ships to NuGet, full group
# set, gated CI. Exception entries declare only the fields that differ.
Exceptions = @{
	Yggdrasil = @{
		# Composition root — CPM on, no NorseRef/nuget plumbing needed.
		Groups = @('universal', 'sdk', 'dotnet', 'tests', 'ci', 'workflows')
	}
	Bifrost = @{
		Groups = @('universal', 'ci')
		Gated  = $false
	}
	Naglfar = @{
		Groups = @('git', 'ci', 'workflows')
		Gated  = $false
	}
	Glitnir = @{
		Groups = @('git', 'ci', 'workflows')
		Gated  = $false
	}
	'.github' = @{
		Gated = $false   # no Groups — scatter never reaches it, excluded above
	}
}
```

A field absent from an exception entry falls back to the default (`DefaultGroups` for `Groups`, `$true` for `Gated`).

---

## 4. Shared Classification Helper

Both scripts need the same live discovery and the same "check `Exceptions`, else default" lookup. This is genuine shared logic, not just data, so it lives once:

`scripts/lib/realm-classification.ps1` (dot-sourced by both scripts):

```powershell
function Get-OrgRepos {
	param([string]$Org)
	$Limit = 200
	$Names = gh repo list $Org --json name,isArchived --limit $Limit |
		ConvertFrom-Json | Where-Object { -not $_.isArchived } | ForEach-Object Name
	if ($Names.Count -ge $Limit) { throw "Repo list hit limit $Limit — likely truncated, raise it." }
	@($Names)
}

function Get-RealmGroups {
	param($Manifest, [string]$Realm)
	if ($Manifest.Exceptions[$Realm]?.Groups) { $Manifest.Exceptions[$Realm].Groups }
	else { $Manifest.DefaultGroups }
}

function Get-RealmGated {
	param($Manifest, [string]$Realm)
	if ($null -ne $Manifest.Exceptions[$Realm]?.Gated) { $Manifest.Exceptions[$Realm].Gated }
	else { $true }
}
```

The truncation guard is a deliberate fail-loud choice: a silently truncated repo list would silently drop realms from scatter/carve coverage, which is worse than an explicit script failure.

---

## 5. `scatter-the-runes.ps1` Changes

- `$TargetRealms` default becomes `Get-OrgRepos $Org | Where-Object { $_ -notin $Manifest.ScatterExcludes } | Sort-Object` (replacing `$Manifest.Realms.Keys | Sort-Object`).
- `Get-RealmFiles` computes groups via `Get-RealmGroups $Manifest $RealmName` instead of indexing `$Manifest.Realms[$RealmName]`.
- The `-Realms` positional CLI override is unchanged in spirit but its guard changes: today it skips (with a warning) any name absent from the hardcoded `Realms` table. That check no longer applies — since classification is manage-by-exception, any name is a valid default realm unless declared otherwise. The guard is replaced with a check against `Get-OrgRepos` (skip with a warning if the named repo doesn't exist in the org at all — catches typos without requiring manifest membership).
- `-DryRun` output prints each repo's resolved classification (default, or which exception fields fired) so a bad `Exceptions` entry is visible before any PR goes out.

## 6. `carve-the-laws.ps1` Changes

- `$GatedRepos` / `$UngatedRepos` hashsets are removed entirely.
- `$AllRepos` default becomes `Get-OrgRepos $Org | Sort-Object` (`.github` is included here, unlike in scatter).
- Per-repo gate classification: `$Gated = Get-RealmGated $Manifest $Repo`, replacing the two `.Contains()` checks.
- The `-Repos` positional CLI override's guard changes the same way as scatter's: today it skips names absent from `$GatedRepos`/`$UngatedRepos`; that's replaced with a check against `Get-OrgRepos` (skip with a warning if the named repo doesn't exist in the org).

## 7. `.github` Asymmetry (Deliberate, Preserved)

`.github` (Ginnungagap) is the source of the canonical config — it never receives a scatter PR into itself, so it is hard-excluded from scatter's discovered repo list via `ScatterExcludes`. It still needs the branch-protection ruleset applied to itself, so it remains in carve's discovered repo list, classified via `Exceptions['.github'].Gated = $false` (no build gate — `.github` has no `gate / build` CI check).

---

## 8. Requirements Update

`gh repo list` requires `read:org` scope (or equivalent org read access) in addition to what `GH_TOKEN` / `SCATTER_PAT` already grant for repo contents and PRs. Both scripts' header comment blocks get a one-line addition noting this, so the requirement doesn't get rediscovered the hard way in CI.

---

## 9. Validation Before Merge

Before this lands in CI:

1. Run `scatter-the-runes.ps1 -DryRun` against the live org and confirm the printed classification matches today's known-good state: 9 default realms with the full `nuget` group set, Yggdrasil/Bifrost/Naglfar/Glitnir with their existing overrides, `.github` absent from the list entirely.
2. Run `carve-the-laws.ps1` (idempotent — its PATCH/PUT/ruleset calls are safe to re-run) and confirm the gated/ungated split is unchanged: the same 9 default realms plus Yggdrasil gated, Bifrost/Naglfar/Glitnir/`.github` ungated.
3. Only after both dry-run outputs match today's expected state does the refactor become load-bearing.
