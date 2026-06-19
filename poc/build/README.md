# Build Enforcement POC

Self-contained replica proving the MSBuild enforcement law before it seeds the
real Glitnir root. Spec: `docs/Platform/specs/2026-06-05-build-enforcement-design.md`.

## Layout

- `Directory.Build.props` — replica of the future Glitnir-root law
- `src/`, `tests/`, `benchmarks/` — governed layers, chained to the law
- `poc/`, `tests/smoke/` — severed floors (standalone props, no chain)
- `src/Glitnir.Probe/` etc. — probe projects; violations live behind `#if CANARY`
- `Verify-Enforcement.ps1` — the harness; exit 0 = law verified

## Run

    pwsh ./Verify-Enforcement.ps1

## Verdict

See `FINDINGS.md`.

## VS Test Drive (Phase 2 acceptance — muscle memory edition)

Open `Glitnir.BuildLaw.slnx` in Visual Studio (SDK resolves from the repo root `global.json`). Record the VS version — it becomes the pinned workstation floor.

1. **Razor format is a no-op (the old wound, ruled closed):** open `FormatProbe.razor` → Ctrl+A, Ctrl+K,D → zero changes (razor is a declared spaces-4 exception — ruling #13; the VS formatter is editorconfig-blind upstream, dotnet/razor #4406, so the law matches its default). Add a tab-indented line inside `@code` → Ctrl+K,D → it converts to spaces: that's the law self-applying, not a failure.
2. **C# format is a no-op on lawful code:** open `LawfulCitizen.cs` → Ctrl+K,D → zero changes.
3. **Live law:** in `LawfulCitizen.cs`, type `private` in front of a bare member → IDE0040 error squiggle (red, not suggestion). Undo.
4. **The slop ratchet:** add `using System.Text;` at the top → grayed immediately; build → IDE0005 *error*. Undo.
5. **The var law, live:** type `var c = new LawfulCitizen();` → IDE0008 error; rewrite `LawfulCitizen c = new LawfulCitizen();` → IDE0090 error; `LawfulCitizen c = new();` → clean. Undo.
6. **Naming law:** add field `int m_test;` → IDE1006 error squiggle. Undo.
7. **Judgment tier stays quiet:** open `JudgmentCitizen.cs` → the if/else return shows at most a faint suggestion, no squiggle, and never appears in Error List as error/warning.
8. **Severed contrast:** open `poc/Glitnir.Probe.Severed/Outlaw.cs` — IDE may show law squiggles (editorconfig reaches the editor) but the project still BUILDS clean: severance lives in the props, not the editorconfig. Expected, by design.
9. **The gate:** `pwsh ./Verify-Enforcement.ps1` → exit 0.
