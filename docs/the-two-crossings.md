# The Two Crossings

*Doctrine. Governs `NorseRef` — the one MSBuild item every cross-realm dependency on this
platform is declared with, in Asgard's `Abstractions.Backend` and in Yggdrasil's composition
root alike. One item, one name, and it resolves to two structurally different things depending
on a single property. Treating that as an implementation detail instead of a load-bearing
duality is how the platform's own tooling got it wrong the first time — see the postmortem
below. This page exists so the next person who touches `Directory.Build.targets` reasons about
both crossings, not just the one they're standing on.*

## The mechanism

Bifröst's root `Directory.Build.targets` carries one `Choose` block. Every realm's `src/`/`tests/`
`Directory.Build.props` declares its cross-realm dependencies as `NorseRef` items — `<NorseRef
Include="Reference.Data.Primitives"><Repo>Mimisbrunnr</Repo></NorseRef>` and the like — and the Choose block
decides, from a single property, what that item actually becomes at build time:

```xml
<When Condition="'$(UseProjectReferences)' == 'true'">
	<ProjectReference Include="@(NorseRef->'$(MSBuildThisFileDirectory)%(Repo)/src/%(Identity)/%(Identity).csproj')" />
	...
</When>
<When Condition="'$(ManagePackageVersionsCentrally)' == 'true'">
	<PackageReference Include="@(NorseRef->'Norse.%(Identity)')" />
	...
</When>
<Otherwise>
	<PackageReference Include="@(NorseRef->'Norse.%(Identity)')" Version="$(NorseRefVersion)" />
	...
</Otherwise>
```

Same item. Same declared intent — "this project depends on that one." Two genuinely different
mechanisms answer it, and which one answers is a single boolean the entire build reasons from.

## The polarity

| | `ProjectReference` — local dev | `PackageReference` — CI / package mode |
|---|---|---|
| **Faces** | The working tree. Every sibling realm's `src/` sitting on disk right now. | The published record. Whatever NuGet actually shipped, pinned by version. |
| **Resolves to** | The sibling's real source, compiled fresh, every build. | A specific artifact, frozen at pack time, identified by SemVer. |
| **Cost of a change** | Zero. Edit Svartálfheim, rebuild Mimisbrunnr, the new code is just *there* — no publish, no version bump, no wait. | A real ship gate: PR merged, CI green, tagged, `dotnet pack`, published. The dependency only moves when someone pays that price. |
| **What it guarantees** | Nothing about isolation. A realm can accidentally lean on a sibling's unshipped, half-finished internals and nobody will know until release. | Realm isolation, for real. You cannot depend on code that hasn't shipped — the package doesn't exist yet, so the reference fails loudly at restore. |
| **What it's for** | Fast iteration across a realm boundary — feature work and debugging that legitimately spans two repos, without ceremony standing in the way. | Proving the platform's actual claim: that realms are independently releasable, independently versioned, and safe to depend on without reading their source. |
| **Analyzer/generator forwarding** | MSBuild never forwards analyzers transitively through `ProjectReference` — a sibling's `gen/` generator only runs where it's explicitly wired. No accidental flow to clean up. | NuGet's package-analyzer propagation reaches transitively through the whole reference graph — a generator can arrive somewhere it was never meant to run, and something has to strip it back out (see the postmortem). |

Both branches answer the same question — "does this compile against that" — with opposite
honesty. `ProjectReference` is honest about *velocity*: nothing stands between an edit and its
consumer seeing it, which is exactly what makes cross-realm feature work and live debugging
tractable instead of a publish-and-pray loop. `PackageReference` is honest about *boundary*:
nothing crosses it that wasn't actually shipped, which is exactly what makes CI's realm
isolation a claim you can trust instead of one you have to hope holds.

## Why this is a platform doctrine and not a convenience

The value isn't either branch alone — plenty of monorepos give you fast local iteration, plenty
of polyrepos give you real package boundaries. The value is that **the same declaration means
both, and which one is live is one flag**, so the two modes never drift into two different
dependency graphs that happen to usually agree. There's exactly one `NorseRef` graph. It just
has two lenses.

That's what makes the triage story real, not aspirational: a CI failure that only reproduces
under real package resolution — a version actually pinned lower than what local `master`
currently has, a package that forwards something a `ProjectReference` never would — gets
reproduced locally by flipping `UseProjectReferences` to `false` and rebuilding. No waiting on a
fresh pipeline run to iterate on a hypothesis. No maintaining a second, hand-synced local repro
harness. The same graph, the other lens, on the same machine, in the time it takes to rebuild.

And the inverse holds too: a whole feature that spans Svartálfheim, Mímisbrunnr, and Yggdrasil
can be built and debugged as one continuous edit-compile-run loop across three repositories,
with the CI-facing guarantee — that none of it can silently depend on unshipped internals —
enforced the moment the same code runs in package mode, without anyone having to remember to
check by hand.

## The postmortem: Task 7, the well-and-wire slice (2026-07-31 → 2026-08-01)

The duality is not free. It was designed with one gap in it, and the gap surfaced for real —
this is that incident, in the crooked-path format, because a doctrine that only shows its good
side isn't earning the "peer to the two unions" claim.

- **Believed:** the generator-forwarding strip target (`_NorseRemoveUnwantedGeneratorAnalyzers`,
  scattered into every realm's `src/`/`tests/` `Directory.Build.targets`) fully accounted for
  every shape a `NorseRef`-wired generator could arrive in. Its allow-list — a project is only
  allowed to run a generator it explicitly declared via `NorseRef Generator="true"` — was
  believed complete.
- **Wrong because:** no allowance existed for a project consuming its *own* sibling `gen/`
  generator — the `src/X` + `gen/X.Generator` pairing, wired with a raw `<ProjectReference
  OutputItemType="Analyzer" ReferenceOutputAssembly="false">` rather than a cross-repo `NorseRef`.
  `NorseRef Generator="true"`'s allow-list has no way to represent *self*-reference — adding one
  would resolve to the project referencing itself, `MSB4006`, a circular target graph. The strip
  target's original condition matched on filename alone (`^Norse\..+\.Generator$`), with no
  regard for *how* the analyzer item arrived — package-forwarded and project-forwarded looked
  identical to it, and only one of those two crossings was ever the actual problem.
- **Surfaced when:** Mímir's `Reference.Contracts` (Task 7 of this plan) became the platform's
  first case where a project's own compilation genuinely depended on its own generator's output.
  Two earlier projects — Asgard's `Abstractions.Web.Server` and Urðarbrunnr's
  `Persistence.EntityFramework` — had the identical self-referencing `ProjectReference` shape
  sitting dormant the whole time; neither ever tripped the bug because neither had anything to
  generate against its *own* compilation yet. The gap was real and platform-wide from the moment
  the strip target shipped — it just had no live exerciser until this task.
- **Correction:** the strip condition gained one conjunct — `'%(Analyzer.NuGetPackageId)' != ''`
  — scoping it to NuGet-delivered analyzers only. The doctrine argument closes the loop: MSBuild
  never forwards analyzers transitively through `ProjectReference` (the polarity table above,
  right column), so a `ProjectReference`-wired analyzer — a project's own sibling `gen/`
  generator, or Bifröst's own dev-mode forwarding — was never a strip candidate in the first
  place; only the package crossing can leak a generator somewhere it wasn't meant to run.
  Provenance metadata makes the two crossings distinguishable at the item level: NuGet-delivered
  analyzers carry `NuGetPackageId`; `ProjectReference`-delivered analyzers carry
  `MSBuildSourceProjectFile`; SDK built-ins carry neither.
- **A second, unrelated-looking symptom dissolved under the same fix, not worked around:**
  re-adding the stripped analyzer as a standalone experiment failed for a reason that had
  nothing to do with the strip logic itself — the scattered `Directory.Build.targets` imports
  *after* `CustomAfterMicrosoftCommonTargets`, so an injected target redefinition silently loses,
  and an `AfterTargets` hook on a not-yet-defined target is silently ignored. No multi-pass
  compiler behavior was involved, despite early diagnostics suggesting otherwise. Verified with
  zero repo edits via `-p:DirectoryBuildTargetsPath` global-property injection — a probing
  technique worth keeping on hand for the next time a fix needs verifying without committing a
  hypothesis first.
- **Lesson:** the strip target's entire job is to reason correctly about which of the two
  crossings produced a given item — that was always its actual specification, whether or not it
  was written down that way at the time. A filename-only match was never enough, because
  filename can't tell you which lens built the graph. Full design record:
  `Platform/specs/2026-07-31-norseref-strip-provenance-scoping-design.md`; the original Gap 2
  design this amends: `Platform/specs/2026-07-01-norseref-generator-forwarding-design.md`.

## Consequences for designers

- **Never reason about a `NorseRef` consumer as if it only has one shape.** Anything that
  inspects, filters, or forwards an item derived from `NorseRef` — an analyzer, a build target, a
  future source generator reading project metadata — has to ask which crossing it's looking at,
  not just what the item is named.
- **A bug that's dormant in one crossing isn't fixed.** Asgard's and Urðarbrunnr's self-references
  carried the exact same defect as Mímir's for as long as the strip target existed; they were
  silent only because nothing exercised them yet. "No project has hit this" is not evidence a gap
  is closed — it's evidence nobody's found the first exerciser.
- **The `ProjectReference` branch is not the sandbox and the `PackageReference` branch is not the
  real one.** Both are real. Local dev optimizes for velocity across a boundary that CI optimizes
  for proving; neither lens is more "true" than the other, and a fix that only makes sense under
  one of them is incomplete.
- **When something behaves differently in CI than it does locally, ask which crossing changed
  first** — before suspecting the code. The flag exists precisely so that question has a fast,
  local answer.

> Two lenses, one graph, and the platform's own build law had to learn to ask which lens it was
> holding before it could tell a real leak from an honest self-reference.
