# MSBuild Deep Dive — Docket

**Status:** Docket only — filed 2026-08-06. A future full-tilt exercise in its own right: brainstorm → spec → plan → the works, in a dedicated session, not squeezed into another realm's task. Nothing below is a design; this page exists so the intent and the accumulated inventory survive until the project starts.

**Why it rates a project:** the MSBuild layer is one of the things that genuinely sets this stack apart from its .NET peers. Most platforms treat MSBuild as ambient ceremony; this one has quietly accreted a real architecture in it — and that architecture deserves the same deliberate treatment (consolidated doctrine, updated documents, closed gaps) the runtime side gets.

## What the deep dive consolidates (inventory as of filing)

- **Brand injection** — `Norse.$(MSBuildProjectName)` as `AssemblyName`/`RootNamespace` from each realm's root `Directory.Build.props`; brand-free project files; fork-by-one-edit.
- **`NorseRef` and the two crossings** — one item, two structurally different resolutions on `$(UseProjectReferences)`; doctrine and postmortem at `the-two-crossings.md`.
- **Realm-root targets chaining** — `GetPathOfFileAbove` up to Bifröst (workspace) or nowhere (standalone); the realm-root `Directory.Build.targets` as "law that binds src, tests, and gen exactly once" (minted 2026-08-03).
- **Evaluation-order law** — props vs targets vs `Directory.Packages.props` timing: the CPM/NU1008 lesson, the `Using Remove` at-targets-time lesson (both proven live 2026-08-03).
- **Analyzer forwarding** — the packaged `analyzers/dotnet/cs` asset propagates through the reference closure; a `ProjectReference` with `OutputItemType="Analyzer"` does not. Bitten twice now: the Primitives.Analyzers dev-mode gap (2026-08-04 postmortem, fixed in Bifröst's root targets) and NORSE080 invisible to Midgard's own consumers (found in Midgard PR #61 review, 2026-08-06, fixed in Midgard's realm-root targets).
- **Open item, parked for this project:** cross-realm *workspace-mode* forwarding of realm-owned analyzers (e.g. Yggdrasil consuming Midgard by `NorseRef` never sees NORSE080 locally; NuGet mode is covered by packaging). Fix shape is a Bifröst-root block coordinated with realm-root blocks so nothing double-attaches — needs a ruling on where that coordination law lives.
- **Packaging targets** — analyzers bundled into their library's package (`IncludeGeneratorInPackage` / `_GetPackageFiles`), generator forwarding (`Generator="true"`, `NorseGeneratorRef`), the duplicate-generator strip target living in Ginnungagap's scattered templates by design.
- **The rest of the estate** — config scatter (Ginnungagap `scatter-the-runes.ps1`/`manifest.psd1`), `Microsoft.Build.Sql` schema projects, `.slnx` everywhere, Glitnir's own `Norse.Docs.msbuildproj`, warnings-ratcheted-to-errors, MTP/coverage build quirks (`18.*` only, no `.runsettings`).

## Deliverable expectation

After the exercise, the documents get updated — this is explicitly part of the ask, not an afterthought. Candidates: `the-two-crossings.md` (extend or absorb into the consolidated doctrine), `conventions.md`, realm `CLAUDE.md` files where build law is described piecemeal, and the public README narrative — the MSBuild story should be tellable as a differentiator, at both altitudes, per the boy-scout law on README/CLAUDE.md pairs.
