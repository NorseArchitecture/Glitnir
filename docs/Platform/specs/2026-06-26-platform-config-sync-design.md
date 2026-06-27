# Platform Config Sync — Design

**Date:** 2026-06-26
**Scope:** `.github` repo + all realms
**Status:** Spec — pending implementation plan

---

## 1. Problem

The Norse Architecture spans 10+ realm repositories. Several files are identical across all (or nearly all) of them: `.editorconfig`, `.gitattributes`, `.gitignore`, `global.json`, `LICENSE`, root `Directory.Build.props`, and the standard `src/` and `tests/` MSBuild props files. There is no mechanism to propagate a change to one of those files without manually opening a PR in every affected repo. This creates silent drift and requires remembering which of 10+ repos needs updating.

---

## 2. Chosen Approach

**`scatter-the-runes.ps1` + GitHub Actions trigger.**

Canonical config files live in the `.github` repo at `config/`, mirroring their target path in each realm. A manifest declares which realms receive which file groups. A GitHub Actions workflow fires automatically on any push to `master` that touches `config/**`, runs the script, and opens a PR with auto-merge enabled in every affected realm. In the happy path — CI green in every realm — zero human action is required after the initial push. A failing realm's PR stays open; the human triages by either fixing the realm or correcting the canonical config and pushing again (which updates the existing PR branch).

This approach was chosen over:

- **`repo-file-sync-action`** (third-party) — declarative and low-code, but auto-merge integration is awkward and adds an external dependency for a security-adjacent operation.
- **`git subtree`** — the right tool for library code in a subdirectory; wrong for root-level config files (`.editorconfig`, `.gitignore`, etc.) because subtree content lives in a subdirectory of the target, not at its root.

---

## 3. Repository Structure

```
.github/
  config/
    manifest.psd1
    .editorconfig
    .gitattributes
    .gitignore
    Directory.Build.props          ← realm root-level
    global.json
    LICENSE
    src/
      Directory.Build.props        ← NuGet-shipping realms
      # Directory.Build.targets    ← reserved; see §6
    tests/
      Directory.Build.props        ← NuGet-shipping realms
      # Directory.Build.targets    ← reserved; see §6
  scripts/
    carve-the-laws.ps1             (existing)
    scatter-the-runes.ps1          (new)
  .github/
    workflows/
      scatter-the-runes.yml        (new)
      ...existing...
```

---

## 4. File Groups and Realm Manifest

### Groups

| Group | Files |
|---|---|
| `git` | `.gitattributes`, `.gitignore` |
| `universal` | everything in `git` + `.editorconfig`, `global.json`, `LICENSE` |
| `dotnet` | `Directory.Build.props` (root) |
| `nuget` | `src/Directory.Build.props`, `tests/Directory.Build.props` |

Groups are additive: `universal` implies `git`. The manifest stores the minimal set (e.g., a realm assigned `universal` does not also list `git`).

### Realm Assignments

| Realm | Groups |
|---|---|
| Svartalfheim | `universal`, `dotnet`, `nuget` |
| Asgard | `universal`, `dotnet`, `nuget` |
| Midgard | `universal`, `dotnet`, `nuget` |
| Urdarbrunnr | `universal`, `dotnet`, `nuget` |
| Ratatoskr | `universal`, `dotnet`, `nuget` |
| Heimdall | `universal`, `dotnet`, `nuget` |
| Himinbjorg | `universal`, `dotnet`, `nuget` |
| Yggdrasil | `universal`, `dotnet` |
| Nagalfar | `git` |
| Glitnir | `git` |
| Bifrost | `universal` |

**Yggdrasil** receives `universal` + `dotnet` but not `nuget` — it is the runtime hosting composition layer, does not ship to NuGet, and owns its own `src/` and `tests/` MSBuild props for two reasons: (1) no `IsAotCompatible=true`, since it is the composition root not a portable library; (2) it uses Central Package Management (CPM) where `<PackageReference>` items carry no `Version` attribute, while NuGet-shipping realms do not use CPM and require explicit versions. Both differences propagate into the future `Directory.Build.targets` files (see §11), making Yggdrasil's copies structurally incompatible with the canonical `nuget` group files.

**Nagalfar** receives `git` only — it is the design system home, not a .NET build target. `global.json` is an SDK version pin with no meaning outside .NET; the platform `.editorconfig` is saturated with `csharp_style_*` and `dotnet_*` rules that are wrong for CSS/tokens/JS work. Nagalfar crafts its own `.editorconfig` suited to whatever toolchain the design system uses. `LICENSE` is omitted — design tokens and component primitives are not distributable library artifacts.

**Glitnir** receives `git` only — it is a docs and proof-of-concept repo. Nothing in it produces an installable artifact, so `LICENSE`, `global.json`, and the .NET `.editorconfig` have no place there.

**Bifrost** receives `universal` — it is a .NET Aspire host and benefits from the shared SDK pin, code style, and git hygiene files. It is excluded from `dotnet` and `nuget`: its root `Directory.Build.props` is intentionally minimal for an Aspire host and is not derived from the platform template, and it has no `src/` or `tests/` package layout.

The manifest is a PowerShell data file (`manifest.psd1`) so the script can import it directly without parsing.

---

## 5. `scatter-the-runes.ps1` Mechanics

For each realm in the manifest:

1. **Compute the effective file list** from the realm's assigned groups.
2. **Clone** the realm with `gh repo clone` (respects `GH_TOKEN` automatically; no credential wiring).
3. **Copy** each canonical file from `config/` into the clone at its target path.
4. **Diff** — `git diff --exit-code`. If nothing changed, skip this realm. No PR, no noise.
5. **Branch** — push to the fixed branch name `sync/platform-config`. If that branch already has an open PR, the new commit updates it in place; GitHub tracks the branch. If the branch exists but has no open PR (e.g., a previous run was manually closed), delete and recreate.
6. **PR** — `gh pr create` if no open PR exists. Title: `sync: platform config from .github`. Body notes which files changed and links to the triggering commit in `.github`.
7. **Arm auto-merge** — `gh pr merge sync/platform-config --auto --merge` immediately after create. When CI passes, GitHub merges and deletes the branch on its own.
8. **Collect failures** — if any step fails for a realm, record it and continue to the next realm. At the end, report all failures and exit non-zero. This matches the `carve-the-laws.ps1` pattern: every realm is attempted, nothing is silently skipped.

---

## 6. GitHub Actions Workflow

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
      - name: Scatter
        env:
          GH_TOKEN: ${{ secrets.SCATTER_PAT }}
        run: pwsh scripts/scatter-the-runes.ps1
```

The `paths` filter ensures the workflow is silent on README edits, workflow changes, and script updates — it fires only when a canonical config file actually changes.

---

## 7. Authentication

`SCATTER_PAT` is a Personal Access Token with `repo` scope, stored as an org-level secret so it is available to the `.github` repo's Actions. Fine-grained PAT scoped to each realm's "Contents: Read & Write" and "Pull requests: Write" is the more secure alternative; for a solo maintainer the classic PAT is the pragmatic starting point.

---

## 8. Pre-requisite: Enable Auto-Merge on All Realms

GitHub's per-PR auto-merge (`gh pr merge --auto`) requires `allow_auto_merge` to be enabled on the target repository. This is a one-time repo setting, not a per-PR option.

`carve-the-laws.ps1` already PATCHes each repo's settings (`delete_branch_on_merge=true`). Add `-F allow_auto_merge=true` to the same call and re-run `carve-the-laws.ps1` once. Every realm is wired before the first scatter run.

---

## 9. Failure Triage

When a realm's CI fails and the auto-merge does not fire, two triage paths apply:

**Realm-side fix** — the canonical config is correct; the realm has a test, lint rule, or build assumption that conflicts. Fix it directly on the realm's `sync/platform-config` branch. CI re-runs; if it passes, auto-merge fires.

**Config-side fix** — the canonical config introduced a problem. Fix it in `.github/config/`, push to master. The `scatter-the-runes.yml` workflow fires again and pushes a new commit onto the existing `sync/platform-config` branch in every open realm PR. CI re-runs across all realms. Realms that were already merged are unaffected — they received the original change and will receive any follow-on correction on their next PR.

In both cases the decision is explicit: you see the failing PR, you choose which leg to pull. The system never silently discards a failure or auto-resolves a conflict.

---

## 10. Bootstrap

The first run of `scatter-the-runes.ps1` will open PRs showing any existing drift between realms and the canonical files. Known drift at spec time:

- **Asgard `.gitignore`** is missing the `coverage-report/` entry that was added to Svartalfheim when code coverage was wired up (2026-06-26). This is expected — Asgard has not had coverage wired yet. The first sync PR brings it in line. After that, the two files move together.

No other drift is known; realms checked were identical on `.editorconfig`, `.gitattributes`, root `Directory.Build.props`, and `global.json`.

---

## 11. Reserved Extension Point: `UseProjectReferences`

`src/Directory.Build.targets` and `tests/Directory.Build.targets` are reserved slots in the `nuget` group. They are commented out in the manifest pending the `UseProjectReferences` feature — an MSBuild toggle that swaps all inter-realm `<ProjectReference>` items for `<PackageReference>` items and back, so the full Bifrost working tree can be built as a developer debuggable source graph or as a strict package consumer, matching CI exactly.

When that feature lands, uncommenting those two lines in `manifest.psd1` and placing the canonical targets files at `config/src/Directory.Build.targets` and `config/tests/Directory.Build.targets` is the entire wiring cost. The next push to `config/**` fans them out to all NuGet-shipping realms automatically.

The canonical targets files use `Version="*"` on the generated `<PackageReference>` items — correct for the NuGet realms, which do not use Central Package Management. Yggdrasil is intentionally excluded from the `nuget` group and will carry its own targets files that omit `Version` entirely, relying on its `Directory.Packages.props` to supply versions via CPM. This is the same boundary that already separates `src/Directory.Build.props` between the groups.

---

## 12. `workflows` Group — Scatter `release.yml` to NuGet Realms

The seven NuGet-shipping realms share an identical `.github/workflows/release.yml` (Svartalfheim's live file is the canonical form, proven 2026-06-26). Rather than manually creating six copies, scatter delivers them.

**New group: `workflows`**

| File | Target path in each realm |
|---|---|
| `config/.github/workflows/release.yml` | `.github/workflows/release.yml` |

**Realm assignments** — only NuGet-shipping realms:

| Realm | Adds `workflows` |
|---|---|
| Svartalfheim | Yes — already has the file; scatter idempotency means no-op on re-run if unchanged |
| Asgard | Yes |
| Midgard | Yes |
| Urdarbrunnr | Yes |
| Ratatoskr | Yes |
| Himinbjorg | Yes |
| Heimdall | Yes |
| Yggdrasil | No — Yggdrasil does not ship NuGet packages and has no release ceremony |
| Bifrost | No |
| Nagalfar | No |
| Glitnir | No |

**`config/` directory structure addition:**

```
.github/
  config/
    .github/
      workflows/
        release.yml      ← canonical NuGet realm release + phone-home caller
```

**Wiring:** copy `Svartalfheim/.github/workflows/release.yml` to `config/.github/workflows/release.yml`, add the `workflows` group to `manifest.psd1`, assign it to the seven NuGet realms. The `paths: config/**` trigger in `scatter-the-runes.yml` picks up the new file automatically on the next push to `master`.
