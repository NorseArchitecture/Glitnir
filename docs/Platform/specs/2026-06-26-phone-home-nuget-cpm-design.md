# Phone Home — NuGet CPM Auto-Update

**Date:** 2026-06-26
**Status:** Approved in session 2026-06-26.
**Owner:** Buvy
**Companion specs:** `2026-06-19-ci-release-pipeline-design.md` (the release ceremony this extends — phone-home fires as the final job after `pack-and-publish` succeeds); `2026-06-26-use-project-references-design.md` (the `UseProjectReferences` toggle that governs when NuGet packages are consumed vs. project references — CPM governs the NuGet path).

---

## 1. Context

When a NuGet realm cuts a stable release (`vX.Y.Z`), the versioned packages land on GitHub Packages. Yggdrasil (`Norse.Hosting.*`) is the composition root that consumes them — its `Directory.Packages.props` (Central Package Management) governs which version of every Norse package flows out to the cloud. Today that file does not exist; this design creates it and the automation that keeps it current.

The pattern is the deliberate inverse of `scatter-the-runes`: scatter fans config **outward** from `.github` to every realm; phone-home fans version signals **inward** from every releasing realm back to Yggdrasil. Both patterns are orchestrated from `.github`; the logic never lives in the callers.

## 2. Rulings

### 2.1 Scope

Seven NuGet-shipping realms participate: Svartalfheim, Asgard, Midgard, Urdarbrunnr, Ratatoskr, Himinbjorg, Heimdall. Bifrost, Glitnir, and Nagalfar do not publish NuGet packages and are not callers.

Yggdrasil is the sole phone-home target for this design. Product realm repos (`{Company}.{Context}.*`) are sovereign; if they need CPM automation, they build their own bridge.

### 2.2 Stable releases only

Phone-home fires only for stable version tags. Any tag containing `-` (e.g., `v1.2.4-beta001`) is a pre-release and is skipped. The guard exists at two levels:

1. **Job condition** in `phone-home-nuget.yml`: `if: ${{ !contains(github.ref_name, '-') }}` — no checkout, no compute consumed for pre-release tags.
2. **Script guard** in `phone-home-nuget.ps1`: belt-and-suspenders `$Version.Contains('-') → exit 0`.

Pre-release packages are available on the feed for Bifrost local-dev consumption but never alter Yggdrasil's pinned versions.

### 2.3 `Directory.Packages.props` structure

Yggdrasil's CPM file is hand-authored once as a deliberate human act. The automation updates values; it never inserts new elements. If a property is missing, the script fails loudly.

One `<{Realm}Version>` property per NuGet realm, all in a single `<PropertyGroup>`. `PackageVersion` items reference these properties via MSBuild expansion — version knowledge lives in exactly one place per realm.

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

		<!-- Additional realms' packages follow as they exist -->
	</ItemGroup>
</Project>
```

`0.0.0` as the initial placeholder is intentional — it is an invalid NuGet version that causes an immediate restore failure rather than silently resolving to any published version. A build against `0.0.0` fails loudly on first attempt; there is no ambiguous success.

MSBuild evaluates `<PropertyGroup>` elements before `<ItemGroup>` elements within the same file, so `$(AsgardVersion)` is defined by the time `PackageVersion` items are processed. No separate import is required.

### 2.4 New artifacts in `.github`

| File | Purpose |
|---|---|
| `.github/workflows/phone-home-nuget.yml` | Reusable `workflow_call`; orchestrates the two checkouts and runs the script |
| `scripts/phone-home-nuget.ps1` | Edits the props file, commits, pushes branch, opens/updates PR, arms auto-merge |

### 2.5 Reusable workflow — `phone-home-nuget.yml`

```yaml
name: Phone Home — NuGet CPM Update

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

**Context in `workflow_call`:** `github.event.repository.name` resolves to the calling repo (e.g., `Svartalfheim`); `github.ref_name` resolves to the tag that triggered the caller (e.g., `v0.0.2`). Both are confirmed behavior — `update-bifrost.yml` uses `github.event.repository.name` and `github.sha` on the same premise.

The caller's repo is never checked out; only `.github` (to get the script) and Yggdrasil (to edit and push) are fetched.

### 2.6 Script — `phone-home-nuget.ps1`

Parameters: `-Realm` (repo name), `-Tag` (full tag string including `v`), `-YggdrasilPath` (relative path to the checked-out Yggdrasil working tree), `-DryRun` (switch; prints plan and exits, no writes).

Behavior:

1. Strip `v` prefix from tag → bare `$Version`.
2. Belt-and-suspenders pre-release guard: `$Version.Contains('-')` → `exit 0`.
3. Hard-fail if `Directory.Packages.props` is not found at `$YggdrasilPath`.
4. Hard-fail if `<{Realm}Version>` property element is not present in the file — no silent insertion.
5. Regex-replace the property value: `<SvartalfheimVersion>[^<]*</SvartalfheimVersion>` → `<SvartalfheimVersion>0.0.2</SvartalfheimVersion>`. MSBuild property name uses `$($Realm)` subexpression syntax to avoid PowerShell variable-boundary ambiguity.
6. `Set-Content -NoNewline` to preserve the original file's line-ending posture.
7. Exit clean (no commit) if the updated content equals the original — idempotent on re-runs.
8. Checkout-or-create branch `update/cpm/{realm-lowercase}`.
9. `git push origin $Branch --force-with-lease` — a faster re-release of the same realm overwrites the existing branch and updates the open PR rather than stacking a second one.
10. `gh pr list` to detect an existing open PR on that branch.
11. If no open PR: `gh pr create` + `gh pr merge --auto --merge` to arm auto-merge.
12. If PR already exists: push already updated it; log the PR number and exit.

Commit message: `update: {Realm} → {version}` (e.g., `update: Svartalfheim → 0.0.2`).

PR title: `update: {Realm} {version}`.

PR body links back to the GitHub Release that triggered the phone-home so the audit trail is a single click.

### 2.7 Realm caller changes

Each of the seven NuGet realms appends one job to its `release.yml`:

```yaml
  phone-home:
    needs: [release]
    uses: NorseArchitecture/.github/.github/workflows/phone-home-nuget.yml@master
    secrets:
      token: ${{ secrets.SCATTER_PAT }}
```

`needs: [release]` waits for the entire `release-nuget.yml` reusable workflow (build-test, codeql, pack-and-publish — all three jobs) before firing. Phone-home never runs if the release ceremony fails.

**Token:** `SCATTER_PAT` is the org-level PAT with `repo` scope. It already covers cross-repo writes for scatter; the same credential covers Yggdrasil. If any realm does not yet have access to `SCATTER_PAT` as an org secret, promote it to org-level once rather than provisioning per-realm.

### 2.8 Failure behavior

| Scenario | Outcome |
|---|---|
| Pre-release tag | Job skipped; Yggdrasil untouched |
| Release ceremony fails | `phone-home` job never runs (`needs: [release]`) |
| `Directory.Packages.props` missing | Script throws; job fails; Yggdrasil untouched; PR not opened |
| `<{Realm}Version>` property missing | Script throws; job fails; PR not opened |
| Yggdrasil CI fails on PR | Auto-merge does not fire; PR stays open; master unaffected; failure is visible in Yggdrasil's open PRs |
| Yggdrasil CI passes | Auto-merge fires; master updated; branch deleted |

### 2.9 Smoke test plan

Release `v0.0.2` from Svartalfheim against a Yggdrasil that contains only `Directory.Packages.props` (no source projects, no tests). Expected outcome: `SvartalfheimVersion` updates from `0.0.0` to `0.0.2`, PR opens, CI on the empty repo passes trivially, auto-merge fires. Proves the full chain before any realm actually takes a compile-time dependency on Norse packages.

## 3. Alternatives Rejected

- **Caller-declared package names.** Each realm's `release.yml` would pass a `packages:` input listing what it ships. Rejected: the `<{Realm}Version>` MSBuild property pattern makes package enumeration irrelevant — the workflow needs only the realm name, which is available from context. Caller declarations would be data with no consumer.

- **Extend `manifest.psd1` with a packages section.** A central mapping of realm → package list. Rejected for the same reason: the single-property-per-realm design eliminates the need for any package enumeration at the automation layer.

- **`repository_dispatch`: realms fire events, Yggdrasil owns its handler.** Logic would be split across N realm release workflows plus Yggdrasil's own handler. Rejected for the same reason per-realm duplicated workflow YAML was rejected in the CI design (§3, `2026-06-19-ci-release-pipeline-design.md`) — a convention change requires N pull requests and drift is inevitable.

- **Dependabot / Renovate Bot.** Zero custom code, but loses the phone-home concept entirely; does not follow `.github` reusable-workflow pattern; dependency on a third-party scheduler for a private GitHub Packages feed.

- **Direct commit to Yggdrasil master.** Faster, but if the new version breaks Yggdrasil's build, master goes red with no gate. The PR + auto-merge model keeps master clean and makes failures loud without being silent.

## 4. Consequences

1. Create `Directory.Packages.props` in Yggdrasil with initial `0.0.0` placeholders for all seven realms. **One-time human step.**
2. Add `phone-home-nuget.yml` to `.github/.github/workflows/`. **New artifact.**
3. Add `phone-home-nuget.ps1` to `.github/scripts/`. **New artifact.**
4. Append `phone-home` job to `release.yml` in each of the seven NuGet realms. **Seven thin caller stubs.**
5. Confirm `SCATTER_PAT` is available as an org-level secret to all NuGet realm repos. **Secret audit / promotion if needed.**
6. Smoke-test: release `v0.0.2` from Svartalfheim (§2.9).
