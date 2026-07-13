# Release Ceremony: Tag Inception with Fan-In Release Creation

**Date:** 2026-07-13
**Status:** Approved in session 2026-07-13.
**Owner:** Buvy
**Companion specs:** `2026-06-19-ci-release-pipeline-design.md` (the pipeline this design amends — §2.5's "tag is the version" ruling is preserved, not revisited; §2.6/§2.7's per-target ceremony steps are restructured, not replaced); `2026-06-26-phone-home-nuget-cpm-design.md` (`sound-gjallarhorn`'s existing responsibility, retargeted here); `2026-07-12-designsystem-stories-hosting-design.md` (Naglfar's dual npm+NuGet publish, the concrete case this design exists to serve).

---

## 1. Context

The 2026-06-19 pipeline creates the GitHub Release as the last step of whichever job pushes the package — `pack-and-publish` in `release-nuget.yml`/`release-npm.yml`, `package` in `release-container.yml`. This has two problems in practice:

1. **Naglfar publishes to both npm and NuGet from one tag.** Two independent jobs (`release-npm`, `release-nuget`) each end with their own `gh release create`. Today this is undefined/racy in practice — whichever job finishes second either fails (release already exists) or silently overwrites the first's attempt. There is no single point that legitimately owns "create the release for this tag."
2. **Release notes are always `--generate-notes`.** GitHub's commit-message-derived notes are, per 18 months of using this pipeline, consistently something Buvy has to go clean up after the fact. There's no room to curate them as part of the ceremony.

A related, adjacent gap surfaced during design: `release-npm.yml` has never had a CodeQL step. Only the NuGet ceremony gates on it. This design closes that gap as a consequence of unifying the shape, not as separately scoped work.

Three trigger models were considered — detailed in §3. **Tag-as-inception** was chosen: the tag remains the sole, human-typed act that starts the ceremony (preserving 2026-06-19 §2.5's invariant), and the "who creates the release" race is resolved by making release creation a single downstream job that fans in from however many publish jobs a given realm runs, rather than a step embedded in each of them.

## 2. Rulings

### 2.1 Job graph shape (uniform across nuget, npm, and container realms)

```
tag push (v*.*.*)
  ├─ build-test        (ci-build-test.yml, unchanged — pack/build + test + coverage)
  ├─ codeql             (NEW reusable codeql.yml; runs parallel to build-test, same as today)
  │
  ├─ publish-nuget     ─┐
  ├─ publish-npm        ─┤  needs: [build-test, codeql] — realm includes only the target(s) it ships
  ├─ publish-container ─┘
  │
  └─ create-release     (NEW reusable; needs: whichever publish-* jobs that realm's release.yml lists)
```

Every realm's own `.github/workflows/release.yml` — the thin per-realm caller — declares which publish jobs it runs and lists them in `create-release`'s `needs:`. `create-release.yml` itself is agnostic to how many publish jobs ran; it downloads every artifact bundle uploaded under a shared naming convention and attaches all of them in one `gh release create`. This is what makes a single-target realm and Naglfar's dual-target realm the same shape at the fan-in layer — the only difference is how many jobs are listed in `needs:` and how many artifact bundles exist to download.

### 2.2 Publish jobs become publish-only

`release-nuget.yml`, `release-npm.yml`, and `release-container.yml` are renamed to `publish-nuget.yml`, `publish-npm.yml`, `publish-container.yml` and lose their trailing `gh release create` step. Each keeps its existing pack/push/SBOM (or, for container, build/Trivy-scan/push-per-image) logic unchanged, and instead ends with `actions/upload-artifact` under a distinct name (`nuget-artifacts`, `npm-artifacts`, `container-artifacts`) bundling whatever that target produces — nupkg, tgz, CycloneDX SBOM(s), or the four per-image Trivy SBOMs for container.

None of these reusable workflows call `ci-build-test.yml` or run their own CodeQL step anymore — both move up a level and are shared across whichever publish jobs a realm's caller runs (§2.1), rather than duplicated per target the way `release-nuget.yml` and `release-container.yml` each ran their own CodeQL today.

### 2.3 CodeQL becomes a shared, language-parameterized reusable workflow

`codeql.yml` (new) takes a `language` input. Realms invoke it once per language surface they actually have:

- NuGet-only and container realms: one `codeql(language: csharp)` job.
- Naglfar: two jobs, `codeql(language: csharp)` and `codeql(language: javascript-typescript)` — it has both the generated `DesignSystem.Tokens` package and the Style Dictionary token pipeline.

CodeQL scans the repository, not a specific artifact, so it is not partitioned by publish target: every publish job in a realm's ceremony waits on *every* CodeQL job that realm runs, regardless of which language that publish job's own artifact is written in. This closes the standing gap where `release-npm.yml` never ran CodeQL at all.

CodeQL continues to run only as part of the release ceremony, never on the PR gate (`ci-build-test.yml` stays build/pack + test + coverage only) — unchanged from today, restated here because it's load-bearing for why this design doesn't touch the PR gate at all.

### 2.4 `create-release.yml`: fan-in, curated-by-default notes

New reusable workflow. Downloads every artifact bundle matching the shared naming convention (`*-artifacts`, merged into one directory), then:

```
gh release create "$TAG" ./downloaded/* --generate-notes
```

`--generate-notes` stays as the default body — there is always something present, never a blank release — and Buvy edits it (`gh release edit` or the UI) after the fact when he wants to curate it. This is a habit, not a pipeline gate: nothing blocks on the notes being edited, and nothing re-triggers if they are.

**Retry/cleanup, unchanged from the tag-as-inception model:** if `build-test`, `codeql`, or any publish job fails, `create-release` never runs and no release exists. Cleanup is deleting the tag, both locally and on origin, and re-tagging. This was the deciding factor over release-as-inception (§3) — that model's cleanup requires deleting the release object *and* both tag copies.

**Known, unchanged risk:** if a realm publishes to two targets and one succeeds while the other fails (e.g. Naglfar's npm push succeeds, nuget push fails), the package is live on the target that succeeded but no GitHub Release is created (create-release needs both). This is not a new regression — the exact same partial-publish risk already exists in the current pipeline before this redesign — so it is noted, not solved, here.

### 2.5 Downstream jobs retarget to the specific publish job they actually depend on

Two jobs previously depended on the old monolithic "publish-and-release" job; both move to depend on the specific publish job whose output they actually need, not on `create-release`:

- **`sound-gjallarhorn`** (bumps Yggdrasil's `Directory.Build.props` CPM pin): `needs: [publish-nuget]`, everywhere it runs (including Naglfar). It only needs the package to be live on GitHub Packages — it has no dependency on the release object existing.
- **Yggdrasil's `deploy-hook`** (currently `needs: [package]`, the old monolithic container job): retargets to `needs: [publish-container]`. Deployment cares that the images are live in GHCR, not that release notes exist.

Container realms never gain a `sound-gjallarhorn` job — Yggdrasil doesn't publish a package another realm's CPM file pins.

**Explicitly out of scope, banked for later:** Yggdrasil is expected to eventually consume the published `@norsearchitecture/designsystem-tokens` npm package (built by Naglfar's Style Dictionary pipeline, not run by Yggdrasil itself) to source CSS into `Hosting.Stories.Client`/`Hosting.Web.Server`'s `wwwroot`. That would add a second, npm-flavored `sound-gjallarhorn` variant (bumping `package.json`/lockfile instead of `Directory.Build.props`) needing `publish-npm`. Nothing consumes that package today, so there is nothing to wire up yet — this is a forward reference, not a task this design creates.

## 3. Alternatives Rejected

- **Release-as-inception** (creating the GitHub Release first, with curated notes, and having *that* event trigger CI/CodeQL/publish). Rejected: inverts the 2026-06-19 §2.5 invariant that the human-typed `git tag` is the sole audit trail for a version — a release-first flow lets the tag ride along as a side effect of the release form instead of being the deliberate act. It also means the release object exists before any artifact is built, so the SBOM/nupkg/tgz manifest can't be attached at creation time and needs a bolt-on "attach after the fact" step. Its only real advantage — drafting notes before the pipeline runs — isn't worth either cost, since editing notes on an already-published release is no harder than drafting them first.
- **Hybrid tag → green → draft release → manual publish.** Rejected outright as a second inception point. Buvy was explicit: one trigger, not two steps to get artifacts live.
- **Each publish job creates its own release, tolerating the race.** This is the status quo for Naglfar and is exactly the bug this design fixes — whichever job finishes second either errors on an existing release or silently clobbers the first one's notes.
- **Partitioning CodeQL by publish target** (e.g. only `publish-npm` waits on the JS scan). Rejected: CodeQL scans the whole repository regardless of which target a given publish job ships, so a partial gate would create a false sense of per-target isolation that doesn't reflect what the scan actually covers.

## 4. Consequences

1. **Ginnungagap (`.github`):** add `codeql.yml` and `create-release.yml`; rename and trim `release-nuget.yml` → `publish-nuget.yml`, `release-npm.yml` → `publish-npm.yml`, `release-container.yml` → `publish-container.yml` to publish-only (drop `gh release create`, add `upload-artifact`).
2. **`config/.github/workflows/release.yml`** (canonical template, scattered to every default NuGet realm): restructure to the §2.1 shape — `build-test` + `codeql(csharp)` + `publish-nuget` + `create-release`, `sound-gjallarhorn` retargeted to `needs: [publish-nuget]`.
3. **Naglfar's bespoke `release.yml`** (not scattered — see `config/manifest.psd1`'s `release` group comment): two `codeql` jobs (csharp, javascript-typescript), `publish-nuget` + `publish-npm` in parallel, `create-release` needing both, `sound-gjallarhorn` needing `publish-nuget`.
4. **Yggdrasil's bespoke `release.yml`:** `codeql(csharp)`, `publish-container` (renamed from the `package` job, Trivy scans unchanged), `create-release`, `deploy-hook` retargeted to `needs: [publish-container]`.
5. **Side effect:** `release-npm.yml`'s missing CodeQL coverage is closed — every publish target on every realm is now gated by a CodeQL run against its actual language surface before anything ships.
6. **Banked, not built:** an npm-flavored `sound-gjallarhorn` variant for the day Yggdrasil consumes `@norsearchitecture/designsystem-tokens` (§2.5) — no code or workflow change happens for this until that consumption is itself designed.
