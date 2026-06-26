# Continuous Integration and Tag/Release Pipeline

**Date:** 2026-06-19
**Status:** Approved in session 2026-06-19.
**Owner:** Buvy
**Companion specs:** `2026-06-05-build-enforcement-design.md` (the warnings-as-errors and `NuGetAudit` baseline this pipeline runs, unchanged by this design — CI executes that law, it does not redefine it); `2026-06-11-repository-topology-design.md` (the shared-record-over-distributed-copies argument this design mirrors at the workflow-file level); `../../codenames.md` and root `CLAUDE.md` §2 (the realm table this scope matrix maps onto).

---

## 1. Context

No CI/CD exists anywhere in the platform today — verified at design time: no `.github/workflows` in any realm repo, no `nuget.config`, no prior Glitnir spec on the subject. Asgard is presently a bare shell (LICENSE only); this design is a deliberate prerequisite to writing any code there. The immediate driver: Asgard's `UseProjectReferences` MSBuild pipeline work cannot proceed to "absolute status" without a CI gate to prove it against, and every realm behind Asgard in the dependency chain inherits the same need. This spec settles the shape once, platform-wide, so each realm's own workflow file is a thin caller rather than a hand-maintained copy.

## 2. Rulings

### 2.1 Scope and repo matrix

| Repo | PR gate (build + test) | Tag/release ceremony | Publish target |
|---|---|---|---|
| Asgard, Svartalfheim, Midgard, Urdarbrunnr, Himinbjorg, Heimdall | Yes | Yes | GitHub Packages (NuGet) |
| Yggdrasil | Yes | Yes (container-shaped) | GHCR (container image) |
| Bifrost | Yes | No — nothing to publish | — |
| Glitnir | No CI | No | — |

Yggdrasil ships a runnable container image, not a NuGet package, so its release ceremony diverges at the publish step (and gains one extra scan — §2.7). Bifrost is the AppHost meta-repository; it composes the realms for local dev and produces no deployable artifact of its own, so it keeps the PR gate (proving the AppHost still compiles and runs) but has no release workflow at all. Glitnir is documents only.

### 2.2 Reusable workflow architecture

A new repository, **`NorseArchitecture/.github`**, hosts the actual pipeline logic as `workflow_call` reusable workflows:

- **`ci-build-test.yml`** — restore, build (warnings-as-errors, `NuGetAudit` per the build-enforcement law — this workflow executes that law, it does not duplicate or redefine it), test. Consumed by every repo with a PR gate (everything except Glitnir).
- **`release-nuget.yml`** — calls `ci-build-test.yml` as a job, so a release re-runs the *identical* build+test invocation against the tagged commit rather than a hand-maintained copy of it; then runs CodeQL, generates an SBOM, and pushes to GitHub Packages. Consumed by the six NuGet realms.
- **`release-container.yml`** — same shape as `release-nuget.yml`, plus a container image scan, pushing to GHCR instead of GitHub Packages. Consumed by Yggdrasil only.

Each realm's own `.github/workflows/*.yml` is a handful of lines: `uses: NorseArchitecture/.github/.github/workflows/ci-build-test.yml@master` (and the matching release caller, where applicable). A convention change to the build, test, or release shape lands once, in `.github`, and is in effect everywhere on the next run — never N pull requests across realms, never a missed repo silently left behind.

### 2.3 PR gate

Every PR against a CI-enabled repo triggers `ci-build-test.yml`:

1. Checkout the PR branch.
2. `actions/setup-dotnet`, fed `global-json-file` so the runner installs exactly the SDK each repo's `global.json` declares — including prerelease channels where `allowPrerelease: true` is set, as it is in Bifrost today (`11.0.100-`, `rollForward: latestFeature`).
3. Restore, then build with warnings-as-errors and `NuGetAudit` active — the existing build-enforcement law, simply executed in CI rather than only locally.
4. Run the full test suite via the SDK's test runner (`Microsoft.Testing.Platform`, per `global.json`).

Integration tests that hit real Postgres or RabbitMQ via testcontainers require no special CI plumbing — GitHub-hosted Linux runners ship Docker by default.

**Required status check context — empirically confirmed 2026-06-25.** GitHub Actions reports reusable workflow checks as `{caller job id} / {called job id}`. The caller workflow `name:` field and the trigger event suffix (`(pull_request)`) are UI display decorations only — they must not appear in the required status check context. For the NuGet realm PR gate, the caller job is named `gate` and the called job in `ci-build-test.yml` is `build`, so the context is `gate / build`. The source must be locked to `integration_id: 15368` (the GitHub Actions app, a platform constant) — without it, any integration that can report a status check can spoof the context name and satisfy the gate. Both values are encoded in `carve-the-laws.ps1`; run it against a realm to apply.

### 2.4 Merge to master

Nothing additional runs on merge. Branch protection requires a PR branch to be up to date with master before merge is permitted, so the tree landing on master is byte-for-byte what the PR gate already built and tested — a second build on merge would be redundant compute, not redundant insurance.

**Operational note — amended 2026-06-25:** the pipeline is live and proven on Svartalfheim. The admin-bypass exception in the "Law of the Aesir" ruleset is retained deliberately: `required_approving_review_count: 0` means Buvy would be self-approving his own PRs, which is theater; the bypass lets him push directly in genuine emergencies. Re-entry trigger: a second contributor joins — then flip to `bypass_mode: pull_request` and raise the review count to 1.

### 2.5 The tag is the version

A release is cut by a human pushing an annotated tag `vX.Y.Z` on master. That act — a person typing a specific version number — is the audit trail; nothing upstream of it is automated, and nothing infers a version independently of it. **MinVer** (added as a `PackageReference` in each NuGet realm's root `Directory.Build.props`, alongside the existing `AssemblyName`/`RootNamespace` injection) derives the package version *from* that tag at build time. There is exactly one place a version number is ever typed; the build cannot diverge from what the human committed to.

**Pre-release packages follow the identical rule, deliberately — ruled 2026-06-19.** A pre-release is a tag with a SemVer pre-release segment (`v0.1.0-beta.1`), typed by the same human, through the same `git tag` act, triggering the identical release ceremony. There is no second trigger (no auto-publish on feature-branch push, no commit-height-derived prerelease feed) — adding one would mean a package could exist on the feed that nobody deliberately decided to cut, breaking §2.5's absolute claim ("nothing infers a version independently of" the human-typed tag). The existing tag-glob trigger (`v*.*.*`) and MinVer already handle any valid SemVer string with no pipeline change; this is a policy ruling, not an implementation gap.

### 2.6 NuGet realm release ceremony

Triggered on tag push, `release-nuget.yml`:

1. Checks out master at the tag — a clean rebuild from source, never a reuse of any PR-gate build artifact.
2. Runs `ci-build-test.yml` as a job against the tagged commit.
3. Runs a CodeQL SAST scan.
4. Generates an SBOM (CycloneDX) and attaches it to the GitHub Release.
5. `dotnet pack`, versioned via MinVer from the tag, and pushes to GitHub Packages.

### 2.7 Yggdrasil release ceremony

Triggered identically, `release-container.yml` runs steps 1–4 above unchanged, then:

5. Builds the container image, tagged with the release version.
6. Runs Trivy or Grype against the built image for OS-package and base-image CVEs — the one ceremony step that exists only for Yggdrasil, since it is presently the sole realm shipping a runnable image with an OS layer beneath it.
7. Pushes the image to GHCR.

### 2.8 Auth and tooling

GitHub Packages and GHCR both live under the `NorseArchitecture` org, so the workflow-scoped `GITHUB_TOKEN` (with `packages: write` permission) covers both publish targets — no new PAT, no new secret to provision or rotate. CodeQL runs via `github/codeql-action`; the SBOM via `anchore/sbom-action`. Both are GitHub-native or marketplace actions, not new vendor accounts.

**`carve-the-laws.ps1` requires `pwsh` (PowerShell Core).** It is cross-platform (`#!/usr/bin/env pwsh`) and runs on Windows natively and on Linux/WSL via `snap install powershell --classic`. It is listed in the `.github` repo's `TOOLCHAIN.md`. It applies both repo settings (`delete_branch_on_merge: true`) and the "Law of the Aesir" ruleset in a single idempotent run — `./scripts/carve-the-laws.ps1 <Realm>` is the complete ceremony for any new realm.

## 3. Alternatives Rejected

- **Per-repo duplicated workflow YAML.** Rejected: every convention change becomes N pull requests, and drift between realms is the exact silent-incongruence failure mode the repository-topology spec rejects at the spec-distribution level (§2.4 of that spec) — the same argument applies one layer down, at the workflow-file level.
- **Nerdbank.GitVersioning or other computed-version schemes.** Rejected: the explicit requirement was a human-typed version number as the deliberate, auditable act — not a tool inferring one from commit height.
- **Manual `<Version>` in csproj/Directory.Build.props.** Rejected: a tag and a csproj version are two sources of truth that can silently drift — the platform forbids exactly this failure shape everywhere else (§2.6/§2.7 of Glitnir's root CLAUDE.md).
- **Rebuilding master on every merge.** Rejected: branch protection (up-to-date-before-merge) already guarantees the merged tree equals what the PR gate tested; a second build buys nothing.
- **Third-party security vendor (Snyk, etc.).** Rejected for now: a new account, secret, and recurring cost before any release can ship, when GitHub-native CodeQL covers the same need for private repos at no additional cost. Re-entry trigger: a concrete gap CodeQL/SBOM/Trivy demonstrably can't cover.

## 4. Consequences

1. Stand up the `NorseArchitecture/.github` repository carrying `ci-build-test.yml`, `release-nuget.yml`, `release-container.yml`. **Done 2026-06-25.**
2. Add MinVer to the `Directory.Build.props` of each NuGet realm (Asgard, Svartalfheim, Midgard, Urdarbrunnr, Himinbjorg, Heimdall). **Done for Svartalfheim 2026-06-25; remaining realms follow when they have buildable code.**
3. Add thin caller workflow files to every CI-enabled repo (all realms except Glitnir). **Done for Svartalfheim 2026-06-25; remaining realms follow.**
4. Once live and proven, remove Buvy's branch-protection admin-bypass exception (§2.4). **Amended — see §2.4; bypass retained deliberately for solo-maintainer mode.**
5. `carve-the-laws.ps1` applies `delete_branch_on_merge: true` as a repo setting alongside the ruleset — added 2026-06-25; run it against each realm when it receives its caller workflows.
6. **Unblocks:** the Asgard `UseProjectReferences` MSBuild pipeline plan, and the subsequent carry-through of the same discipline across the remaining realms — both were explicitly gated on this design landing first.
