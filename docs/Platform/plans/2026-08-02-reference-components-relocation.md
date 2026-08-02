# Reference Components Relocation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (the platform default — superpowers:executing-plans is the narrow fallback for a separate-session review checkpoint, never an interchangeable alternative) to implement this plan task-by-task, paired with superpowers:test-driven-development on every task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `CountryLookup.razor` moves out of Yggdrasil into Mímir's first components RCL, the Blazor template debris dies, and Bragi pulls in Mímir exactly like Heimdall.

**Architecture:** New `Mimir/src/Reference.Components.FluentUI` (Sdk.Razor RCL, mirroring Heimdall's `AuthN.Components.FluentUI`; the headless `Reference.Components` sibling is deferred until a genuinely headless component exists — YAGNI, ruled 2026-08-02). Yggdrasil keeps only bootstrap razor and learns the new assembly at the router. Bragi gains the NorseRef + a `Reference/` story category; the story host registers a `FakeReferenceService` beside its existing `FakeAuthenticationService`. Design authority: `../specs/2026-07-11-blazor-component-architecture-design.md` + the 2026-08-02 session ruling ("no razor in Yggdrasil other than bootstrap"; "Bragi pulls in Mimir just like Heimdall"). No new spec — Heimdall is the prior art and the spec.

**Sequencing:** runs AFTER (a) Yggdrasil's `feature/reference-data-inversion` PR merges and (b) the well-seam plan completes (both touch `Program.cs`/CPM; this plan stays out of their way). Ship order inside the plan: Mímir → gate → Bragi + Yggdrasil.

**Tech Stack:** Blazor RCL (Microsoft.NET.Sdk.Razor), FluentUI Blazor v5, BlazingStory, xUnit v3/MTP.

## Global Constraints

- IMMUTABLE: every `Directory.Build.props`/`.targets`, `.editorconfig`, `nuget.config`, `global.json`. Halt-and-ask if a step seems to need one.
- Git: per-realm local branch `feature/reference-components-relocation` off `master`; subagents commit locally, never push, never master, never Bifröst/Glitnir commits (staged only). `git branch --show-current` before every commit.
- Route law: `/reference/country-lookup` keeps its OG route — renaming waits for the curation pass.
- House style: tabs; sealed by default; bare sentence_shaped test methods; IDE0005 deletions; one PropertyGroup + one ItemGroup per csproj, alphabetical; US English.
- Verification per realm from Bifröst root: `dotnet test <Realm>/<Realm>.slnx`, zero warnings. Docker-dependent suites defer to CI if the environment lacks Docker.
- Use `env -C <dir>`/absolute paths, never `cd x && ...` chains.

---

### Task 1: Mímir — `Reference.Components.FluentUI` RCL, first tenant `CountryLookup`

**Files:**
- Create: `Mimir/src/Reference.Components.FluentUI/Reference.Components.FluentUI.csproj`, `_Imports.razor`, `Pages/CountryLookup.razor` (moved from `Yggdrasil/src/Hosting.Web.Components/Pages/CountryLookup.razor` — copy verbatim; the Yggdrasil deletion is Task 3's)
- Create: `Mimir/tests/Reference.Components.FluentUI.Tests/` only if Heimdall's `AuthN.Components.FluentUI` has a test project — mirror its existence and shape exactly (check `Heimdall/tests/` first; one-test-project-per-package law follows precedent here)
- Modify: `Mimir/Mimir.slnx`, `Mimir/README.md`, `Mimir/CLAUDE.md` (realm gains its Components family — the charter's `Norse.Reference.Components` finally has a tenant)

**Interfaces:**
- Consumes: `IReferenceService`/`CountryRequest`/`CountryResponse` (sibling `Reference.Contracts`), `Iso3166.Ids`/`IsoCountryCodes` (Mimisbrunnr `Reference.Data.Primitives`, transitive via Contracts), `AsyncComponentBase` (Asgard components primitives — resolve the exact assembly from the component's existing `@inherits` + Yggdrasil's `Hosting.Web.Components` csproj/_Imports; add the matching NorseRef).
- Produces: package `Norse.Reference.Components.FluentUI`; routed page `/reference/country-lookup`; type `CountryLookup` (the router-registration marker Task 3 uses).

- [ ] **Step 1: Branch** (`git switch -c feature/reference-components-relocation master` in `Mimir/`).
- [ ] **Step 2: csproj** — mirror Heimdall's shape (no headless sibling to reference; Contracts takes that slot):

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
	<PropertyGroup>
		<Description>Norse.Reference.Components.FluentUI: FluentUI Blazor v5 reference-data components — CountryLookup and the set that grows beside it — wired to inject IReferenceService directly (transport-dumb; each host registers its own implementation via DI). First tenant of the realm's charted Components family; a headless Reference.Components sibling lands the day a component with no FluentUI markup exists.</Description>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Microsoft.FluentUI.AspNetCore.Components" Version="5.*-*" />
		<ProjectReference Include="../Reference.Contracts/Reference.Contracts.csproj" />
	</ItemGroup>
</Project>
```

plus the Asgard components NorseRef discovered in Step 3 (alphabetical placement).
- [ ] **Step 3: Move the page.** Copy `CountryLookup.razor` verbatim into `Pages/`; create `_Imports.razor` carrying exactly the usings the page needs (crib from `Yggdrasil/src/Hosting.Web.Components/_Imports.razor`, pruned to what this page uses — the compiler is the judge; IDE0005-equivalent unused `@using`s are removed). Build to discover the `AsyncComponentBase` home; add that NorseRef.
- [ ] **Step 4: Tests per precedent** — if Heimdall has a components test project, mirror it with one bUnit-equivalent render test proving the page renders its input controls without a registered `IReferenceService` interaction; if Heimdall has none, skip (note the precedent in the report).
- [ ] **Step 5: slnx + realm docs.** Add the project (and test project if created) to `Mimir.slnx`; README/CLAUDE.md rows gain the Components family sentence.
- [ ] **Step 6: `dotnet test Mimir/Mimir.slnx` — zero warnings, green. Commit.**

> **SHIP GATE (human):** Mímir PR → CI → merge → tag (expected `v0.0.3`) → publish.

---

### Task 2: Bragi — pulls in Mímir just like Heimdall

**Files:**
- Modify: `Bragi/src/DesignSystem.Stories/DesignSystem.Stories.csproj` (NorseRef), `Bragi/README.md`/`CLAUDE.md` if they enumerate story categories
- Create: `Bragi/src/DesignSystem.Stories/Reference/CountryLookup.stories.razor`

**Interfaces:**
- Consumes: `Norse.Reference.Components.FluentUI` (Task 1's package).
- Produces: the `Reference/CountryLookup` story in the BlazingStory catalog.

- [ ] **Step 1: Branch. Step 2: csproj** — add beside the Heimdall NorseRef (alphabetical):

```xml
		<NorseRef Include="Reference.Components.FluentUI">
			<Repo>Mimir</Repo>
		</NorseRef>
```

- [ ] **Step 3: Story** — the Login.stories.razor shape verbatim:

```razor
@attribute [Stories("Reference/CountryLookup")]
<Stories TComponent="CountryLookup">
    <Story Name="Default">
        <Template>
            <CountryLookup @attributes="context.Args" />
        </Template>
    </Story>
</Stories>
```

(Story files follow BlazingStory's own 4-space/space-indent conventions already in the folder — match the sibling files, not the platform tab rule; they are the one sanctioned exception already present in this repo.)
- [ ] **Step 4: Build Bragi green (dev mode resolves the Mimir project ref). Commit.**

> **SHIP GATE (human):** Bragi PR → CI (needs Mímir `v0.0.3` published) → merge → tag → publish.

---

### Task 3: Yggdrasil — bootstrap-only razor, debris to the dustbin

**Files:**
- Delete: `Yggdrasil/src/Hosting.Web.Components/Pages/CountryLookup.razor`, `Pages/Counter.razor`, `Pages/Weather.razor`
- Modify: `Yggdrasil/src/Hosting.Web.Components/Layout/NavMenu.razor` (or wherever links to `/counter`, `/weather`, `/reference/country-lookup` live — grep for the routes), `Yggdrasil/src/Hosting.Web.Server/Program.cs` (router assemblies), `Yggdrasil/Directory.Packages.props` (add `Norse.Reference.Components.FluentUI` pin at the Mímir version; bump `MimirVersion`), `Yggdrasil/src/Hosting.Web.Client/Hosting.Web.Client.csproj` or `Hosting.Web.Components` csproj — whichever carries the WASM component refs today gains the NorseRef `Reference.Components.FluentUI`/`Mimir` (follow where `Reference.Contracts` is consumed now)
- Create: `Yggdrasil/src/Hosting.Stories.Client/FakeReferenceService.cs`; Modify: `Hosting.Stories.Client/Program.cs` (register it)

**Interfaces:**
- Consumes: `CountryLookup` type (Task 1) as the router marker; `IReferenceService` for the fake.
- Produces: Yggdrasil hosts the page from the package; `/counter` + `/weather` cease to exist; story host serves the Reference category live.

- [ ] **Step 1: Branch (verify clean master first — this realm carries parallel work; BLOCKED if not).**
- [ ] **Step 2: Deletions + nav.** `git rm` the three pages; remove their nav links (grep `Hosting.Web.Components` for `counter`, `weather`, `country-lookup` route strings — update every hit; the CountryLookup nav link, if any, keeps working via the package page and stays).
- [ ] **Step 3: Router + refs.** Add the NorseRef + CPM pin; in `Program.cs`, extend `RoutesAdditionalAssemblies` and `.AddAdditionalAssemblies(...)` with `typeof(CountryLookup).Assembly` (hoist `using Norse.Reference.Components.FluentUI;` — adjust to the page's real namespace).
- [ ] **Step 4: Story host fake** — mirror `FakeAuthenticationService` exactly (same file placement, same namespace, same registration style in `Hosting.Stories.Client/Program.cs`):

```csharp
using Norse.Abstractions.Contracts;
using Norse.Reference;

namespace Norse.Hosting.Stories.Client;

/// <summary>
/// Story-host-only stand-in for <see cref="IReferenceService"/> — never crosses a wire; answers
/// every lookup with the United States row, its Id read from the baked <see cref="Iso3166.Ids"/>
/// so the story doubles as a golden-value check of the generated surface.
/// </summary>
sealed class FakeReferenceService : IReferenceService
{
	public Task<Outcome<CountryResponse>> GetCountry(CountryRequest request, CancellationToken cancellationToken = default) =>
		Task.FromResult(/* success-construct exactly as FakeAuthenticationService does for its Outcome<T> returns */ BuildUs());

	static Outcome<CountryResponse> BuildUs() =>
		new CountryResponse
		{
			Id = Iso3166.Ids[IsoCountryCode.UnitedStatesOfAmerica],
			Alpha2 = "US",
			Alpha3 = "USA",
			Name = "United States of America",
		};
}
```

(The success-construction idiom — implicit conversion vs factory — is whatever `FakeAuthenticationService` already uses; match it exactly and collapse `BuildUs` inline if that reads cleaner at one call site.)
- [ ] **Step 5: `dotnet test Yggdrasil/Yggdrasil.slnx` — zero warnings; non-container tests green; run the stories host briefly (`dotnet run --project src/Hosting.Stories.Server`) if the environment allows, confirm the Reference/CountryLookup story renders. Commit.**

> **SHIP GATE (human):** Yggdrasil PR → CI → merge. Bifröst smoke: dashboard up, `/reference/country-lookup` served from the package, `/counter` 404s.

---

## Self-Review Notes

- Scope pins: no headless sibling (ruled), no route rename (OG-routes law), no Bragi story-host changes beyond the fake (the host pattern already exists), realm docs updated where the change lands (boy-scout).
- Type consistency: `CountryLookup` marker type used in Tasks 1 and 3; `FakeReferenceService : IReferenceService` matches the Contracts signature (`Task<Outcome<CountryResponse>> GetCountry(CountryRequest, CancellationToken)`).
- Deliberate deferrals: none of the seam plan's files are touched except `Program.cs`'s router lines and CPM adds — hence the run-after-seam sequencing rule up top.
