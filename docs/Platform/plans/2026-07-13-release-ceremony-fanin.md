# Release Ceremony Fan-In Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: `superpowers:subagent-driven-development` — the default, not a recommendation among equals (`superpowers:executing-plans` is the narrow fallback for a separate session with human review checkpoints) — paired with `superpowers:test-driven-development`. This repo's own CLAUDE.md notes there is no unit-test runner for pure workflow YAML; the TDD substitute here is (a) local YAML syntax validation before every stage-and-stop, and (b) a real tag push producing a real green run as the "receipts" — same discipline the original release-pipeline plan used, just with a runtime check instead of a unit test. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restructure the release ceremony so a pushed tag remains the sole inception point, but release creation moves from a step embedded in each publish job to a single fan-in job — fixing Naglfar's dual-publish race and closing `release-npm.yml`'s missing CodeQL gate — without touching the PR gate.

**Architecture:** Five reusable workflows in `NorseArchitecture/.github`: `ci-build-test.yml` (unchanged), a new language-parameterized `codeql.yml`, a new `create-release.yml` that downloads every publish job's uploaded artifacts and creates the release in one shot, and `publish-nuget.yml`/`publish-npm.yml`/`publish-container.yml` trimmed from their current `release-*.yml` shape to publish-only (pack/push/scan, then `upload-artifact` — no more `gh release create`). Each realm's own thin `release.yml` caller lists exactly the publish jobs it ships and points `create-release`'s `needs:` at them, so a single-target realm and Naglfar's dual-target realm are the same shape with a different `needs:` width.

**Tech Stack:** GitHub Actions (`workflow_call` reusable workflows), `github/codeql-action`, `anchore/sbom-action`, `aquasecurity/trivy-action`, `actions/upload-artifact@v4` / `actions/download-artifact@v4`, `gh` CLI.

## Global Constraints

- **Spec:** `../Glitnir/docs/Platform/specs/2026-07-13-release-ceremony-fanin-design.md`. Every ruling in that spec is binding; this plan does not re-derive or re-litigate them.
- **No automatic git commits, pushes, merges, tags, or `gh release`/`gh pr merge` actions by the executor — ever, in any repo touched by this plan.** The executor edits files, runs `git add`, shows the diff, and **stops**. Buvy runs every commit, push, tag, and merge himself. After he confirms an action is done, the executor may resume using read-only `gh`/`git` commands to verify — never to mutate.
- **Branch is `master`, never `main`,** in every repo this plan touches.
- **`NorseArchitecture/.github` is a sibling clone**, not a Bifrost submodule — already present at `../.github` relative to the Bifrost workspace root (confirmed at plan-authoring time; do not re-clone). Naglfar and Yggdrasil are existing Bifrost submodules at `Naglfar/` and `Yggdrasil/`.
- **`fetch-depth: 0` is not needed anywhere touched by this plan** — none of the jobs being written or edited use MinVer directly (MinVer already runs inside each realm's own build, unaffected by this restructuring).
- **`workflow_call` permission chain:** a caller's `permissions:` block must grant everything any job it calls (transitively) declares, or GitHub rejects the workflow at parse time. Confirmed empirically 2026-06-27, restated in the 2026-06-19 plan. Every realm caller in this plan keeps `contents: write, packages: write, security-events: write, pull-requests: write` — the union of what `ci-build-test.yml` (`pull-requests: write`), `codeql.yml` (`security-events: write`), `publish-*.yml` (`packages: write`), and `create-release.yml` (`contents: write`) each declare.
- **NuGet source — do not add inline.** `nuget.config` is already scattered and present at checkout; never add an inline `dotnet nuget add source` step (confirmed 2026-06-27, breaks with "name already added").
- **Composite actions cannot be called cross-repo from within a reusable workflow** — the runner checks out the *caller's* repo, so any `./` path resolves there, not in `.github`. This is why every reusable workflow in this plan inlines its own checkout/setup steps rather than calling a composite action.
- **Action versions pin to major only**, matching existing convention in every file this plan touches: `actions/checkout@v7`, `actions/setup-dotnet@v5`, `actions/setup-node@v4`, `github/codeql-action/{init,analyze}@v4`, `anchore/sbom-action@v0`, `docker/login-action@v4`, `aquasecurity/trivy-action@master` (existing pin, unchanged). New to this plan: `actions/upload-artifact@v4`, `actions/download-artifact@v4`.
- **YAML syntax validation substitutes for a test run** at every stage-and-stop in Tasks 1–5 (no live workflow exists yet to trigger): `python3 -c "import yaml; yaml.safe_load(open('<path>'))"` must exit 0 before staging. Tasks 6–8 additionally require a real tag push and a real green run — the actual proof, not just syntax.
- **US English spelling** in every file, commit message, and PR/release title touched by this plan.

---

## File Structure

In `../.github` (sibling clone of `NorseArchitecture/.github`):
- `.github/workflows/codeql.yml` — new. Reusable, `language`-parameterized CodeQL scan.
- `.github/workflows/create-release.yml` — new. Reusable fan-in: downloads every `*-artifacts` bundle, one `gh release create`.
- `.github/workflows/publish-nuget.yml` — renamed from `release-nuget.yml`, trimmed to publish-only.
- `.github/workflows/publish-npm.yml` — renamed from `release-npm.yml`, trimmed to publish-only.
- `.github/workflows/publish-container.yml` — renamed from `release-container.yml`, trimmed to publish-only.
- `config/.github/workflows/release.yml` — modified. Canonical single-target template scattered to every default NuGet realm.

In `Naglfar/` (existing Bifrost submodule):
- `.github/workflows/release.yml` — modified. Bespoke dual npm+NuGet caller (not scattered — see `config/manifest.psd1`'s `release` group comment).

In `Yggdrasil/` (existing Bifrost submodule):
- `.github/workflows/release.yml` — modified. Bespoke container caller, includes `deploy-hook`.

---

### Task 1: Author the language-parameterized CodeQL reusable workflow

**Files:**
- Create: `../.github/.github/workflows/codeql.yml`

**Interfaces:**
- Produces: a `workflow_call` reusable workflow taking one required string input, `language` (`csharp` or `javascript-typescript`), with one job, id `analyze`. Consumed by Task 6 (canonical `release.yml`, `language: csharp`), Task 7 (Naglfar, two calls: `csharp` and `javascript-typescript`), Task 8 (Yggdrasil, `language: csharp`).

- [ ] **Step 1: Write the reusable workflow**

Create `../.github/.github/workflows/codeql.yml`:

```yaml
name: CodeQL

# Setup steps (checkout, .NET/Node, restore) are intentionally inline.
# Composite actions cannot be called from within a reusable workflow — the runner
# checks out the CALLER's repository, so any ./ path resolves there, not here.

on:
  workflow_call:
    inputs:
      language:
        description: 'CodeQL language to analyze (csharp, javascript-typescript)'
        type: string
        required: true

permissions:
  security-events: write
  contents: read

env:
  DOTNET_VERSION: "11.0.x"
  DOTNET_QUALITY: "preview"

jobs:
  analyze:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v7

      - name: Setup .NET
        if: inputs.language == 'csharp'
        uses: actions/setup-dotnet@v5
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}
          dotnet-quality: ${{ env.DOTNET_QUALITY }}

      - name: Setup Node
        if: inputs.language == 'javascript-typescript'
        uses: actions/setup-node@v4
        with:
          node-version: '22'

      - name: Initialize CodeQL
        uses: github/codeql-action/init@v4
        with:
          languages: ${{ inputs.language }}

      - name: Restore and build (csharp)
        if: inputs.language == 'csharp'
        env:
          NUGET_AUTH_TOKEN: ${{ secrets.PACKAGES_READ_TOKEN }}
        run: |
          dotnet restore
          dotnet build -c Release

      - name: Install dependencies (javascript-typescript)
        if: inputs.language == 'javascript-typescript'
        run: npm ci

      - name: Analyze
        uses: github/codeql-action/analyze@v4
```

The `csharp` branch is copied from the CodeQL job already proven in `release-nuget.yml`/`release-container.yml` — same steps, same env vars, just guarded by `if:`. The `javascript-typescript` branch is new: JS/TS extraction doesn't require a compiled build the way C# does, so `npm ci` (populating `node_modules` so CodeQL's extractor sees real dependency resolution) is the only setup step needed before `analyze`.

- [ ] **Step 2: Validate YAML syntax**

```bash
python3 -c "import yaml; yaml.safe_load(open('../.github/.github/workflows/codeql.yml'))" && echo OK
```

Expected: `OK`.

- [ ] **Step 3: Stage and stop**

```bash
git -C ../.github add .github/workflows/codeql.yml
git -C ../.github diff --cached
```

**STOP.** Buvy commits and pushes directly to `.github`'s `master` (this repo has no PR gate of its own — see the 2026-06-19 plan's Task 1 for precedent):

```bash
git -C ../.github commit -m "Add language-parameterized codeql.yml reusable workflow"
git -C ../.github push origin master
```

Do not run these two commands yourself. Wait for Buvy to confirm before Task 2.

---

### Task 2: Author the fan-in release-creation reusable workflow

**Files:**
- Create: `../.github/.github/workflows/create-release.yml`

**Interfaces:**
- Consumes: artifact bundles uploaded by any publish job under a name ending in `-artifacts` (produced by Tasks 3–5).
- Produces: a `workflow_call` reusable workflow with one job, id `create-release`, that creates the tag's GitHub Release with every downloaded file attached. Consumed by Tasks 6–8.

- [ ] **Step 1: Write the reusable workflow**

Create `../.github/.github/workflows/create-release.yml`:

```yaml
name: Create Release

# This workflow needs no checkout: gh release create talks to the GitHub API directly
# once given --repo, and every file it attaches comes from downloaded publish artifacts.

on:
  workflow_call:

permissions:
  contents: write

jobs:
  create-release:
    runs-on: ubuntu-latest
    steps:
      - name: Download all publish artifacts
        uses: actions/download-artifact@v4
        with:
          pattern: '*-artifacts'
          path: ./release-assets
          merge-multiple: true

      - name: Create GitHub Release
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          gh release create "${{ github.ref_name }}" \
            --repo "${{ github.repository }}" \
            ./release-assets/* \
            --generate-notes
```

`pattern: '*-artifacts'` with `merge-multiple: true` matches every publish job's upload regardless of how many ran (`nuget-artifacts` alone, or `nuget-artifacts` + `npm-artifacts` together) and flattens them into one directory — this reusable workflow never needs to know which or how many targets a given realm ships.

- [ ] **Step 2: Validate YAML syntax**

```bash
python3 -c "import yaml; yaml.safe_load(open('../.github/.github/workflows/create-release.yml'))" && echo OK
```

Expected: `OK`.

- [ ] **Step 3: Stage and stop**

```bash
git -C ../.github add .github/workflows/create-release.yml
git -C ../.github diff --cached
```

**STOP.** Buvy commits and pushes directly to `master`:

```bash
git -C ../.github commit -m "Add create-release.yml fan-in reusable workflow"
git -C ../.github push origin master
```

Wait for his confirmation before Task 3.

---

### Task 3: Trim `release-nuget.yml` to publish-only (`publish-nuget.yml`)

**Files:**
- Rename: `../.github/.github/workflows/release-nuget.yml` → `../.github/.github/workflows/publish-nuget.yml`

**Interfaces:**
- Produces: a `workflow_call` reusable workflow with one job, id `publish-nuget`, no `needs:` (gating now happens at the caller level — Tasks 6–7). Uploads artifact bundle `nuget-artifacts` (the `.nupkg`(s) plus the CycloneDX SBOM). Consumed by Task 6 and Task 7.

- [ ] **Step 1: Rename with git mv to preserve history**

```bash
git -C ../.github mv .github/workflows/release-nuget.yml .github/workflows/publish-nuget.yml
```

- [ ] **Step 2: Rewrite the file**

Replace the full contents of `../.github/.github/workflows/publish-nuget.yml` with:

```yaml
name: Publish NuGet

# Setup steps (checkout, .NET, NuGet source, restore) are intentionally inline.
# Composite actions cannot be called from within a reusable workflow — the runner
# checks out the CALLER's repository, so any ./ path resolves there, not here.
#
# build-test and codeql are no longer called from inside this workflow — the caller's
# release.yml runs them once and gates every publish-* job on them via needs:, so a
# multi-target realm (Naglfar) doesn't pay for two redundant build-test/codeql runs.

on:
  workflow_call:

permissions:
  packages: write

env:
  DOTNET_VERSION: "11.0.x"
  DOTNET_QUALITY: "preview"

jobs:
  publish-nuget:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v7

      - name: Setup .NET
        uses: actions/setup-dotnet@v5
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}
          dotnet-quality: ${{ env.DOTNET_QUALITY }}

      - name: Pack
        env:
          NUGET_AUTH_TOKEN: ${{ secrets.PACKAGES_READ_TOKEN }}
        run: dotnet pack -c Release -o ./artifacts/nupkg

      - name: Generate SBOM
        uses: anchore/sbom-action@v0
        with:
          path: .
          format: cyclonedx-json
          output-file: sbom.cyclonedx.json

      - name: Push to GitHub Packages
        run: dotnet nuget push "./artifacts/nupkg/*.nupkg" --api-key ${{ secrets.GITHUB_TOKEN }} --source "https://nuget.pkg.github.com/NorseArchitecture/index.json" --skip-duplicate

      - name: Upload release assets
        uses: actions/upload-artifact@v4
        with:
          name: nuget-artifacts
          path: |
            ./artifacts/nupkg/*.nupkg
            ./sbom.cyclonedx.json
          retention-days: 7
```

- [ ] **Step 3: Validate YAML syntax**

```bash
python3 -c "import yaml; yaml.safe_load(open('../.github/.github/workflows/publish-nuget.yml'))" && echo OK
```

Expected: `OK`.

- [ ] **Step 4: Stage and stop**

```bash
git -C ../.github add .github/workflows/publish-nuget.yml .github/workflows/release-nuget.yml
git -C ../.github diff --cached
```

**STOP.** Buvy commits and pushes directly to `master`:

```bash
git -C ../.github commit -m "Trim release-nuget.yml to publish-only, rename to publish-nuget.yml"
git -C ../.github push origin master
```

Wait for his confirmation before Task 4.

---

### Task 4: Trim `release-npm.yml` to publish-only (`publish-npm.yml`)

**Files:**
- Rename: `../.github/.github/workflows/release-npm.yml` → `../.github/.github/workflows/publish-npm.yml`

**Interfaces:**
- Produces: a `workflow_call` reusable workflow with one job, id `publish-npm`, no `needs:`. Uploads artifact bundle `npm-artifacts` (the `.tgz` plus the CycloneDX SBOM). Consumed by Task 7.

- [ ] **Step 1: Rename with git mv to preserve history**

```bash
git -C ../.github mv .github/workflows/release-npm.yml .github/workflows/publish-npm.yml
```

- [ ] **Step 2: Rewrite the file**

Replace the full contents of `../.github/.github/workflows/publish-npm.yml` with:

```yaml
name: Publish npm

# build-test and codeql are no longer called from inside this workflow — see the
# same comment in publish-nuget.yml for why.

on:
  workflow_call:

permissions:
  packages: write

jobs:
  publish-npm:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v7

      - name: Setup Node
        uses: actions/setup-node@v4
        with:
          node-version: '22'
          registry-url: 'https://npm.pkg.github.com'
          scope: '@norsearchitecture'

      - name: Install dependencies
        run: npm ci

      - name: Set version from tag
        run: npm version "${GITHUB_REF_NAME#v}" --no-git-tag-version --allow-same-version

      - name: Build
        run: npm run build

      - name: Generate SBOM
        uses: anchore/sbom-action@v0
        with:
          path: .
          format: cyclonedx-json
          output-file: sbom.cyclonedx.json

      - name: Pack
        run: npm pack

      - name: Publish to GitHub Packages
        run: |
          PKG_VERSION=$(node -p "require('./package.json').version")
          PKG_NAME=$(node -p "require('./package.json').name")
          if npm view "$PKG_NAME@$PKG_VERSION" version --registry https://npm.pkg.github.com &>/dev/null; then
            echo "Version $PKG_VERSION of $PKG_NAME already published, skipping."
          else
            npm publish
          fi
        env:
          NODE_AUTH_TOKEN: ${{ secrets.GITHUB_TOKEN }}

      - name: Upload release assets
        uses: actions/upload-artifact@v4
        with:
          name: npm-artifacts
          path: |
            ./*.tgz
            ./sbom.cyclonedx.json
          retention-days: 7
```

- [ ] **Step 3: Validate YAML syntax**

```bash
python3 -c "import yaml; yaml.safe_load(open('../.github/.github/workflows/publish-npm.yml'))" && echo OK
```

Expected: `OK`.

- [ ] **Step 4: Stage and stop**

```bash
git -C ../.github add .github/workflows/publish-npm.yml .github/workflows/release-npm.yml
git -C ../.github diff --cached
```

**STOP.** Buvy commits and pushes directly to `master`:

```bash
git -C ../.github commit -m "Trim release-npm.yml to publish-only, rename to publish-npm.yml"
git -C ../.github push origin master
```

Wait for his confirmation before Task 5.

---

### Task 5: Trim `release-container.yml` to publish-only (`publish-container.yml`)

**Files:**
- Rename: `../.github/.github/workflows/release-container.yml` → `../.github/.github/workflows/publish-container.yml`

**Interfaces:**
- Produces: a `workflow_call` reusable workflow with one job, id `publish-container`, no `needs:`. Uploads artifact bundle `container-artifacts` (the four Trivy CycloneDX SBOMs — migrations, web, worker, stories). Consumed by Task 8.

- [ ] **Step 1: Rename with git mv to preserve history**

```bash
git -C ../.github mv .github/workflows/release-container.yml .github/workflows/publish-container.yml
```

- [ ] **Step 2: Rewrite the file**

Replace the full contents of `../.github/.github/workflows/publish-container.yml` with:

```yaml
name: Publish Container

# Setup steps (checkout, .NET, NuGet source) are intentionally inline.
# Composite actions cannot be called from within a reusable workflow — the runner
# checks out the CALLER's repository, so any ./ path resolves there, not here.
#
# ci and codeql are no longer called from inside this workflow — see the same
# comment in publish-nuget.yml for why.

on:
  workflow_call:

permissions:
  packages: write

env:
  DOTNET_VERSION: "11.0.x"
  DOTNET_QUALITY: "preview"

jobs:
  publish-container:
    runs-on: ubuntu-latest
    env:
      NUGET_AUTH_TOKEN: ${{ secrets.PACKAGES_READ_TOKEN }}
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

      # ── stories ─────────────────────────────────────────────────────────────

      - name: Publish stories image (local daemon)
        run: |
          dotnet publish src/Hosting.Stories.Server/Hosting.Stories.Server.csproj \
            --os linux --arch x64 -c Release /t:PublishContainer \
            /p:ContainerRepository=norsearchitecture/hosting/stories \
            /p:ContainerImageTag=${{ steps.version.outputs.value }}

      - name: Scan stories image
        uses: aquasecurity/trivy-action@master
        with:
          image-ref: norsearchitecture/hosting/stories:${{ steps.version.outputs.value }}
          format: cyclonedx
          output: sbom-stories.cdx.json
          exit-code: '1'
          severity: HIGH,CRITICAL

      - name: Push stories image
        run: |
          docker tag norsearchitecture/hosting/stories:${{ steps.version.outputs.value }} \
            ghcr.io/norsearchitecture/hosting/stories:${{ steps.version.outputs.value }}
          docker push ghcr.io/norsearchitecture/hosting/stories:${{ steps.version.outputs.value }}

      # ── artifacts ───────────────────────────────────────────────────────────

      - name: Upload release assets
        uses: actions/upload-artifact@v4
        with:
          name: container-artifacts
          path: |
            sbom-migrations.cdx.json
            sbom-web.cdx.json
            sbom-worker.cdx.json
            sbom-stories.cdx.json
          retention-days: 7
```

The four `dotnet publish .../t:PublishContainer` → `Scan` → `Push` sequences are unchanged from the current file — only the trailing `Create GitHub Release` step is removed and replaced with the `Upload release assets` step, and the job is renamed from `package` to `publish-container` with its `needs: [ci, codeql]` dropped (gating moves to the caller, Task 8).

- [ ] **Step 3: Validate YAML syntax**

```bash
python3 -c "import yaml; yaml.safe_load(open('../.github/.github/workflows/publish-container.yml'))" && echo OK
```

Expected: `OK`.

- [ ] **Step 4: Stage and stop**

```bash
git -C ../.github add .github/workflows/publish-container.yml .github/workflows/release-container.yml
git -C ../.github diff --cached
```

**STOP.** Buvy commits and pushes directly to `master`:

```bash
git -C ../.github commit -m "Trim release-container.yml to publish-only, rename to publish-container.yml"
git -C ../.github push origin master
```

Wait for his confirmation before Task 6.

---

### Task 6: Rewrite the canonical `release.yml` template, scatter it, prove against Svartalfheim

**Files:**
- Modify: `../.github/config/.github/workflows/release.yml`

**Interfaces:**
- Consumes: `codeql.yml` (Task 1), `create-release.yml` (Task 2), `publish-nuget.yml` (Task 3), all `@master`.
- Produces: the template scattered by `scatter-the-runes.yml` to every default NuGet realm (Asgard, Svartalfheim, Midgard, Urdarbrunnr, Himinbjorg, Heimdall today).

- [ ] **Step 1: Rewrite the canonical template**

Replace the full contents of `../.github/config/.github/workflows/release.yml` with:

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
  pull-requests: write

jobs:
  build-test:
    uses: NorseArchitecture/.github/.github/workflows/ci-build-test.yml@master
    secrets: inherit

  codeql:
    uses: NorseArchitecture/.github/.github/workflows/codeql.yml@master
    secrets: inherit
    with:
      language: csharp

  publish-nuget:
    needs: [build-test, codeql]
    uses: NorseArchitecture/.github/.github/workflows/publish-nuget.yml@master
    secrets: inherit

  create-release:
    needs: [publish-nuget]
    uses: NorseArchitecture/.github/.github/workflows/create-release.yml@master
    secrets: inherit

  sound-gjallarhorn:
    needs: [publish-nuget]
    uses: NorseArchitecture/.github/.github/workflows/sound-gjallarhorn.yml@master
    secrets:
      token: ${{ secrets.SCATTER_PAT }}
```

`sound-gjallarhorn` now depends on `publish-nuget` directly instead of the old monolithic `release` job — it only needs the package live on GitHub Packages, not the release object to exist (spec §2.5), so it now runs in parallel with `create-release` instead of waiting on it.

- [ ] **Step 2: Validate YAML syntax**

```bash
python3 -c "import yaml; yaml.safe_load(open('../.github/config/.github/workflows/release.yml'))" && echo OK
```

Expected: `OK`.

- [ ] **Step 3: Stage and stop**

```bash
git -C ../.github add config/.github/workflows/release.yml
git -C ../.github diff --cached
```

**STOP.** Buvy commits and pushes directly to `master`. Since this path is under `config/**`, the push itself auto-triggers `scatter-the-runes.yml`, which opens (or updates) an auto-merge PR against every default NuGet realm:

```bash
git -C ../.github commit -m "Restructure canonical release.yml for tag-inception fan-in"
git -C ../.github push origin master
```

Do not run this yourself. Wait for Buvy's confirmation before Step 4.

- [ ] **Step 4: Verify the scatter PRs landed (read-only)**

```bash
gh pr list -R NorseArchitecture/Svartalfheim --search "scatter" --json number,title,state
```

Expected: an auto-merge PR touching `.github/workflows/release.yml`. Repeat for the other default NuGet realms (Asgard, Midgard, Urdarbrunnr, Himinbjorg, Heimdall) if any have buildable code and existing tags — realms with no code yet won't have anything to prove against, so skip proof for those and only confirm the PR opened/merged.

- [ ] **Step 5: Wait for the scatter PR to auto-merge, then cut a proof tag on Svartalfheim**

Once the scatter PR against Svartalfheim shows merged (`gh pr view <number> -R NorseArchitecture/Svartalfheim --json state,mergedAt`), Buvy determines the next patch tag and cuts it:

```bash
git -C Svartalfheim fetch --tags
git -C Svartalfheim tag --sort=-v:refname | head -5
```

Buvy picks the next patch version off whatever's listed (e.g. if the latest is `v0.0.3`, the next is `v0.0.4`) and runs:

```bash
git -C Svartalfheim tag -a v0.0.4 -m "Prove tag-inception fan-in release ceremony"
git -C Svartalfheim push origin v0.0.4
```

**Do not run these yourself** — substitute the actual next version Buvy confirms.

- [ ] **Step 6: Verify the full ceremony (read-only)**

```bash
gh run list -R NorseArchitecture/Svartalfheim --workflow=release.yml --limit 1
gh run view -R NorseArchitecture/Svartalfheim <run-id>
```

Expected: `build-test`, `codeql`, `publish-nuget`, `create-release`, and `sound-gjallarhorn` all succeed, with `create-release` and `sound-gjallarhorn` starting around the same time (both gated on `publish-nuget`, not on each other). Then confirm the release itself:

```bash
gh release view <tag> -R NorseArchitecture/Svartalfheim
```

Expected: the release lists both the `.nupkg` and `sbom.cyclonedx.json` as attached assets — the fan-in job correctly picked up `publish-nuget`'s uploaded artifact bundle.

---

### Task 7: Rewrite Naglfar's bespoke `release.yml`, prove the dual-target ceremony

**Files:**
- Modify: `Naglfar/.github/workflows/release.yml`

**Interfaces:**
- Consumes: `codeql.yml` (Task 1, called twice), `create-release.yml` (Task 2), `publish-nuget.yml` (Task 3), `publish-npm.yml` (Task 4), all `@master`.
- Produces: on a tag push to Naglfar, one release containing both the `.nupkg`/nuget SBOM and the `.tgz`/npm SBOM, created exactly once regardless of publish-job ordering.

- [ ] **Step 1: Rewrite Naglfar's release.yml**

Replace the full contents of `Naglfar/.github/workflows/release.yml` with:

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
  pull-requests: write

jobs:
  build-test:
    uses: NorseArchitecture/.github/.github/workflows/ci-build-test.yml@master
    secrets: inherit

  codeql-csharp:
    uses: NorseArchitecture/.github/.github/workflows/codeql.yml@master
    secrets: inherit
    with:
      language: csharp

  codeql-js:
    uses: NorseArchitecture/.github/.github/workflows/codeql.yml@master
    secrets: inherit
    with:
      language: javascript-typescript

  publish-nuget:
    needs: [build-test, codeql-csharp, codeql-js]
    uses: NorseArchitecture/.github/.github/workflows/publish-nuget.yml@master
    secrets: inherit

  publish-npm:
    needs: [build-test, codeql-csharp, codeql-js]
    uses: NorseArchitecture/.github/.github/workflows/publish-npm.yml@master
    secrets: inherit

  create-release:
    needs: [publish-nuget, publish-npm]
    uses: NorseArchitecture/.github/.github/workflows/create-release.yml@master
    secrets: inherit

  sound-gjallarhorn:
    needs: [publish-nuget]
    uses: NorseArchitecture/.github/.github/workflows/sound-gjallarhorn.yml@master
    secrets:
      token: ${{ secrets.SCATTER_PAT }}
```

Both `publish-nuget` and `publish-npm` wait on *both* CodeQL jobs (spec §2.3 — CodeQL scans the whole repo, not a specific target, so partitioning by language would be a false signal). `create-release` needs both publish jobs — this is the fix for the race described in the spec's Context: previously each of `release-npm`/`release-nuget` ended with its own `gh release create`; now exactly one job creates it, after both targets are confirmed live.

- [ ] **Step 2: Validate YAML syntax**

```bash
python3 -c "import yaml; yaml.safe_load(open('Naglfar/.github/workflows/release.yml'))" && echo OK
```

Expected: `OK`.

- [ ] **Step 3: Stage and stop**

```bash
git -C Naglfar add .github/workflows/release.yml
git -C Naglfar diff --cached
```

**STOP.** Per Naglfar's own CLAUDE.md, Buvy reviews and commits/pushes himself (directly to `master`, or via a small PR — his call):

```bash
git -C Naglfar commit -m "Restructure release.yml for tag-inception fan-in"
git -C Naglfar push origin master
```

Wait for his confirmation before Step 4.

- [ ] **Step 4: Cut a proof tag on Naglfar**

```bash
git -C Naglfar fetch --tags
git -C Naglfar tag --sort=-v:refname | head -5
```

Buvy picks the next patch version and runs (substitute the actual version):

```bash
git -C Naglfar tag -a v0.0.X -m "Prove dual-target tag-inception fan-in release ceremony"
git -C Naglfar push origin v0.0.X
```

**Do not run these yourself.**

- [ ] **Step 5: Verify the full ceremony (read-only)**

```bash
gh run list -R NorseArchitecture/Naglfar --workflow=release.yml --limit 1
gh run view -R NorseArchitecture/Naglfar <run-id>
```

Expected: `build-test`, `codeql-csharp`, `codeql-js`, `publish-nuget`, `publish-npm`, `create-release`, and `sound-gjallarhorn` all succeed. Confirm the release has both targets' assets attached:

```bash
gh release view <tag> -R NorseArchitecture/Naglfar
```

Expected: the release lists the `.tgz`, the npm SBOM, the `.nupkg`, and the NuGet SBOM — four files from two independent publish jobs, attached by exactly one `create-release` run. This is the direct fix for the race in the spec's Context — confirm by checking there is exactly one release for this tag, not two conflicting attempts (`gh release list -R NorseArchitecture/Naglfar --limit 5` should show one entry for this tag).

---

### Task 8: Rewrite Yggdrasil's bespoke `release.yml`, retarget `deploy-hook`, prove the container ceremony

**Files:**
- Modify: `Yggdrasil/.github/workflows/release.yml`

**Interfaces:**
- Consumes: `codeql.yml` (Task 1), `create-release.yml` (Task 2), `publish-container.yml` (Task 5), all `@master`.
- Produces: on a tag push to Yggdrasil, four container images pushed to GHCR (unchanged), a release with the four Trivy SBOMs attached, and `deploy-hook` running once images are live rather than once the release exists.

- [ ] **Step 1: Rewrite Yggdrasil's release.yml**

Replace the full contents of `Yggdrasil/.github/workflows/release.yml` with:

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
  pull-requests: write

jobs:
  build-test:
    uses: NorseArchitecture/.github/.github/workflows/ci-build-test.yml@master
    secrets: inherit

  codeql:
    uses: NorseArchitecture/.github/.github/workflows/codeql.yml@master
    secrets: inherit
    with:
      language: csharp

  publish-container:
    needs: [build-test, codeql]
    uses: NorseArchitecture/.github/.github/workflows/publish-container.yml@master
    secrets: inherit

  create-release:
    needs: [publish-container]
    uses: NorseArchitecture/.github/.github/workflows/create-release.yml@master
    secrets: inherit

  deploy-hook:
    needs: [publish-container]
    runs-on: ubuntu-latest
    steps:
      - name: Deploy to feature environment
        if: ${{ contains(github.ref_name, '-') }}
        run: |
          echo "Pre-release ${{ github.ref_name }} — deploy hook target: feature environment (not yet implemented)"

      - name: Deploy to dev/integration
        if: ${{ !contains(github.ref_name, '-') }}
        run: |
          echo "Stable ${{ github.ref_name }} — deploy hook target: dev/integration environment (not yet implemented)"
```

`deploy-hook` moves from `needs: [package]` (the old monolithic job) to `needs: [publish-container]` — it cares that the images are live in GHCR, not that the release notes exist yet (spec §2.5), so it now runs in parallel with `create-release` instead of waiting on it.

- [ ] **Step 2: Validate YAML syntax**

```bash
python3 -c "import yaml; yaml.safe_load(open('Yggdrasil/.github/workflows/release.yml'))" && echo OK
```

Expected: `OK`.

- [ ] **Step 3: Stage and stop**

```bash
git -C Yggdrasil add .github/workflows/release.yml
git -C Yggdrasil diff --cached
```

**STOP.** Buvy reviews and commits/pushes himself:

```bash
git -C Yggdrasil commit -m "Restructure release.yml for tag-inception fan-in, retarget deploy-hook"
git -C Yggdrasil push origin master
```

Wait for his confirmation before Step 4.

- [ ] **Step 4: Cut a proof tag on Yggdrasil**

```bash
git -C Yggdrasil fetch --tags
git -C Yggdrasil tag --sort=-v:refname | head -5
```

Buvy picks the next patch version and runs (substitute the actual version):

```bash
git -C Yggdrasil tag -a v0.0.X -m "Prove container tag-inception fan-in release ceremony"
git -C Yggdrasil push origin v0.0.X
```

**Do not run these yourself.**

- [ ] **Step 5: Verify the full ceremony (read-only)**

```bash
gh run list -R NorseArchitecture/Yggdrasil --workflow=release.yml --limit 1
gh run view -R NorseArchitecture/Yggdrasil <run-id>
```

Expected: `build-test`, `codeql`, `publish-container`, `create-release`, and `deploy-hook` all succeed, with `create-release` and `deploy-hook` starting around the same time (both gated on `publish-container`, not on each other). Confirm the images and the release:

```bash
gh release view <tag> -R NorseArchitecture/Yggdrasil
```

Expected: the release lists all four Trivy SBOMs (`sbom-migrations.cdx.json`, `sbom-web.cdx.json`, `sbom-worker.cdx.json`, `sbom-stories.cdx.json`) as attached assets.

```bash
gh api /orgs/NorseArchitecture/packages/container/hosting%2Fweb/versions --jq '.[0].metadata.container.tags'
```

Expected: the tag list includes the version just pushed, confirming the image actually landed in GHCR.

---

## Self-Review

**Spec coverage:** §2.1 job graph shape → Tasks 6–8 (each realm caller). §2.2 publish-only reusable workflows → Tasks 3–5. §2.3 CodeQL parameterization and the `release-npm.yml` gap fix → Task 1, applied in Task 7's `codeql-js`. §2.4 `create-release.yml` fan-in with `--generate-notes` default → Task 2. §2.5 `sound-gjallarhorn`/`deploy-hook` retargeting → Task 6 (gjallarhorn) and Task 8 (deploy-hook); container-realms-never-get-gjallarhorn is honored by Task 8 simply never adding that job. The banked Yggdrasil/npm-tokens follow-on (§2.5) has deliberately no task — nothing to build yet.

**Placeholder scan:** no TBD/TODO; every YAML block is complete and matches either an existing proven file (Tasks 3–5's unchanged sections) or is fully written out (Tasks 1, 2, 6, 7, 8).

**Type/name consistency:** artifact names `nuget-artifacts` (Task 3) / `npm-artifacts` (Task 4) / `container-artifacts` (Task 5) all match the `*-artifacts` download pattern in Task 2, and are referenced identically in Tasks 6–8's verification steps. Job ids `publish-nuget`/`publish-npm`/`publish-container` (Tasks 3–5) match exactly what Tasks 6–8's `needs:` arrays and `uses:` job names reference. `codeql.yml`'s `language` input values (`csharp`, `javascript-typescript`, Task 1) match every caller in Tasks 6–8.
