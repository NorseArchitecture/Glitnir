# Yggdrasil Tag & Release Pipeline

**Date:** 2026-06-27
**Status:** Approved in session 2026-06-27.
**Owner:** Buvy
**Companion specs:** `2026-06-19-ci-release-pipeline-design.md` (the platform-wide CI/release shape this extends — Yggdrasil's ceremony was deferred there as §2.7 pending this design); `2026-06-26-phone-home-nuget-cpm-design.md` (the inward fan of version signals from NuGet realms to Yggdrasil — this design is the downstream terminus of that chain).

---

## 1. Context

Yggdrasil (`Norse.Hosting.*`) holds two roles simultaneously, and the release story must honor both:

**CPM composition root.** `Directory.Packages.props` in Yggdrasil is the authoritative bill of materials for the Norse platform — one `<{Realm}Version>` property per NuGet-shipping realm, all in one file. Phone-home PRs from releasing realms update those properties automatically; when they land and CI passes, master reflects the current best-known composition. No tag required for that accumulation to happen.

**Container shipper.** When the composition is ready to leave the developer bridge (Bifrost) and run in the cloud, Yggdrasil is built into a container image and pushed to GHCR. That is the artifact cloud environments consume.

The tag is the bridge between these two roles.

## 2. Rulings

### 2.1 What a tag means

A Yggdrasil tag `vX.Y.Z` is a deliberate human act with two simultaneous meanings:

1. **Composition snapshot.** The `Directory.Packages.props` at that commit is a declared, blessed combination of Norse realm versions. Not necessarily the latest of every realm — the human reviewed the accumulated phone-home changes and judged the composition coherent and ready.

2. **Cloud-readiness gate.** The tag authorizes the container build and GHCR push. No image reaches the registry without it. This prevents container explosion: four or five teams may orchestrate realm releases in concert over days before the composition is considered cloud-ready. Phone-home PRs accumulate on master freely; the registry stays quiet until the gate opens.

The person who types the tag owns the composition. They are on record as the composition author and triage lead if the image causes problems downstream. The `git log --tags` is the accountability trail. This is the "one throat to choke" convention — not punitive, but clarifying: when things go south, triage leadership is not a question.

Nothing upstream of the tag is automated. Nothing infers readiness independently of the human decision. Same posture as every other realm (§2.5 of the CI/release pipeline spec).

### 2.2 What Yggdrasil does not do

- **No NuGet packages.** Yggdrasil ships a container image, not library packages. It does not call `release-nuget.yml`.
- **No phone-home.** Yggdrasil is the terminus of the phone-home chain. It receives version signals; it sends none. There is no downstream CPM target to notify on release.
- **No `:latest` tag.** This is a platform composition artifact, not a consumer image. The only tag on the GHCR image is the explicit version. Ambiguity about what is deployed is not acceptable.
- **No auto-tag.** A phone-home PR landing on master does not trigger a release. Accumulation is the design; the human decides when the pile is ready.

### 2.3 PR gate

Yggdrasil's `ci.yml` is a thin caller of `ci-build-test.yml@master` — identical in shape to every other realm's PR gate. Currently passes trivially on the bare shell; gates real substance once `Norse.Hosting.*` projects land. No special handling for the bare-shell phase: a repo with no projects produces a trivially-passing build, and that is the correct outcome for the phone-home smoke test (§2.9 of the CPM spec).

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

No `minimum_coverage` input until Yggdrasil has tests. When it does, the threshold is set here, same as Svartalfheim.

### 2.4 Release ceremony — job graph

```
tag vX.Y.Z
  │
  ├── [ci]     — ci-build-test.yml (build + test against the tagged commit)
  │                                                                           ┐
  └── [codeql] — CodeQL SAST scan                                            ├── [package] ── [deploy-hook]
                                                                              ┘
```

`ci` and `codeql` fire in parallel. Each job does its own checkout; the double build cost is accepted in exchange for a faster wall-clock ceremony. Yggdrasil's build will grow heavier than Svartalfheim's once hosting projects land — the parallel shape pays off there.

`package` gates on both `ci` and `codeql`. It:

1. Builds three container images — `hosting/migrations`, `hosting/web`, `hosting/worker` — to the local Docker daemon via `dotnet publish /t:PublishContainer`. Images never reach GHCR until Trivy passes.
2. Runs Trivy (`aquasecurity/trivy-action`) against each built image — OS-layer CVEs and language-package vulnerabilities in one pass. Fails the job on HIGH or CRITICAL findings; nothing lands in the registry from a vulnerable image.
3. Generates a CycloneDX SBOM via Trivy for each image and attaches all three to the GitHub Release.
4. Pushes all three images to GHCR. The version is the bare tag with the `v` prefix stripped (`v0.1.0` → `0.1.0`), consistent with the MinVer convention used by NuGet realms.

`deploy-hook` gates on `package`. Today it is a named no-op:

```yaml
deploy-hook:
  needs: [package]
  runs-on: ubuntu-latest
  steps:
    - name: Deploy to dev/integration
      run: echo "Deploy hook — not yet implemented"
```

Its presence in the ceremony now means the deploy trigger has a known address in the workflow graph before it has a body. When the cloud environment design lands, this job receives a real implementation without altering the ceremony shape. The job name is the contract; the steps are the implementation detail.

### 2.5 `release-container.yml` in `.github`

This workflow lives in `NorseArchitecture/.github` alongside `release-nuget.yml`. Yggdrasil's own `release.yml` is a thin caller:

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

`pull-requests: write` is required even though Yggdrasil has no phone-home job. `release-container.yml` chains to `ci-build-test.yml`, which declares `pull-requests: write` on its `build` job (for the coverage comment). GitHub enforces the full permission chain at validation time: every caller in a `workflow_call` stack must grant every permission that any descendant job declares, regardless of whether the guarded step actually runs on the event. The permission must flow from `release.yml` → `release-container.yml` → `ci-build-test.yml` or the workflow is rejected at parse time.

The full ceremony logic lives in `.github`. A future container-shipping realm (a MAUI chassis, a standalone WASM host, anything that becomes its own repo) gets the identical ceremony with a five-line thin caller. "This one is different" never enters the institutional vocabulary.

### 2.6 Container identity

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

### 2.7 Pre-release tags

Pre-release tags (`v0.1.0-beta.1`, any tag containing `-`) follow the identical ceremony. The container is built, scanned, and pushed to GHCR with the bare pre-release version as the image tag (`0.1.0-beta.1`). The cloud-readiness gate still applies — a human typed the tag. No behavioral difference in the pipeline; the pre-release/stable distinction is a convention for what the tag communicates, not a workflow branch.

**Deploy routing (decided now, implemented with the deploy hook):**

- **Pre-release tag** → deploy to a feature environment. Allows teams to validate a specific in-progress composition in isolation before it is considered stable.
- **Stable tag** → deploy to the default deployment target (dev/integration environment). The image sits there until a human runs whatever tests they deem necessary, then promotes it via the deployment tool of record (Octopus Deploy, Azure DevOps Pipeline, or equivalent). The platform does not automate promotion beyond the default target — that gate is the human's to open.

The routing condition (`contains(github.ref_name, '-')`) mirrors the pre-release guard already used in `phone-home-nuget.yml` (§2.2 of the CPM spec), keeping the convention consistent across the automation layer. Implementation is folded into consequence 5.

### 2.8 Failure behavior

| Scenario | Outcome |
|---|---|
| CI fails | `package` and `deploy-hook` never run; nothing pushed to GHCR |
| CodeQL fails | Same — `package` gates on both `ci` and `codeql` |
| Trivy finds HIGH/CRITICAL | `package` job fails; GHCR push never executes; GitHub Release not created |
| GHCR push fails | `deploy-hook` never runs |
| `deploy-hook` fails (future) | Image is already in GHCR; the deploy failure is visible and actionable without rolling back the artifact |

## 3. Alternatives Rejected

- **Inline ceremony in Yggdrasil's `release.yml`.** Simpler today; wrong tomorrow. If a MAUI chassis or any other deployable composition becomes its own realm, it starts from scratch instead of from a five-line caller. Consistency prevents the "this one is different" institutional debt.

- **Auto-tag on phone-home PR merge.** Would fire a container build on every realm release that reaches master. Four or five teams releasing in concert would produce four or five container images before the composition is considered cloud-ready. The human gate exists precisely to prevent this churn.

- **`:latest` tag.** `:latest` is ambiguous by design — it always points to the most recent push, which changes without notice. A platform that refuses silent ambiguity everywhere else does not accept it in image references.

- **Grype + Syft for scanning and SBOM.** Two tools doing what Trivy handles in one pass. Trivy has better native GitHub Actions support (`aquasecurity/trivy-action`) and produces CycloneDX SBOMs natively. No meaningful capability gap justifies the added surface area.

## 4. Consequences

1. Add `release-container.yml` to `NorseArchitecture/.github/.github/workflows/`. **Pending.**
2. Add Yggdrasil's thin `ci.yml` and `release.yml` callers. **Pending.**
3. Run `carve-the-laws.ps1` against Yggdrasil to apply branch protection and the "Law of the Æsir" ruleset. **Pending.**
4. Confirm `SCATTER_PAT` is not needed on Yggdrasil (no phone-home job); only `GITHUB_TOKEN` with `packages: write` is required for the GHCR push. **Confirmed by design.**
5. When the cloud environment story is designed, implement `deploy-hook` in `release-container.yml`. Job name and position in the graph are fixed; only the steps change. Apply the routing decided in §2.7: pre-release tags deploy to a feature environment; stable tags deploy to the default target and wait for human promotion via the deployment tool of record. **Future — implement together.**
6. Set `minimum_coverage` in Yggdrasil's `ci.yml` when the first tests land. **Future.**

## 5. Future Design — Out of Scope Here

**Migrations as init container.** When Yggdrasil's hosted services are designed and deployed, the migrations service (`Norse.Hosting.Migrations.Service`) runs as a Kubernetes init container ahead of `Norse.Hosting.Web.Server` and `Norse.Hosting.Worker`. This ensures the schema is current before any request-handling or message-consuming process starts. It is a runtime topology decision — it affects how the container image is orchestrated in Kubernetes manifests, not how it is built or pushed. The GHCR ceremony (this spec) is complete and correct regardless of how the container is eventually run.

Design of the init-container topology belongs in the Yggdrasil hosting spec when that increment opens.
