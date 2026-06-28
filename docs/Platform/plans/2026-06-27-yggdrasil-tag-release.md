# Yggdrasil Tag & Release Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (default) or `superpowers:executing-plans` (separate-session fallback only) to implement this plan task-by-task. Pair with `superpowers:test-driven-development` for any coding tasks. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire up the CI and container-release GitHub Actions workflows for Yggdrasil — `ci.yml` and `release.yml` thin callers in the Yggdrasil repo, the `release-container.yml` shared ceremony in `.github`, and branch-protection on Yggdrasil — so that a `vX.Y.Z` tag triggers the full `ci → codeql → package → deploy-hook` ceremony and lands three container images in GHCR.

**Architecture:** Two repositories are touched: `NorseArchitecture/.github` receives the reusable `release-container.yml` workflow (the ceremony); `NorseArchitecture/Yggdrasil` receives two thin callers (`ci.yml`, `release.yml`) that invoke the shared workflows by their fully-qualified org path. The package job builds three images to the local Docker daemon, scans each with Trivy (CycloneDX SBOM output, HIGH/CRITICAL exit), pushes only on clean scans, then creates a GitHub Release attaching all three SBOMs. The `deploy-hook` job is a named no-op today with routing stubs for the future cloud-environment implementation.

**Tech Stack:** GitHub Actions, `actions/checkout@v7`, `actions/setup-dotnet@v5`, `docker/login-action@v4`, `aquasecurity/trivy-action@master`, `github/codeql-action/{init,analyze}@v4`, `gh` CLI, .NET 11 preview (`11.0.x`/`preview`), `dotnet publish /t:PublishContainer`.

## Global Constraints

- **No automatic git commits** — `git add` only; human commits.
- **No force-push to `master`**, no `--no-verify` on any hook.
- **US English** in all workflow names, step names, comments, and commit messages.
- **`release-container.yml` lives in `NorseArchitecture/.github` at `.github/workflows/release-container.yml`** — alongside `release-nuget.yml`, not in Yggdrasil.
- **No `:latest` tag** on any GHCR image — ever.
- **Version strip:** `v` prefix always removed (`v0.1.0` → `0.1.0`) for image tags and NuGet convention parity.
- **Scan before push** — images must never reach GHCR if Trivy finds HIGH or CRITICAL vulnerabilities.
- **Three images, exact names:**
  - `ghcr.io/norsearchitecture/hosting/migrations:{version}`
  - `ghcr.io/norsearchitecture/hosting/web:{version}`
  - `ghcr.io/norsearchitecture/hosting/worker:{version}`
- **Project paths** (brand-free per Bifrost CLAUDE.md §2, under `src/` at Yggdrasil repo root):
  - `src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj`
  - `src/Hosting.Web.Server/Hosting.Web.Server.csproj`
  - `src/Hosting.Worker/Hosting.Worker.csproj`
- **`GITHUB_TOKEN` only** — no `SCATTER_PAT` needed (Yggdrasil sends no phone-home PRs).
- **Deploy-hook routing** (per spec §2.7): tag containing `-` → feature environment; stable tag → dev/integration. Routing condition: `contains(github.ref_name, '-')`. Both are no-ops today; structure is locked in now.
- **Smoke test (Task 5) requires the Yggdrasil hosting skeleton** (the three `Program.cs` stubs and `.csproj` files for the deployable hosts). That skeleton is implemented in a separate context. Do not execute Task 5 until those projects exist and `dotnet publish /t:PublishContainer` succeeds locally or on CI.

---

## File Map

| Action | File | Repository |
|---|---|---|
| Modify | `docs/Platform/specs/2026-06-27-yggdrasil-tag-release-design.md` | Bifrost/Glitnir |
| Create | `.github/workflows/release-container.yml` | `.github` |
| Create | `.github/workflows/ci.yml` | Yggdrasil |
| Create | `.github/workflows/release.yml` | Yggdrasil |
| Run (no file) | `scripts/carve-the-laws.ps1 Yggdrasil` | `.github` |

---

## Task 1: Amend spec — three-image identity

The spec was written before the three-image topology was confirmed. §2.4 and §2.6 need to reflect three `hosting/` images.

**Files:**
- Modify: `../Glitnir/docs/Platform/specs/2026-06-27-yggdrasil-tag-release-design.md`

- [ ] **Step 1: Update §2.4 package job description**

Locate the bullet that reads:
```
1. Builds the container image.
```
Replace with:
```
1. Builds three container images — `hosting/migrations`, `hosting/web`, `hosting/worker` — to the local Docker daemon via `dotnet publish /t:PublishContainer`. Images never reach GHCR until Trivy passes.
```

- [ ] **Step 2: Update §2.6 container identity table**

Replace the existing single-image table:
```markdown
| Property | Value |
|---|---|
| Registry | `ghcr.io` |
| Image | `ghcr.io/norsearchitecture/yggdrasil` |
| Tag | `{bare-version}` — `v` prefix stripped, e.g. `0.1.0` |
| `:latest` | Never |
| SBOM format | CycloneDX, attached to GitHub Release |
| Vulnerability threshold | Fail on HIGH or CRITICAL |
```

With the three-image table:
```markdown
| Image | GHCR path | Tag | `:latest` |
|---|---|---|---|
| Migrations init container | `ghcr.io/norsearchitecture/hosting/migrations` | `{bare-version}` | Never |
| Web server | `ghcr.io/norsearchitecture/hosting/web` | `{bare-version}` | Never |
| Worker | `ghcr.io/norsearchitecture/hosting/worker` | `{bare-version}` | Never |

`{bare-version}` = tag with `v` prefix stripped (`v0.1.0` → `0.1.0`), consistent with MinVer convention.

| Property | Value |
|---|---|
| SBOM format | CycloneDX, one file per image, attached to GitHub Release |
| Vulnerability threshold | Fail on HIGH or CRITICAL — nothing reaches GHCR from a vulnerable image |
```

- [ ] **Step 3: Stage**

```bash
git add Glitnir/docs/Platform/specs/2026-06-27-yggdrasil-tag-release-design.md
```

Proposed commit message: `docs: amend yggdrasil tag-release spec for three-image topology`

---

## Task 2: Add `release-container.yml` to the `.github` repo

This is the full ceremony workflow. It lives at `.github/workflows/release-container.yml` in `NorseArchitecture/.github` — alongside `release-nuget.yml`. Yggdrasil's `release.yml` (Task 3) calls it by the fully-qualified org path.

**Files:**
- Create: `.github/workflows/release-container.yml` in the `NorseArchitecture/.github` repo (filesystem path: `../../../.github/.github/workflows/release-container.yml` from Bifrost root, i.e. `/home/buvy/code/NorseArchitecture/.github/.github/workflows/release-container.yml`)

- [ ] **Step 1: Create the workflow file**

Create `/home/buvy/code/NorseArchitecture/.github/.github/workflows/release-container.yml`:

```yaml
name: Container Release

# Setup steps (checkout, .NET, NuGet source) are intentionally inline.
# Composite actions cannot be called from within a reusable workflow — the runner
# checks out the CALLER's repository, so any ./ path resolves there, not here.

on:
  workflow_call:

permissions:
  contents: write
  packages: write
  security-events: write

env:
  DOTNET_VERSION: "11.0.x"
  DOTNET_QUALITY: "preview"

jobs:
  ci:
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
          dotnet-version: ${{ env.DOTNET_VERSION }}
          dotnet-quality: ${{ env.DOTNET_QUALITY }}

      - name: Initialize CodeQL
        uses: github/codeql-action/init@v4
        with:
          languages: csharp

      - name: Build
        run: |
          dotnet restore
          dotnet build -c Release

      - name: Analyze
        uses: github/codeql-action/analyze@v4

  package:
    needs: [ci, codeql]
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v7

      - name: Setup .NET
        uses: actions/setup-dotnet@v5
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}
          dotnet-quality: ${{ env.DOTNET_QUALITY }}

      - name: Log in to GHCR
        uses: docker/login-action@v4
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Extract version
        id: version
        run: echo "value=${GITHUB_REF_NAME#v}" >> "$GITHUB_OUTPUT"

      # ── migrations ──────────────────────────────────────────────────────────

      - name: Publish migrations image (local daemon)
        run: |
          dotnet publish src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj \
            --os linux --arch x64 -c Release /t:PublishContainer \
            /p:ContainerRepository=norsearchitecture/hosting/migrations \
            /p:ContainerImageTag=${{ steps.version.outputs.value }}

      - name: Scan migrations image
        uses: aquasecurity/trivy-action@master
        with:
          image-ref: norsearchitecture/hosting/migrations:${{ steps.version.outputs.value }}
          format: cyclonedx
          output: sbom-migrations.cdx.json
          exit-code: '1'
          severity: HIGH,CRITICAL

      - name: Push migrations image
        run: |
          docker tag norsearchitecture/hosting/migrations:${{ steps.version.outputs.value }} \
            ghcr.io/norsearchitecture/hosting/migrations:${{ steps.version.outputs.value }}
          docker push ghcr.io/norsearchitecture/hosting/migrations:${{ steps.version.outputs.value }}

      # ── web ─────────────────────────────────────────────────────────────────

      - name: Publish web image (local daemon)
        run: |
          dotnet publish src/Hosting.Web.Server/Hosting.Web.Server.csproj \
            --os linux --arch x64 -c Release /t:PublishContainer \
            /p:ContainerRepository=norsearchitecture/hosting/web \
            /p:ContainerImageTag=${{ steps.version.outputs.value }}

      - name: Scan web image
        uses: aquasecurity/trivy-action@master
        with:
          image-ref: norsearchitecture/hosting/web:${{ steps.version.outputs.value }}
          format: cyclonedx
          output: sbom-web.cdx.json
          exit-code: '1'
          severity: HIGH,CRITICAL

      - name: Push web image
        run: |
          docker tag norsearchitecture/hosting/web:${{ steps.version.outputs.value }} \
            ghcr.io/norsearchitecture/hosting/web:${{ steps.version.outputs.value }}
          docker push ghcr.io/norsearchitecture/hosting/web:${{ steps.version.outputs.value }}

      # ── worker ──────────────────────────────────────────────────────────────

      - name: Publish worker image (local daemon)
        run: |
          dotnet publish src/Hosting.Worker/Hosting.Worker.csproj \
            --os linux --arch x64 -c Release /t:PublishContainer \
            /p:ContainerRepository=norsearchitecture/hosting/worker \
            /p:ContainerImageTag=${{ steps.version.outputs.value }}

      - name: Scan worker image
        uses: aquasecurity/trivy-action@master
        with:
          image-ref: norsearchitecture/hosting/worker:${{ steps.version.outputs.value }}
          format: cyclonedx
          output: sbom-worker.cdx.json
          exit-code: '1'
          severity: HIGH,CRITICAL

      - name: Push worker image
        run: |
          docker tag norsearchitecture/hosting/worker:${{ steps.version.outputs.value }} \
            ghcr.io/norsearchitecture/hosting/worker:${{ steps.version.outputs.value }}
          docker push ghcr.io/norsearchitecture/hosting/worker:${{ steps.version.outputs.value }}

      # ── release ─────────────────────────────────────────────────────────────

      - name: Create GitHub Release
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          gh release create "${{ github.ref_name }}" \
            sbom-migrations.cdx.json \
            sbom-web.cdx.json \
            sbom-worker.cdx.json \
            --generate-notes

  deploy-hook:
    needs: [package]
    runs-on: ubuntu-latest
    steps:
      - name: Deploy to feature environment
        if: ${{ contains(github.ref_name, '-') }}
        run: echo "Pre-release ${{ github.ref_name }} — deploy hook target: feature environment (not yet implemented)"

      - name: Deploy to dev/integration
        if: ${{ !contains(github.ref_name, '-') }}
        run: echo "Stable ${{ github.ref_name }} — deploy hook target: dev/integration environment (not yet implemented)"
```

- [ ] **Step 2: Verify YAML is valid**

```bash
cd /home/buvy/code/NorseArchitecture/.github
gh workflow list 2>/dev/null || true
python3 -c "import yaml; yaml.safe_load(open('.github/workflows/release-container.yml'))" && echo "YAML valid"
```

Expected: `YAML valid` — no parse errors.

- [ ] **Step 3: Stage**

```bash
cd /home/buvy/code/NorseArchitecture/.github
git add .github/workflows/release-container.yml
```

Proposed commit message: `feat: add release-container.yml ceremony workflow for Yggdrasil`

---

## Task 3: Add `ci.yml` and `release.yml` thin callers to Yggdrasil

Two files in Yggdrasil at `.github/workflows/`. The directory does not exist yet.

**Files:**
- Create: `.github/workflows/ci.yml` in Yggdrasil
- Create: `.github/workflows/release.yml` in Yggdrasil

Filesystem paths (Yggdrasil is a submodule at `Bifrost/Yggdrasil/`):
- `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/.github/workflows/ci.yml`
- `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/.github/workflows/release.yml`

- [ ] **Step 1: Create the workflows directory**

```bash
mkdir -p /home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/.github/workflows
```

- [ ] **Step 2: Create `ci.yml`**

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
```

- [ ] **Step 3: Create `release.yml`**

```yaml
name: Release

on:
  push:
    tags:
      - 'v*.*.*'

permissions:
  contents: write
  packages: write
  pull-requests: write
  security-events: write

jobs:
  release:
    uses: NorseArchitecture/.github/.github/workflows/release-container.yml@master
```

- [ ] **Step 4: Verify both files are valid YAML**

```bash
python3 -c "
import yaml
for f in ['.github/workflows/ci.yml', '.github/workflows/release.yml']:
    yaml.safe_load(open(f))
    print(f'{f}: valid')
" 
```

Run from `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil`.
Expected:
```
.github/workflows/ci.yml: valid
.github/workflows/release.yml: valid
```

- [ ] **Step 5: Stage (in Yggdrasil)**

```bash
cd /home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil
git add .github/workflows/ci.yml .github/workflows/release.yml
```

Proposed commit message: `feat: add ci.yml and release.yml workflow callers`

---

## Task 4: Apply branch protection to Yggdrasil

`Yggdrasil` is already in `carve-the-laws.ps1`'s `$AllRepos` list (line 28) — no script edit needed. Run it scoped to Yggdrasil.

**Prerequisite:** Task 3's workflows must be pushed to Yggdrasil's `master` **and** at least one CI run must have completed on a PR so GitHub has recorded the `gate / build` check name. Branch protection requires a known check name to enforce. If no run exists yet, create a throwaway PR against Yggdrasil after pushing Task 3 to trigger `ci.yml`, verify the `gate / build` check appears in the PR's status, then run `carve-the-laws.ps1`.

- [ ] **Step 1: Confirm `gh` is authenticated and has repo admin**

```bash
gh auth status
```

Expected: authenticated as a user with admin on `NorseArchitecture/Yggdrasil`.

- [ ] **Step 2: Run `carve-the-laws.ps1` scoped to Yggdrasil**

```bash
cd /home/buvy/code/NorseArchitecture/.github
pwsh scripts/carve-the-laws.ps1 Yggdrasil
```

Expected output:
```
==> NorseArchitecture/Yggdrasil
    Applying repo settings...
    Repo settings applied.
    Carving the law anew...
    Created.
The laws are carved. Verify with:
  gh ruleset list -R NorseArchitecture/Asgard
```

(Or "Law already carved — re-inscribing... Updated." if re-running.)

- [ ] **Step 3: Verify the ruleset is live**

```bash
gh ruleset list -R NorseArchitecture/Yggdrasil
```

Expected: one ruleset named `Law of the Æsir` with status `active`.

- [ ] **Step 4: Verify the required status check name**

```bash
gh api repos/NorseArchitecture/Yggdrasil/rulesets \
  --jq '.[] | select(.name == "Law of the Æsir") | .rules[] | select(.type == "required_status_checks") | .parameters.required_status_checks'
```

Expected:
```json
[{"context": "gate / build", "integration_id": 15368}]
```

No stage/commit — `carve-the-laws.ps1` applies changes directly via the GitHub API.

---

## Task 5: Smoke test — pre-release tag end-to-end

**Hard prerequisite:** The Yggdrasil hosting skeleton must exist before this task. Specifically, the three projects must be in Yggdrasil's `master` and `dotnet publish /t:PublishContainer` must succeed for each:
- `src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj`
- `src/Hosting.Web.Server/Hosting.Web.Server.csproj`
- `src/Hosting.Worker/Hosting.Worker.csproj`

Each project must have `<ContainerBaseImage>` set in its `.csproj` (or a `Directory.Build.props` override) so .NET container publish picks the right base image. Minimum values:
- Web.Server: `mcr.microsoft.com/dotnet/nightly/aspnet:11.0.0-preview.5`
- Worker + Migrations.Service: `mcr.microsoft.com/dotnet/nightly/runtime:11.0.0-preview.5`

If the skeleton context has not run, **stop here and do not proceed.**

- [ ] **Step 1: Confirm the three projects build and publish containers locally**

```bash
cd /home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil
dotnet publish src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj \
  --os linux --arch arm64 -c Release /t:PublishContainer \
  /p:ContainerRepository=norsearchitecture/hosting/migrations \
  /p:ContainerImageTag=smoke-test
docker image ls norsearchitecture/hosting/migrations:smoke-test
```

Expected: image listed with `norsearchitecture/hosting/migrations` and tag `smoke-test`.
Repeat for `Hosting.Web.Server` and `Hosting.Worker` (same pattern, same check).
Clean up: `docker image rm norsearchitecture/hosting/migrations:smoke-test` (and the other two).

- [ ] **Step 2: Push a pre-release tag**

From the Yggdrasil repo root with all skeleton commits on `master`:

```bash
cd /home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil
git tag v0.0.1-beta.1
git push origin v0.0.1-beta.1
```

- [ ] **Step 3: Watch the release workflow run**

```bash
gh run watch --repo NorseArchitecture/Yggdrasil
```

Or watch in the GitHub Actions UI. Expected job sequence:
- `ci` and `codeql` start simultaneously
- `package` starts only after both complete
- `deploy-hook` starts only after `package` completes

All four jobs must be green.

- [ ] **Step 4: Verify three images in GHCR**

```bash
gh api /orgs/NorseArchitecture/packages?package_type=container \
  --jq '.[].name' | grep "hosting/"
```

Expected output includes:
```
hosting/migrations
hosting/web
hosting/worker
```

Then verify the tag on each:
```bash
gh api /orgs/NorseArchitecture/packages/container/hosting%2Fmigrations/versions \
  --jq '.[0].metadata.container.tags'
```

Expected: `["0.0.1-beta.1"]` — bare version, no `v` prefix, no `:latest`.

- [ ] **Step 5: Verify the GitHub Release**

```bash
gh release view v0.0.1-beta.1 --repo NorseArchitecture/Yggdrasil
```

Expected:
- Tag `v0.0.1-beta.1` with auto-generated notes
- Three SBOM files attached: `sbom-migrations.cdx.json`, `sbom-web.cdx.json`, `sbom-worker.cdx.json`

```bash
gh release view v0.0.1-beta.1 --repo NorseArchitecture/Yggdrasil --json assets \
  --jq '.assets[].name'
```

Expected:
```
sbom-migrations.cdx.json
sbom-web.cdx.json
sbom-worker.cdx.json
```

- [ ] **Step 6: Verify deploy-hook routing for pre-release**

In the `deploy-hook` job logs:
```bash
gh run view --repo NorseArchitecture/Yggdrasil --log | grep "deploy hook target"
```

Expected: `Pre-release v0.0.1-beta.1 — deploy hook target: feature environment (not yet implemented)`

- [ ] **Step 7: Smoke test stable tag**

```bash
cd /home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil
git tag v0.0.1
git push origin v0.0.1
```

After the run completes, verify deploy-hook log shows:
`Stable v0.0.1 — deploy hook target: dev/integration environment (not yet implemented)`

And verify GHCR has `0.0.1` tag (no `v`) on all three images.

- [ ] **Step 8: Confirm no `:latest` tag exists on any image**

```bash
for img in migrations web worker; do
  echo "=== hosting/$img ==="
  gh api /orgs/NorseArchitecture/packages/container/hosting%2F${img}/versions \
    --jq '.[].metadata.container.tags[]' | grep -x "latest" && echo "FAIL: latest tag found" || echo "OK: no latest tag"
done
```

Expected: `OK: no latest tag` for all three.

No stage/commit — smoke test is verification only.
