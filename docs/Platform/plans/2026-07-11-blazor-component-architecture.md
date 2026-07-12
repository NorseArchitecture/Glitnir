# Blazor Component Architecture (First Wave) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the shape-only slice of the Blazor component architecture spec — Naglfar's first .NET project (a BlazingStory host), Asgard's headless components/plugin-interface primitives, and the Heimdall `Norse.Access` → `Norse.AuthN` doc-sync rename — without touching Heimdall's or Mimir's own gRPC/behavioral work, which still awaits its own converged spec.

**Architecture:** Ginnungagap's manifest gains Naglfar's `.NET` groups so scatter can bootstrap its root config; Asgard's existing `Abstractions.Components` project gains two new things (a `Primitives/IDashboardWidget` interface and a flat, headless `Loader.razor`) with no new project; Naglfar's new `DesignSystem.Stories` project (via BlazingStory's `blazingstorywasm` template) takes a live `NorseRef` project-reference to `Abstractions.Components` and stories the `Loader` component; four documents get the Heimdall rename plus one stale "Chamber" row removed.

**Tech Stack:** .NET (net11.0 per current root `Directory.Build.props`), Razor Class Library (`Microsoft.NET.Sdk.Razor`), BlazingStory (`BlazingStory.ProjectTemplates` / `BlazingStory` preview package), xUnit v3 + Shouldly + bUnit, PowerShell (`manifest.psd1`).

## Global Constraints

- **FluentUI Blazor is the only component-library target platform-wide; no Blazorise anywhere in this plan.** (spec §1.1)
- **Asgard gets no new project.** `IDashboardWidget` lands in `Norse.Abstractions.Components.Primitives` (folder `Primitives/`); the headless `Loader.razor` lands flat at the `Abstractions.Components` project root. Neither may reference FluentUI Blazor, ASP.NET Core, EF Core, or any third-party design-system package. (spec §1.2, §2.1)
- **BlazingStory and any `.stories.razor` file live exclusively in Naglfar's `DesignSystem.Stories`.** No `.Components.FluentUI` project anywhere may reference BlazingStory or carry a story file. (spec §1.3)
- **Heimdall renames `Norse.Access` → `Norse.AuthN` everywhere in the documentation record**, with zero remaining `Norse.Access` reference to Heimdall. (spec §1.4, §4)
- **US English spelling** in code, identifiers, comments, docs, and commit copy.
- **`internal sealed` by default**; `omit_if_default` on accessibility modifiers everywhere.
- **Tabs, 4-space width** for C#; Razor uses 4-space per the platform `.editorconfig`.
- **No automatic git commits.** Every task ends with `git add` and a shown diff — the human commits. Do not run `git commit` under any circumstance in this plan.
- **`UseProjectReferences=true` is Bifrost's default** — every `NorseRef` item resolves to a live `ProjectReference` against the sibling submodule while working inside Bifrost; it only falls back to a `Norse.*` `PackageReference` in a standalone checkout. Don't hardcode a package version anywhere a `NorseRef` belongs.

---

## Task 1: Ginnungagap — extend Naglfar's manifest groups

**Files:**
- Modify: `/home/buvy/code/NorseArchitecture/.github/config/manifest.psd1`

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces: the group list `Get-RealmGroups` resolves for `Naglfar` — later tasks manually create the files this *would* scatter, so they must match this list exactly.

This is a PowerShell data file, not C# — there's no unit test runner for it in this repo. Verification is a manual `pwsh` sanity check instead of an xUnit test, mirroring how the seeding framework spec treated pure-config changes.

- [ ] **Step 1: Read the current Naglfar exception entry**

The current entry (`config/manifest.psd1`) reads:

```powershell
		# Design system — no .NET tooling; crafts its own .editorconfig. Ungated.
		Naglfar   = @{
			Groups = @('git', 'ci', 'workflows', 'claude')
			Gated  = $false
		}
```

- [ ] **Step 2: Edit the entry to add the .NET groups**

Replace it with:

```powershell
		# Design system — token pipeline (JS/Style Dictionary) + DesignSystem.Stories
		# (BlazingStory host, .NET, consumes Abstractions.Components et al. via NorseRef).
		# Ungated: little unit-testable logic lives in this repo directly — Asgard's
		# components are already gated in their own repo. Revisit if that changes.
		Naglfar   = @{
			Groups = @('universal', 'sdk', 'dotnet', 'nuget', 'tests', 'ci', 'workflows', 'claude')
			Gated  = $false
		}
```

- [ ] **Step 3: Verify the manifest still parses and resolves the expected groups**

Run:
```bash
cd /home/buvy/code/NorseArchitecture/.github
pwsh -NoProfile -Command "
	. ./scripts/lib/realm-classification.ps1
	\$m = Import-PowerShellDataFile ./config/manifest.psd1
	(Get-RealmGroups \$m 'Naglfar') -join ', '
	Get-RealmGated \$m 'Naglfar'
"
```
Expected output:
```
universal, sdk, dotnet, nuget, tests, ci, workflows, claude
False
```

- [ ] **Step 4: Stage the change**

```bash
cd /home/buvy/code/NorseArchitecture/.github
git add config/manifest.psd1
git status
```

Show the diff. Do not commit — this is a separate repo from Bifrost with its own human review.

---

## Task 2: Naglfar — bootstrap root .NET config files

**Files:**
- Create: `/home/buvy/code/NorseArchitecture/Bifrost/Naglfar/.editorconfig`
- Create: `/home/buvy/code/NorseArchitecture/Bifrost/Naglfar/Directory.Build.props`
- Create: `/home/buvy/code/NorseArchitecture/Bifrost/Naglfar/global.json`
- Create: `/home/buvy/code/NorseArchitecture/Bifrost/Naglfar/nuget.config`

**Interfaces:**
- Consumes: Task 1's manifest groups (this task manually creates exactly the files those groups would scatter — `universal` ⇒ `.editorconfig`, `nuget.config` (LICENSE/.gitattributes/.gitignore already present); `sdk` ⇒ `global.json`; `dotnet` ⇒ `Directory.Build.props`).
- Produces: the root MSBuild/analyzer baseline every subsequent Naglfar `.NET` project (Task 3+) inherits via `GetPathOfFileAbove`.

These four files are byte-for-byte identical across every realm today (verified: `diff` against Asgard's copies is empty) — copy them verbatim from Ginnungagap's canonical source rather than retyping. No test — these are inert config files with no executable behavior of their own; their effect is proven indirectly once Task 3's project builds.

- [ ] **Step 1: Copy the four canonical files**

```bash
cp /home/buvy/code/NorseArchitecture/.github/config/.editorconfig /home/buvy/code/NorseArchitecture/Bifrost/Naglfar/.editorconfig
cp /home/buvy/code/NorseArchitecture/.github/config/Directory.Build.props /home/buvy/code/NorseArchitecture/Bifrost/Naglfar/Directory.Build.props
cp /home/buvy/code/NorseArchitecture/.github/config/global.json /home/buvy/code/NorseArchitecture/Bifrost/Naglfar/global.json
cp /home/buvy/code/NorseArchitecture/.github/config/nuget.config /home/buvy/code/NorseArchitecture/Bifrost/Naglfar/nuget.config
```

- [ ] **Step 2: Verify each file matches its canonical source exactly**

```bash
diff /home/buvy/code/NorseArchitecture/.github/config/.editorconfig /home/buvy/code/NorseArchitecture/Bifrost/Naglfar/.editorconfig
diff /home/buvy/code/NorseArchitecture/.github/config/Directory.Build.props /home/buvy/code/NorseArchitecture/Bifrost/Naglfar/Directory.Build.props
diff /home/buvy/code/NorseArchitecture/.github/config/global.json /home/buvy/code/NorseArchitecture/Bifrost/Naglfar/global.json
diff /home/buvy/code/NorseArchitecture/.github/config/nuget.config /home/buvy/code/NorseArchitecture/Bifrost/Naglfar/nuget.config
```
Expected: no output from any `diff` (all four identical).

- [ ] **Step 3: Stage and show the diff**

```bash
cd /home/buvy/code/NorseArchitecture/Bifrost/Naglfar
git add .editorconfig Directory.Build.props global.json nuget.config
git status
```

---

## Task 3: Naglfar — src/tests scaffolding and the solution file

**Files:**
- Create: `/home/buvy/code/NorseArchitecture/Bifrost/Naglfar/src/Directory.Build.props`
- Create: `/home/buvy/code/NorseArchitecture/Bifrost/Naglfar/src/Directory.Build.targets`
- Create: `/home/buvy/code/NorseArchitecture/Bifrost/Naglfar/tests/Directory.Build.props`
- Create: `/home/buvy/code/NorseArchitecture/Bifrost/Naglfar/tests/Directory.Build.targets`
- Create: `/home/buvy/code/NorseArchitecture/Bifrost/Naglfar/Naglfar.slnx`

**Interfaces:**
- Consumes: Task 2's root `Directory.Build.props` (via `GetPathOfFileAbove` imports in each file below).
- Produces: the `src/` and `tests/` MSBuild scaffolding Task 5's `DesignSystem.Stories` project and its tests inherit; `Naglfar.slnx` is the solution file `dotnet build`/`dotnet test`/IDEs load.

- [ ] **Step 1: Copy the four canonical `src`/`tests` MSBuild files**

```bash
mkdir -p /home/buvy/code/NorseArchitecture/Bifrost/Naglfar/src
mkdir -p /home/buvy/code/NorseArchitecture/Bifrost/Naglfar/tests
cp /home/buvy/code/NorseArchitecture/.github/config/src/Directory.Build.props /home/buvy/code/NorseArchitecture/Bifrost/Naglfar/src/Directory.Build.props
cp /home/buvy/code/NorseArchitecture/.github/config/src/Directory.Build.targets /home/buvy/code/NorseArchitecture/Bifrost/Naglfar/src/Directory.Build.targets
cp /home/buvy/code/NorseArchitecture/.github/config/tests/Directory.Build.props /home/buvy/code/NorseArchitecture/Bifrost/Naglfar/tests/Directory.Build.props
cp /home/buvy/code/NorseArchitecture/.github/config/tests/Directory.Build.targets /home/buvy/code/NorseArchitecture/Bifrost/Naglfar/tests/Directory.Build.targets
```

- [ ] **Step 2: Verify each file matches its canonical source exactly**

```bash
diff /home/buvy/code/NorseArchitecture/.github/config/src/Directory.Build.props /home/buvy/code/NorseArchitecture/Bifrost/Naglfar/src/Directory.Build.props
diff /home/buvy/code/NorseArchitecture/.github/config/src/Directory.Build.targets /home/buvy/code/NorseArchitecture/Bifrost/Naglfar/src/Directory.Build.targets
diff /home/buvy/code/NorseArchitecture/.github/config/tests/Directory.Build.props /home/buvy/code/NorseArchitecture/Bifrost/Naglfar/tests/Directory.Build.props
diff /home/buvy/code/NorseArchitecture/.github/config/tests/Directory.Build.targets /home/buvy/code/NorseArchitecture/Bifrost/Naglfar/tests/Directory.Build.targets
```
Expected: no output.

- [ ] **Step 3: Create `Naglfar.slnx`**

Following `Asgard.slnx`'s structure exactly, scoped to Naglfar's one project pair (Task 5 adds the actual `<Project Path>` entries once they exist — this step creates the file with just the solution-items folder and empty `src`/`tests` folders so `dotnet sln` has somewhere to add to):

```xml
<Solution>
	<Folder Name="/Solution Items/">
		<File Path=".editorconfig" />
		<File Path=".gitattributes" />
		<File Path=".gitignore" />
		<File Path="Directory.Build.props" />
		<File Path="global.json" />
		<File Path="LICENSE" />
	</Folder>
	<Folder Name="/src/">
		<File Path="src/Directory.Build.props" />
		<File Path="src/Directory.Build.targets" />
	</Folder>
	<Folder Name="/tests/">
		<File Path="tests/Directory.Build.props" />
		<File Path="tests/Directory.Build.targets" />
	</Folder>
</Solution>
```

Save to `/home/buvy/code/NorseArchitecture/Bifrost/Naglfar/Naglfar.slnx`.

- [ ] **Step 4: Verify the solution loads**

```bash
cd /home/buvy/code/NorseArchitecture/Bifrost/Naglfar
dotnet sln Naglfar.slnx list
```
Expected: no error (empty project list is fine at this point — Task 5 adds the first project).

- [ ] **Step 5: Stage and show the diff**

```bash
cd /home/buvy/code/NorseArchitecture/Bifrost/Naglfar
git add src/Directory.Build.props src/Directory.Build.targets tests/Directory.Build.props tests/Directory.Build.targets Naglfar.slnx
git status
```

---

## Task 4: Asgard — `IDashboardWidget` primitive (TDD)

**Files:**
- Create: `Asgard/src/Abstractions.Components/Primitives/IDashboardWidget.cs`
- Create: `Asgard/tests/Abstractions.Components.Tests/Primitives/IDashboardWidgetTests.cs`

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces: `Norse.Abstractions.Components.Primitives.IDashboardWidget` — `string Title { get; }`, `Type ComponentType { get; }`. Task 6 (Naglfar's first story) does not consume this directly, but any future realm's dashboard-widget implementation will implement it.

- [ ] **Step 1: Write the failing test**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Asgard/tests/Abstractions.Components.Tests/Primitives/IDashboardWidgetTests.cs`:

```csharp
namespace Norse.Abstractions.Components.Tests.Primitives;

public sealed class IDashboardWidgetTests
{
	[Fact]
	void Title_returns_concrete_value()
	{
		StubWidget widget = new();

		widget.Title.ShouldBe("Stub Widget");
	}

	[Fact]
	void ComponentType_returns_concrete_value()
	{
		StubWidget widget = new();

		widget.ComponentType.ShouldBe(typeof(StubWidgetComponent));
	}

	sealed class StubWidget : IDashboardWidget
	{
		public string Title => "Stub Widget";
		public Type ComponentType => typeof(StubWidgetComponent);
	}

	sealed class StubWidgetComponent;
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd /home/buvy/code/NorseArchitecture/Bifrost/Asgard
dotnet test tests/Abstractions.Components.Tests/Abstractions.Components.Tests.csproj --filter "IDashboardWidgetTests"
```
Expected: build FAILS — `IDashboardWidget` does not exist yet (`CS0246`).

- [ ] **Step 3: Write the minimal interface**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Asgard/src/Abstractions.Components/Primitives/IDashboardWidget.cs`:

```csharp
namespace Norse.Abstractions.Components.Primitives;

/// <summary>
/// Declares a component that can register as a dashboard widget an end user arranges.
/// No rendering, no persistence — pure declared law, per Asgard's charter.
/// </summary>
public interface IDashboardWidget
{
	string Title { get; }

	Type ComponentType { get; }
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
cd /home/buvy/code/NorseArchitecture/Bifrost/Asgard
dotnet test tests/Abstractions.Components.Tests/Abstractions.Components.Tests.csproj --filter "IDashboardWidgetTests"
```
Expected: PASS (2 tests).

- [ ] **Step 5: Stage and show the diff**

```bash
cd /home/buvy/code/NorseArchitecture/Bifrost/Asgard
git add src/Abstractions.Components/Primitives/IDashboardWidget.cs tests/Abstractions.Components.Tests/Primitives/IDashboardWidgetTests.cs
git status
```

---

## Task 5: Asgard — headless `Loader` component (TDD, bUnit)

**Files:**
- Modify: `Asgard/src/Abstractions.Components/Abstractions.Components.csproj`
- Create: `Asgard/src/Abstractions.Components/Loader.razor`
- Create: `Asgard/src/Abstractions.Components/Loader.razor.css`
- Modify: `Asgard/tests/Abstractions.Components.Tests/Abstractions.Components.Tests.csproj`
- Create: `Asgard/tests/Abstractions.Components.Tests/LoaderTests.cs`

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces: `Norse.Abstractions.Components.Loader` — a headless Razor component, parameter `string Label` (default `"Loading…"`), no third-party design-system package reference. Task 6 (Naglfar's `DesignSystem.Stories`) stories this component.

The existing `Abstractions.Components.csproj` uses `Sdk="Microsoft.NET.Sdk"`, which does not compile `.razor` files — it must become `Microsoft.NET.Sdk.Razor` first.

- [ ] **Step 1: Switch the project to the Razor SDK**

Current content of `/home/buvy/code/NorseArchitecture/Bifrost/Asgard/src/Abstractions.Components/Abstractions.Components.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse component abstractions: Razor component base types shared across Blazor WASM, Blazor Server, and MAUI consumers. No ASP.NET Core, EF Core, or server-side infrastructure references — this assembly must compile into a client bundle.</Description>
	</PropertyGroup>
</Project>
```

Replace with:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
	<PropertyGroup>
		<Description>Norse component abstractions: Razor component base types shared across Blazor WASM, Blazor Server, and MAUI consumers. No ASP.NET Core, EF Core, or server-side infrastructure references — this assembly must compile into a client bundle.</Description>
	</PropertyGroup>
</Project>
```

- [ ] **Step 2: Verify the project still builds with zero source files**

```bash
cd /home/buvy/code/NorseArchitecture/Bifrost/Asgard
dotnet build src/Abstractions.Components/Abstractions.Components.csproj
```
Expected: builds successfully (the SDK switch alone is a no-op until `.razor` files exist).

- [ ] **Step 3: Add bUnit to the test project**

Current `/home/buvy/code/NorseArchitecture/Bifrost/Asgard/tests/Abstractions.Components.Tests/Abstractions.Components.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="../../src/Abstractions.Components/Abstractions.Components.csproj" />
	</ItemGroup>
</Project>
```

Replace with:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
	<ItemGroup>
		<ProjectReference Include="../../src/Abstractions.Components/Abstractions.Components.csproj" />
	</ItemGroup>
	<ItemGroup>
		<PackageReference Include="bunit" Version="*" />
	</ItemGroup>
</Project>
```

(The test project also switches to `Microsoft.NET.Sdk.Razor` — bUnit's rendering harness needs the Razor-aware compiler even though this project has no `.razor` files of its own.)

- [ ] **Step 4: Write the failing test**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Asgard/tests/Abstractions.Components.Tests/LoaderTests.cs`:

```csharp
using Bunit;

namespace Norse.Abstractions.Components.Tests;

public sealed class LoaderTests : TestContext
{
	[Fact]
	void Renders_default_label()
	{
		var cut = RenderComponent<Loader>();

		cut.Find("[role='status']").GetAttribute("aria-label").ShouldBe("Loading…");
	}

	[Fact]
	void Renders_custom_label()
	{
		var cut = RenderComponent<Loader>(parameters => parameters
			.Add(p => p.Label, "Fetching widgets…"));

		cut.Find("[role='status']").GetAttribute("aria-label").ShouldBe("Fetching widgets…");
	}
}
```

- [ ] **Step 5: Run the test to verify it fails**

```bash
cd /home/buvy/code/NorseArchitecture/Bifrost/Asgard
dotnet test tests/Abstractions.Components.Tests/Abstractions.Components.Tests.csproj --filter "LoaderTests"
```
Expected: build FAILS — `Loader` does not exist yet (`CS0246`).

- [ ] **Step 6: Write the minimal component**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Asgard/src/Abstractions.Components/Loader.razor`:

```razor
<div role="status" aria-label="@Label" class="norse-loader">
	<div class="norse-loader__ring"></div>
</div>

@code {
	[Parameter]
	public string Label { get; set; } = "Loading…";
}
```

Create `/home/buvy/code/NorseArchitecture/Bifrost/Asgard/src/Abstractions.Components/Loader.razor.css`:

```css
.norse-loader {
	display: inline-flex;
	align-items: center;
	justify-content: center;
}

.norse-loader__ring {
	width: 1.5rem;
	height: 1.5rem;
	border: 0.2rem solid currentColor;
	border-top-color: transparent;
	border-radius: 50%;
	animation: norse-loader-spin 0.8s linear infinite;
}

@keyframes norse-loader-spin {
	to {
		transform: rotate(360deg);
	}
}
```

- [ ] **Step 7: Run the test to verify it passes**

```bash
cd /home/buvy/code/NorseArchitecture/Bifrost/Asgard
dotnet test tests/Abstractions.Components.Tests/Abstractions.Components.Tests.csproj --filter "LoaderTests"
```
Expected: PASS (2 tests).

- [ ] **Step 8: Run the full Abstractions.Components.Tests suite to check for regressions**

```bash
cd /home/buvy/code/NorseArchitecture/Bifrost/Asgard
dotnet test tests/Abstractions.Components.Tests/Abstractions.Components.Tests.csproj
```
Expected: PASS — includes the pre-existing `WiringTests.Project_wires_up`, Task 4's `IDashboardWidgetTests`, and this task's `LoaderTests`.

- [ ] **Step 9: Stage and show the diff**

```bash
cd /home/buvy/code/NorseArchitecture/Bifrost/Asgard
git add src/Abstractions.Components/Abstractions.Components.csproj src/Abstractions.Components/Loader.razor src/Abstractions.Components/Loader.razor.css tests/Abstractions.Components.Tests/Abstractions.Components.Tests.csproj tests/Abstractions.Components.Tests/LoaderTests.cs
git status
```

---

## Task 6: Naglfar — `DesignSystem.Stories` (BlazingStory host)

**Files:**
- Create: `Naglfar/src/DesignSystem.Stories/DesignSystem.Stories.csproj` (and template-generated files under the same folder)
- Modify: `Naglfar/src/DesignSystem.Stories/DesignSystem.Stories.csproj` (add the `NorseRef` to `Abstractions.Components`)
- Create: `Naglfar/src/DesignSystem.Stories/Stories/Loader.stories.razor`
- Modify: `Naglfar/Naglfar.slnx` (add the project entry)

**Interfaces:**
- Consumes: `Norse.Abstractions.Components.Loader` (Task 5) via a live `NorseRef`/`ProjectReference`.
- Produces: a runnable BlazingStory catalog — no other task consumes this project.

- [ ] **Step 1: Install the BlazingStory project template**

```bash
dotnet new install BlazingStory.ProjectTemplates
```
Expected: template installs; `dotnet new list` includes `blazingstorywasm`.

- [ ] **Step 2: Scaffold the project**

```bash
cd /home/buvy/code/NorseArchitecture/Bifrost/Naglfar/src
dotnet new blazingstorywasm -n DesignSystem.Stories
```
Expected: creates `src/DesignSystem.Stories/` with a working BlazingStory host app (its own `.csproj`, `Program.cs`, `wwwroot/`, and a `Stories/` folder for `.stories.razor` files).

- [ ] **Step 3: Add the NorseRef to Asgard's Abstractions.Components**

Open `src/DesignSystem.Stories/DesignSystem.Stories.csproj` and add:

```xml
	<ItemGroup>
		<NorseRef Include="Abstractions.Components">
			<Repo>Asgard</Repo>
		</NorseRef>
	</ItemGroup>
```

- [ ] **Step 4: Add the project to `Naglfar.slnx`**

Update `/home/buvy/code/NorseArchitecture/Bifrost/Naglfar/Naglfar.slnx`'s `/src/` folder:

```xml
	<Folder Name="/src/">
		<File Path="src/Directory.Build.props" />
		<File Path="src/Directory.Build.targets" />
		<Project Path="src/DesignSystem.Stories/DesignSystem.Stories.csproj" />
	</Folder>
```

- [ ] **Step 5: Build and verify the NorseRef resolves to a live project reference**

```bash
cd /home/buvy/code/NorseArchitecture/Bifrost/Naglfar
dotnet build src/DesignSystem.Stories/DesignSystem.Stories.csproj
```
Expected: builds successfully, pulling `Norse.Abstractions.Components.dll` from Asgard's own `bin/` output (not from a NuGet cache) — confirms `UseProjectReferences=true` resolved the `NorseRef` to `../../../Asgard/src/Abstractions.Components/Abstractions.Components.csproj` as a live `ProjectReference`.

- [ ] **Step 6: Write the story for `Loader`**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Naglfar/src/DesignSystem.Stories/Stories/Loader.stories.razor`:

```razor
@using Norse.Abstractions.Components
@attribute [Stories("Primitives/Loader")]

<Stories TComponent="Loader">
	<Story Name="Default">
		<Template>
			<Loader @attributes="context.Args" />
		</Template>
	</Story>
	<Story Name="Custom Label">
		<Template>
			<Loader Label="Fetching widgets…" @attributes="context.Args" />
		</Template>
	</Story>
</Stories>
```

- [ ] **Step 7: Run the story host and confirm it serves**

```bash
cd /home/buvy/code/NorseArchitecture/Bifrost/Naglfar
dotnet run --project src/DesignSystem.Stories/DesignSystem.Stories.csproj &
sleep 5
curl -sf http://localhost:5000/ > /dev/null && echo "OK: story host responding"
kill %1
```
Expected: `OK: story host responding` (adjust the port to whatever `dotnet run`'s console output reports if it differs from 5000).

- [ ] **Step 8: Stage and show the diff**

```bash
cd /home/buvy/code/NorseArchitecture/Bifrost/Naglfar
git add src/DesignSystem.Stories Naglfar.slnx
git status
```

---

## Task 7: Doc-sync — Heimdall `Norse.Access` → `Norse.AuthN`

**Files:**
- Modify: `Glitnir/docs/codenames.md` (line 29)
- Modify: `Glitnir/docs/decomposition.md` (lines 29, 49)
- Modify: `Bifrost/CLAUDE.md` (§2 naming table)
- Modify: `Bifrost/README.md` (realm table)
- Modify: `Heimdall/CLAUDE.md` (title and §1)

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces: nothing consumed by other tasks — pure documentation.

No test — these are prose documents. Verification is a repo-wide grep proving zero remaining `Norse.Access` references to Heimdall.

- [ ] **Step 1: Update `Glitnir/docs/codenames.md` line 29**

Current:
```
| **Heimdall** | `Norse.Access` | The ever-watchful guardian who alone decides who may cross — auth services riding on Himinbjorg's identity record: one access ruleset across Blazor Server, WASM, and MAUI, with admin components and the backing gRPC service. |
```

Replace with:
```
| **Heimdall** | `Norse.AuthN` | The ever-watchful guardian who alone decides who may cross — the authn story built on Himinbjorg's identity record: login, register, forgot-password, 2FA setup, recovery, and reset, uniform across Blazor Server, WASM, and MAUI, with the backing gRPC service. |
```

- [ ] **Step 2: Update `Glitnir/docs/decomposition.md` lines 29 and 49**

Line 29, current:
```
| `Norse.Access` | Auth services on Himinbjorg's identity record: one access ruleset across Blazor Server, WASM, and MAUI, with admin components and the backing gRPC service | Heimdall |
```
Replace with:
```
| `Norse.AuthN` | The authn story on Himinbjorg's identity record: login, register, forgot-password, 2FA setup, recovery, and reset, uniform across Blazor Server, WASM, and MAUI, with the backing gRPC service | Heimdall |
```

Line 49, current:
```
| **Heimdall** | `Norse.Access.*` | Auth services riding on Himinbjorg: one access ruleset across Blazor Server, WASM, and MAUI, with admin components and the backing gRPC service. |
```
Replace with:
```
| **Heimdall** | `Norse.AuthN.*` | The authn story riding on Himinbjorg: login, register, forgot-password, 2FA setup, recovery, and reset, uniform across Blazor Server, WASM, and MAUI, with the backing gRPC service. |
```

- [ ] **Step 3: Update `Bifrost/CLAUDE.md` §2 naming table**

Current row:
```
| Heimdall | `Norse.Access.*` | Auth services on Himinbjörg: one access ruleset across Blazor Server, WASM, and MAUI, with admin Blazor components and the backing gRPC service |
```
Replace with:
```
| Heimdall | `Norse.AuthN.*` | The authn story on Himinbjörg's identity record: login, register, forgot-password, 2FA setup, recovery, and reset, uniform across Blazor Server, WASM, and MAUI, with the backing gRPC service |
```

- [ ] **Step 4: Update `Bifrost/README.md` realm table**

Current:
```
| [Heimdall](https://github.com/NorseArchitecture/Heimdall) | `Norse.Access.*` — auth services on Himinbjörg: one access ruleset across Blazor Server, WASM, and MAUI, with admin Blazor components and the backing gRPC service |
```
Replace with:
```
| [Heimdall](https://github.com/NorseArchitecture/Heimdall) | `Norse.AuthN.*` — the authn story on Himinbjörg's identity record: login, register, forgot-password, 2FA setup, recovery, and reset, uniform across Blazor Server, WASM, and MAUI, with the backing gRPC service |
```

- [ ] **Step 5: Update `Heimdall/CLAUDE.md`**

Current title line:
```
# CLAUDE.md — Heimdall (`Norse.Access`)
```
Replace with:
```
# CLAUDE.md — Heimdall (`Norse.AuthN`)
```

Current §1 opening sentence:
```
Heimdall is **the gate** — `Norse.Access`: auth services built on Himinbjorg, presenting one access ruleset uniformly across Blazor Server, WASM, and MAUI, plus the admin Blazor components and the backing gRPC service. It is the topmost realm in the dependency chain among the current submodules — nothing else rides above it.
```
Replace with:
```
Heimdall is **the gate** — `Norse.AuthN`: the authn story built on Himinbjorg, presenting login, register, forgot-password, 2FA setup, recovery, and reset uniformly across Blazor Server, WASM, and MAUI, plus the backing gRPC service. It is the topmost realm in the dependency chain among the current submodules — nothing else rides above it.
```

- [ ] **Step 6: Verify zero remaining `Norse.Access` references to Heimdall**

```bash
cd /home/buvy/code/NorseArchitecture/Bifrost
grep -rn "Norse\.Access" Glitnir/docs/codenames.md Glitnir/docs/decomposition.md CLAUDE.md README.md Heimdall/CLAUDE.md
```
Expected: no output (empty grep result — all five occurrences renamed).

- [ ] **Step 7: Stage each repo's changes separately and show the diffs**

```bash
cd /home/buvy/code/NorseArchitecture/Bifrost/Glitnir
git add docs/codenames.md docs/decomposition.md
git status

cd /home/buvy/code/NorseArchitecture/Bifrost
git add CLAUDE.md README.md
git status

cd /home/buvy/code/NorseArchitecture/Bifrost/Heimdall
git add CLAUDE.md
git status
```

---

## Task 8: Doc cleanup — remove the stale "Chamber" row (correction below)

**Correction, found during execution:** this task's original text named `Bifrost/CLAUDE.md` §0 as the location — that was wrong. `Bifrost/CLAUDE.md` has no platform-vocabulary table and never did; grepping it for "Chamber" during execution returned nothing. The actual "Chamber" row lives in Buvy's personal global `CLAUDE.md` (`~/.claude/CLAUDE.md`, a symlink into his own dotfiles repo) — a file outside every repo this plan otherwise touches, and personal rather than project configuration. Executed directly by the controller rather than as a dispatched subagent task, since it's a one-line removal in a personal settings file, not project doc content. While there, Buvy also asked to remove the same table's other Hadron-specific codenames (Collider, Accelerator, Crucible) and the now-dangling Open Source section sentence referencing them — done in the same pass.

**Files (actual):**
- Modify: Buvy's personal global `CLAUDE.md` (dotfiles repo, not part of this plan's repo set)

**Interfaces:**
- Consumes: nothing.
- Produces: nothing.

- [x] **Step 1: Remove the Chamber row and the other Hadron codenames**

Removed the entire `Platform Vocabulary` table (Collider/Accelerator/Crucible/Chamber rows) and its OSS-umbrella follow-up sentence, keeping the `Bounded contexts` line. Removed the Open Source section's now-dangling reference to the same retired codenames.

- [x] **Step 2: Verify no other reference to "Chamber" remains**

```bash
grep -in chamber /home/buvy/code/buvinghausen/buvinghausen/CLAUDE.md
```
Confirmed empty (no match, exit 1).

- [ ] **Step 3: Stage and show the diff**

```bash
cd /home/buvy/code/NorseArchitecture/Bifrost
git add CLAUDE.md
git status
```

---

## Explicitly Out of Scope (unchanged from the spec)

- Heimdall's and Mimir's own gRPC contracts, DTOs, validators, and `.Components.FluentUI` implementations — both realms remain gated by their own CLAUDE.md ("no code before a converged spec"); each needs its own brainstorm → spec → plan first.
- Blazorise — no code, no package reference, anywhere in this plan.
- The dashboard-widget layout-preference persistence service and the dashboard-composition/rendering mechanism itself (Midgard's likely future charter) — `IDashboardWidget` (Task 4) is declared and tested in isolation; nothing consumes it yet.
- Hosting/publishing BlazingStory beyond Naglfar's local dev-time project (live container, `story.{company}.{tld}`, or a published `.stories.razor` NuGet package) — captured separately as its own future brainstorm.

---

## Self-Review

**Spec coverage:** §1.2/§2.1 (Asgard headless components + `Primitives/` interface, no new project) → Tasks 4–5. §1.3 (Naglfar hosts BlazingStory only, first .NET project) → Tasks 2–3, 6. §1.4/§4 (Heimdall rename across four documents) → Task 7. The stale Chamber row (§4) → Task 8. Task 1 is a prerequisite the spec's §7 success criteria assume but didn't itself narrate as a task — added because Naglfar's manifest exception blocks everything in Tasks 2–3 otherwise.

**Placeholder scan:** Every step has complete file content or an exact command with expected output — no "add tests for the above," no invented APIs (BlazingStory's `Stories`/`Story`/`Template` markup and the `NorseRef`/`Repo` metadata syntax are both copied from verified working examples in this repo and the tool's own README, not guessed).

**Type consistency:** `IDashboardWidget.ComponentType` (Task 4) returns `Type`, matching `DynamicComponent`'s own `Type` parameter shape for whoever eventually renders a widget — not exercised by any task here, but the signature is chosen to compose with that later work rather than needing a rename. `Loader.Label` (Task 5) is referenced identically in both `LoaderTests.cs` and `Loader.stories.razor`.

Fixed inline during review: originally planned to leave `Abstractions.Components.Tests.csproj` on `Microsoft.NET.Sdk` — bUnit's component-rendering harness requires the Razor-aware SDK even without local `.razor` files, so Task 5 Step 3 now switches it alongside the source project.
