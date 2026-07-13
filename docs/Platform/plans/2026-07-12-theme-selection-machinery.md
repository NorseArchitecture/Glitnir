# Theme Selection Machinery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the platform's first theme-selection machinery — Naglfar packs its generated FluentUI seed and semantic CSS tokens as a NuGet package for the first time, Midgard stands up `Infrastructure.Components.Theme`/`.Theme.FluentUI` as the first slice of its "UI composition" charter, and both Yggdrasil host pairs (`Hosting.Stories.*`, `Hosting.Web.*`) wire it in — so flipping the OS/browser color-scheme preference and reloading changes the rendered theme everywhere, with no manual toggle anywhere yet.

**Architecture:** Naglfar's Style Dictionary pipeline gains a `Microsoft.NET.Sdk.Razor` project (`DesignSystem.Tokens`) that packs the already-generated `FluentTokenSeed.g.cs` plus a native `wwwroot/norse-design-tokens.css` static asset — both flow through NuGet's static-web-assets propagation with zero manual file-copy wiring downstream. Midgard's `Infrastructure.Components.Theme` takes a bare `NorseRef` to that package (no FluentUI dependency); `Infrastructure.Components.Theme.FluentUI` layers `AddFluentUIComponents()` + a `NorseFluentDesignTheme` wrapper on top, seeded from the same package's `FluentTokenSeed`. Both Yggdrasil host pairs reference `Infrastructure.Components.Theme.FluentUI` and wire it once at their root.

**Tech Stack:** .NET (net11.0), `Microsoft.NET.Sdk.Razor`, `Microsoft.FluentUI.AspNetCore.Components` 4.14.x, Style Dictionary 5.x (Node ≥22), xUnit v3 + Shouldly, PowerShell (`manifest.psd1`).

## Global Constraints

- **`Infrastructure.Components.Theme` (Midgard) may never take a third-party UI-library `PackageReference`.** Only `.Theme.FluentUI` does. (addendum, Decision 4)
- **`Mode="DesignThemeModes.System"` only.** No manual toggle, no JS bridging to BlazingStory's own toggle, anywhere in this plan. (addendum, "Explicitly Still Deferred")
- **Asgard is not touched by this plan.** Zero files change under `Asgard/src/Abstractions.Components/`. (addendum, closed door)
- **`Norse.DesignSystem.Tokens` (NuGet) and `@norsearchitecture/design-tokens` (npm) version identically, published in the same CI step.** Never give the NuGet package its own version property. (addendum, Decision 2)
- **Naglfar's dark CSS override moves from `[data-theme="dark"]` to `@media (prefers-color-scheme: dark)`.** (addendum, Decision 3)
- US English spelling everywhere — code, comments, docs, commit copy.
- **`internal sealed` by default**; `omit_if_default` on accessibility modifiers.
- Tabs, 4-space width for C#; Razor and YAML follow the platform `.editorconfig`.
- **Commits happen only on `feature/theme-selection-machinery`, never `master`.** Every repo this plan touches has that branch checked out before Task 1 dispatches. Tasks whose steps end in `git commit` (6, 7, 10, 11) commit directly on that branch — the platform's "no automatic git commits" rule is about master/push, not a blanket ban on a subagent committing on a feature branch the human is watching. Tasks that only ever `git add` (1, 2, 3, 4, 5, 8, 9, 12, 13) stop there — the human commits those.
- **`UseProjectReferences=true` is Bifrost's default** — every `NorseRef` item resolves to a live `ProjectReference` against the sibling submodule while working inside Bifrost; never hardcode a package version where a `NorseRef` belongs.
- **Repo-relative paths only in this document** — no machine-local absolute paths (`Glitnir` CLAUDE.md anti-pattern list).

---

## Task 1: Ginnungagap — give Naglfar back its .NET groups, this time for a generated-only package

**Files:**
- Modify: `.github/config/manifest.psd1` (repo `NorseArchitecture/.github`, checked out as `../.github` beside `Bifrost`)

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces: the group list `Get-RealmGroups` resolves for `Naglfar` — Task 2 manually creates the files this scatters, so they must match this list exactly.

This is a PowerShell data file — verification is a `pwsh` sanity check, not a unit test.

- [ ] **Step 1: Read the current Naglfar entry**

Current (confirmed live, `manifest.psd1` around line 89):

```powershell
		# Design system — token pipeline only (JS/Style Dictionary, @norsearchitecture/design-tokens).
		# npm-only, no .NET at all as of 2026-07-12 — DesignSystem.Stories split out to Bragi the
		# same day it landed here. No 'sdk'/'dotnet'/'nuget'/'tests' groups: nothing here restores a
		# .NET SDK or resolves NorseRef. ci.yml (hand-authored, not scattered) already calls
		# ci-build-test-npm.yml, not the dotnet gate. Ungated.
		Naglfar   = @{
			Groups = @('universal', 'ci', 'workflows', 'claude')
			Gated  = $false
		}
```

- [ ] **Step 2: Replace it**

```powershell
		# Design system — token pipeline (JS/Style Dictionary) plus DesignSystem.Tokens, a single
		# 100%-generated .NET package (FluentTokenSeed.g.cs + norse-design-tokens.css) packed
		# alongside @norsearchitecture/design-tokens in the same release step. "npm-only, no .NET"
		# narrows to "no hand-authored C#" as of 2026-07-12 (Theme Selection Machinery addendum,
		# ../Bifrost/Glitnir/docs/Platform/specs/2026-07-11-blazor-component-architecture-design.md).
		# Ungated: nothing here is unit-testable logic — it's 100% generated output, verified by
		# Naglfar's existing test/build.test.js against the generated files directly.
		Naglfar   = @{
			Groups = @('universal', 'sdk', 'dotnet', 'nuget', 'tests', 'ci', 'workflows', 'claude')
			Gated  = $false
		}
```

- [ ] **Step 3: Verify the manifest parses and resolves the expected groups**

Run (from the `.github` repo root):
```bash
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

- [ ] **Step 4: Stage and show the diff**

```bash
git add config/manifest.psd1
git diff --cached
```

---

## Task 2: Naglfar — scaffold the minimal .NET root

**Files:**
- Create: `Naglfar/global.json`
- Create: `Naglfar/nuget.config`
- Create: `Naglfar/Directory.Build.props`
- Create: `Naglfar/src/Directory.Build.props`
- Create: `Naglfar/src/Directory.Build.targets`
- Create: `Naglfar/Naglfar.slnx`
- Modify: `Naglfar/.gitignore`

**Interfaces:**
- Consumes: nothing.
- Produces: the MSBuild property scaffold (`AssemblyName`, `RootNamespace`, `PackageId` = `Norse.$(MSBuildProjectName)`, `TargetFramework=net11.0`) every later Naglfar `.csproj` in this plan relies on.

No test — this is scaffolding, verified by Task 4's `dotnet build` succeeding.

- [ ] **Step 1: Copy `global.json` from an existing realm**

Match Midgard's exactly (same platform-wide SDK channel):

```bash
cp ../Midgard/global.json Naglfar/global.json
cp ../Midgard/nuget.config Naglfar/nuget.config
```

- [ ] **Step 2: Create the root `Directory.Build.props`**

```xml
<Project>
	<PropertyGroup>
		<AnalysisLevel>latest-Recommended</AnalysisLevel>
		<AnalysisLevelSecurity>latest-All</AnalysisLevelSecurity>
		<AnalysisLevelPerformance>latest-All</AnalysisLevelPerformance>
		<AnalysisLevelReliability>latest-All</AnalysisLevelReliability>
		<AnalysisLevelUsage>latest-All</AnalysisLevelUsage>
		<!--
			The brand prefix lives here and nowhere else: project folders and .csproj files are
			brand-free, so a fork rebrands by changing "Norse" once per realm — no file renames.
		-->
		<AssemblyName>Norse.$(MSBuildProjectName)</AssemblyName>
		<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
		<GenerateDocumentationFile>true</GenerateDocumentationFile>
		<ImplicitUsings>enable</ImplicitUsings>
		<LangVersion>preview</LangVersion>
		<Nullable>enable</Nullable>
		<RootNamespace>Norse.$(MSBuildProjectName)</RootNamespace>
		<TargetFramework>net11.0</TargetFramework>
		<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
		<UseProjectReferences Condition="'$(UseProjectReferences)' == ''">false</UseProjectReferences>
		<WarningLevel>9999</WarningLevel>
		<_ParentProps>$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))</_ParentProps>
	</PropertyGroup>
	<Import Project="$(_ParentProps)" Condition="Exists('$(_ParentProps)')" />
</Project>
```

- [ ] **Step 3: Create `src/Directory.Build.props`**

Byte-for-byte identical to Midgard's (`Midgard/src/Directory.Build.props`) — same `Authors`, `Deterministic`, `IsAotCompatible`, `MinVerTagPrefix`, `NoWarn` (NU5104), `PackageId`, `PackageReadmeFile`, `PackageLicenseFile`, `InternalsVisibleTo`, `MinVer` reference, conditional `README.md`/`LICENSE` pack includes. Copy it verbatim:

```bash
cp ../Midgard/src/Directory.Build.props Naglfar/src/Directory.Build.props
```

- [ ] **Step 4: Create `src/Directory.Build.targets`**

Byte-for-byte identical to Midgard's (`Midgard/src/Directory.Build.targets`) — the `NorseRef`/`NorseDesignRef` PackageReference-forwarding and generator-analyzer-stripping target. Copy it verbatim:

```bash
cp ../Midgard/src/Directory.Build.targets Naglfar/src/Directory.Build.targets
```

- [ ] **Step 5: Create `Naglfar.slnx`**

```xml
<Solution>
	<Folder Name="/Solution Items/">
		<File Path=".editorconfig" />
		<File Path=".gitattributes" />
		<File Path=".gitignore" />
		<File Path="CLAUDE.md" />
		<File Path="Directory.Build.props" />
		<File Path="global.json" />
		<File Path="LICENSE" />
		<File Path="nuget.config" />
		<File Path="README.md" />
	</Folder>
	<Folder Name="/src/">
		<File Path="src/Directory.Build.props" />
		<File Path="src/Directory.Build.targets" />
		<Project Path="src/DesignSystem.Tokens/DesignSystem.Tokens.csproj" />
	</Folder>
</Solution>
```

- [ ] **Step 6: Ensure generated output never gets committed**

Add to `Naglfar/.gitignore`, right after the existing `# NuGet Packages` block:

```gitignore
# Style Dictionary build output — regenerated by `npm run build`, never committed.
/dist/
```

- [ ] **Step 7: Stage and show the diff**

```bash
git -C Naglfar add global.json nuget.config Directory.Build.props src/Directory.Build.props src/Directory.Build.targets Naglfar.slnx .gitignore
git -C Naglfar diff --cached --stat
```

---

## Task 3: Naglfar — switch dark-theme CSS from `[data-theme="dark"]` to `@media (prefers-color-scheme: dark)`

**Files:**
- Modify: `Naglfar/style-dictionary.config.js`
- Modify: `Naglfar/test/build.test.js`

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces: `dist/css/tokens.css`'s dark-mode selector shape — Task 4's `norse-design-tokens.css` platform entry reuses the same `css/theme-variables` format, so this change applies to both outputs identically.

- [ ] **Step 1: Confirm the current failing shape (the test as it stands today asserts the old attribute selector)**

Run:
```bash
cd Naglfar && npm test
```
Expected: passes today (baseline, `[data-theme="dark"]` is what's currently generated).

- [ ] **Step 2: Update the two dark-mode assertions in `test/build.test.js` to expect a media query instead**

Replace:
```javascript
test('tokens.css exposes --color-semantic-primary and a dark override', () => {
	const css = readFileSync(new URL('../dist/css/tokens.css', import.meta.url), 'utf8');
	assert.match(css, /--color-semantic-primary:\s*#[0-9a-f]{6};/);
	assert.match(css, /\[data-theme="dark"\]\s*{[^}]*--color-semantic-primary:\s*#[0-9a-f]{6};/s);
});
```
With:
```javascript
test('tokens.css exposes --color-semantic-primary and a dark override', () => {
	const css = readFileSync(new URL('../dist/css/tokens.css', import.meta.url), 'utf8');
	assert.match(css, /--color-semantic-primary:\s*#[0-9a-f]{6};/);
	assert.match(css, /@media \(prefers-color-scheme: dark\)\s*{\s*:root\s*{[^}]*--color-semantic-primary:\s*#[0-9a-f]{6};/s);
});
```

And replace:
```javascript
test('elevation tokens are themed the same way color tokens are', () => {
	const css = readFileSync(new URL('../dist/css/tokens.css', import.meta.url), 'utf8');
	assert.match(css, /--elevation-1: 0 1px 2px rgba\(28, 26, 23, 0\.08\);/);
	assert.match(css, /\[data-theme="dark"\]\s*{[^}]*--elevation-1:\s*0 0 0 1px rgba\(246, 244, 240, 0\.06\);/s);
});
```
With:
```javascript
test('elevation tokens are themed the same way color tokens are', () => {
	const css = readFileSync(new URL('../dist/css/tokens.css', import.meta.url), 'utf8');
	assert.match(css, /--elevation-1: 0 1px 2px rgba\(28, 26, 23, 0\.08\);/);
	assert.match(css, /@media \(prefers-color-scheme: dark\)\s*{\s*:root\s*{[^}]*--elevation-1:\s*0 0 0 1px rgba\(246, 244, 240, 0\.06\);/s);
});
```

- [ ] **Step 3: Run the tests to confirm they now fail (red)**

```bash
npm test
```
Expected: both edited assertions FAIL (`[data-theme="dark"]` is still what's generated — the format hasn't changed yet).

- [ ] **Step 4: Edit the `css/theme-variables` format in `style-dictionary.config.js`**

Replace:
```javascript
StyleDictionary.registerFormat({
	name: 'css/theme-variables',
	format: async ({ dictionary }) => {
		const light = dictionary.allTokens.filter((t) => t.path.at(-1) === 'light');
		const dark = dictionary.allTokens.filter((t) => t.path.at(-1) === 'dark');
		const themeless = dictionary.allTokens.filter(
			(t) => t.path.at(-1) !== 'light' && t.path.at(-1) !== 'dark',
		);

		const rootLines = [...themeless, ...light].map((t) => `  --${cssVarName(t)}: ${t.$value};`);
		const darkLines = dark.map((t) => `  --${cssVarName(t)}: ${t.$value};`);

		return `:root {\n${rootLines.join('\n')}\n}\n\n[data-theme="dark"] {\n${darkLines.join('\n')}\n}\n`;
	},
});
```
With:
```javascript
StyleDictionary.registerFormat({
	name: 'css/theme-variables',
	format: async ({ dictionary }) => {
		const light = dictionary.allTokens.filter((t) => t.path.at(-1) === 'light');
		const dark = dictionary.allTokens.filter((t) => t.path.at(-1) === 'dark');
		const themeless = dictionary.allTokens.filter(
			(t) => t.path.at(-1) !== 'light' && t.path.at(-1) !== 'dark',
		);

		const rootLines = [...themeless, ...light].map((t) => `  --${cssVarName(t)}: ${t.$value};`);
		const darkLines = dark.map((t) => `    --${cssVarName(t)}: ${t.$value};`);

		// Media-query-driven, not attribute-driven: nothing in the platform sets [data-theme]
		// yet (confirmed by search, 2026-07-12), and the actual requirement is "flip the OS/
		// browser preference and reload" — see the Theme Selection Machinery addendum, Decision 3.
		// A [data-theme] override can be layered on top later without touching this shape.
		return `:root {\n${rootLines.join('\n')}\n}\n\n@media (prefers-color-scheme: dark) {\n  :root {\n${darkLines.join('\n')}\n  }\n}\n`;
	},
});
```

- [ ] **Step 5: Rebuild and re-run tests to confirm they pass (green)**

```bash
npm run build && npm test
```
Expected: all tests pass, including the two edited in Step 2.

- [ ] **Step 6: Stage and show the diff**

```bash
git add style-dictionary.config.js test/build.test.js
git diff --cached
```

---

## Task 4: Naglfar — `DesignSystem.Tokens` project, packing the seed and the CSS as native static web assets

**Files:**
- Modify: `Naglfar/style-dictionary.config.js`
- Create: `Naglfar/src/DesignSystem.Tokens/DesignSystem.Tokens.csproj`
- Create: `Naglfar/src/DesignSystem.Tokens/README.md`

**Interfaces:**
- Consumes: Task 2's scaffold (`src/Directory.Build.props`/`.targets`), Task 3's media-query CSS shape.
- Produces: NuGet package `Norse.DesignSystem.Tokens` carrying `Norse.DesignSystem.FluentTokenSeed` (public `const string AccentBaseColor`/`NeutralBaseColor`) and a static web asset at `_content/Norse.DesignSystem.Tokens/norse-design-tokens.css` — both consumed by Task 6/7 (Midgard).

- [ ] **Step 1: Add a second Style Dictionary platform that writes the CSS natively into the new project's `wwwroot/`**

In `style-dictionary.config.js`, the existing `platforms.css` block writes only to `dist/css/`. Add a sibling platform (don't touch the existing one — the npm package's public `./css` export must keep working unchanged):

```javascript
		cssWwwroot: {
			transformGroup: 'css',
			buildPath: 'src/DesignSystem.Tokens/wwwroot/',
			files: [
				{
					destination: 'norse-design-tokens.css',
					format: 'css/theme-variables',
				},
			],
		},
```

Add it as a sibling key inside the existing `platforms: { ... }` object (after `css`, before `js`).

- [ ] **Step 2: Rebuild and confirm the new file lands where expected**

```bash
cd Naglfar && npm run build
test -f src/DesignSystem.Tokens/wwwroot/norse-design-tokens.css && echo "OK: wwwroot CSS generated"
test -f dist/csharp/FluentTokenSeed.g.cs && echo "OK: seed still generated"
```
Expected: both `OK` lines print.

- [ ] **Step 3: Create `DesignSystem.Tokens.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
	<PropertyGroup>
		<Description>Norse.DesignSystem.Tokens: 100%-generated output of Naglfar's Style Dictionary pipeline — FluentTokenSeed (AccentBaseColor/NeutralBaseColor, for FluentUI Blazor's DesignTokens) and a native wwwroot/norse-design-tokens.css static web asset (plain semantic color custom properties, no third-party dependency). Versioned identically to @norsearchitecture/design-tokens; published in the same release step. No hand-authored C# anywhere in this project.</Description>
	</PropertyGroup>
	<ItemGroup>
		<Compile Include="../../dist/csharp/FluentTokenSeed.g.cs" Link="FluentTokenSeed.g.cs" />
	</ItemGroup>
</Project>
```

(`wwwroot/norse-design-tokens.css` needs no explicit item — `Microsoft.NET.Sdk.Razor` picks up anything under the project's own `wwwroot/` automatically as a static web asset.)

- [ ] **Step 4: Create the package README (required for `PackageReadmeFile` to actually pack one, per `src/Directory.Build.props`'s conditional include)**

```markdown
# Norse.DesignSystem.Tokens

Generated output of Naglfar's Style Dictionary pipeline. Two things ship in this package:

- `Norse.DesignSystem.FluentTokenSeed` — `AccentBaseColor`/`NeutralBaseColor` constants, for FluentUI Blazor's `DesignTokens`. Consumed by `Norse.Infrastructure.Components.Theme.FluentUI` (Midgard).
- `_content/Norse.DesignSystem.Tokens/norse-design-tokens.css` — plain semantic color custom properties, switched by `@media (prefers-color-scheme: dark)`. No third-party dependency. Consumed by `Norse.Infrastructure.Components.Theme` (Midgard).

Nothing in this package is hand-authored. Do not edit `FluentTokenSeed.g.cs` or `norse-design-tokens.css` directly — edit `tokens/*.json` and run `npm run build`.
```

- [ ] **Step 5: Build and pack locally to verify the shape**

```bash
cd Naglfar
dotnet build src/DesignSystem.Tokens/DesignSystem.Tokens.csproj
dotnet pack src/DesignSystem.Tokens/DesignSystem.Tokens.csproj -o /tmp/norse-pack-check
unzip -l /tmp/norse-pack-check/Norse.DesignSystem.Tokens.*.nupkg | grep -E "FluentTokenSeed|norse-design-tokens.css"
```
Expected: the listing shows both `lib/net11.0/Norse.DesignSystem.Tokens.dll` (compiled seed) and `staticwebassets/norse-design-tokens.css` (or equivalent `build`/`buildTransitive` static-web-assets props referencing it — exact path depends on the SDK's current static-web-assets packing convention; the key check is that `norse-design-tokens.css` appears somewhere in the package listing).

- [ ] **Step 6: Stage and show the diff**

```bash
git add style-dictionary.config.js src/DesignSystem.Tokens/DesignSystem.Tokens.csproj src/DesignSystem.Tokens/README.md
git diff --cached --stat
```

---

## Task 5: Naglfar — CI publishes the NuGet package alongside npm, same tag

**Files:**
- Modify: `Naglfar/.github/workflows/release.yml`
- Modify: `Naglfar/.github/workflows/ci.yml`

**Interfaces:**
- Consumes: Task 4's packable project.
- Produces: `Norse.DesignSystem.Tokens` published to NuGet on every `v*.*.*` tag push, same version as the npm package (both derive from the same tag); triggers `sound-gjallarhorn` so Yggdrasil's `Directory.Packages.props` auto-bumps.

- [ ] **Step 1: Add the npm build step ahead of the dotnet pack in CI (`ci.yml`)**

Current `ci.yml` calls only the npm gate. Add a dotnet build smoke-check job so a broken `DesignSystem.Tokens.csproj` fails CI before it ever reaches a release tag:

```yaml
name: CI

on:
  pull_request:
    branches: [master]

permissions:
  packages: read

jobs:
  gate:
    uses: NorseArchitecture/.github/.github/workflows/ci-build-test-npm.yml@master
    secrets: inherit

  dotnet-build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - uses: actions/setup-node@v5
        with:
          node-version: 22
      - run: npm ci
      - run: npm run build
      - uses: actions/setup-dotnet@v5
        with:
          dotnet-version: "11.0.x"
          dotnet-quality: preview
      - run: dotnet build src/DesignSystem.Tokens/DesignSystem.Tokens.csproj
```

- [ ] **Step 2: Add the NuGet release job to `release.yml`, parallel to the existing npm job, plus `sound-gjallarhorn`**

Current:
```yaml
jobs:
  release:
    uses: NorseArchitecture/.github/.github/workflows/release-npm.yml@master
    secrets: inherit
```

Replace with:
```yaml
jobs:
  release-npm:
    uses: NorseArchitecture/.github/.github/workflows/release-npm.yml@master
    secrets: inherit

  release-nuget:
    uses: NorseArchitecture/.github/.github/workflows/release-nuget.yml@master
    secrets: inherit

  sound-gjallarhorn:
    needs: [release-nuget]
    uses: NorseArchitecture/.github/.github/workflows/sound-gjallarhorn.yml@master
    secrets:
      token: ${{ secrets.SCATTER_PAT }}
```

`release-nuget.yml`'s own `build-test` job calls `./.github/workflows/ci-build-test.yml` — Naglfar doesn't have that file (it only has `ci-build-test-npm.yml`'s caller). Confirm this reusable workflow's `build-test` job actually resolves for a repo with only one dotnet project and no `tests/` directory — if `ci-build-test.yml` hard-requires a test project to exist, that's a real gap to flag, not silently work around.

- [ ] **Step 3: Verify the reusable workflow's assumptions**

```bash
cat ../.github/.github/workflows/ci-build-test.yml
```
Read it and confirm it tolerates zero test projects (e.g., globs for `**/*.Tests.csproj` and no-ops if none match, rather than failing). If it hard-fails on zero test projects, stop here and flag it — do not silently add a placeholder test project just to satisfy the workflow.

- [ ] **Step 4: Stage and show the diff**

```bash
git add .github/workflows/release.yml .github/workflows/ci.yml
git diff --cached
```

---

## Task 6: Midgard — `Infrastructure.Components.Theme` (no third-party UI dependency)

**Files:**
- Create: `Midgard/src/Infrastructure.Components.Theme/Infrastructure.Components.Theme.csproj`
- Create: `Midgard/src/Infrastructure.Components.Theme/NorseThemeAssets.cs`
- Create: `Midgard/tests/Infrastructure.Components.Theme.Tests/Infrastructure.Components.Theme.Tests.csproj`
- Create: `Midgard/tests/Infrastructure.Components.Theme.Tests/NorseThemeAssetsTests.cs`
- Modify: `Midgard/Midgard.slnx`

**Interfaces:**
- Consumes: Task 4's `Norse.DesignSystem.Tokens` package (via `NorseRef`).
- Produces: `Norse.Infrastructure.Components.Theme.NorseThemeAssets.StylesheetPath` (`public const string`) — the well-known static-asset path every consuming host links, and what Task 7's project references transitively. Consumed directly by Task 10/11 (Yggdrasil hosts).

- [ ] **Step 1: Write the failing test**

`Midgard/tests/Infrastructure.Components.Theme.Tests/NorseThemeAssetsTests.cs`:
```csharp
using Norse.Infrastructure.Components.Theme;
using Shouldly;

namespace Norse.Infrastructure.Components.Theme.Tests;

public class NorseThemeAssetsTests
{
	[Fact]
	public void StylesheetPath_PointsAtDesignSystemTokensStaticWebAsset()
	{
		NorseThemeAssets.StylesheetPath.ShouldBe("_content/Norse.DesignSystem.Tokens/norse-design-tokens.css");
	}
}
```

`Midgard/tests/Infrastructure.Components.Theme.Tests/Infrastructure.Components.Theme.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="../../src/Infrastructure.Components.Theme/Infrastructure.Components.Theme.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Run it to confirm it fails**

```bash
cd Midgard
dotnet test tests/Infrastructure.Components.Theme.Tests/Infrastructure.Components.Theme.Tests.csproj
```
Expected: FAIL — `Infrastructure.Components.Theme.csproj` doesn't exist yet, build error.

- [ ] **Step 3: Create `Infrastructure.Components.Theme.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
	<PropertyGroup>
		<Description>Norse.Infrastructure.Components.Theme: the plain-CSS half of the platform's theme selection machinery. No third-party UI-library dependency — every headless component (Asgard's Loader, any headless markup in a realm's .Components project) implicitly depends on this via currentColor, without ever referencing it directly. See Norse.Infrastructure.Components.Theme.FluentUI for the FluentUI-specific sibling.</Description>
	</PropertyGroup>
	<ItemGroup>
		<NorseRef Include="DesignSystem.Tokens">
			<Repo>Naglfar</Repo>
		</NorseRef>
	</ItemGroup>
</Project>
```

- [ ] **Step 4: Create `NorseThemeAssets.cs`**

```csharp
namespace Norse.Infrastructure.Components.Theme;

/// <summary>
/// Well-known static-asset paths for the platform's theme selection machinery. Every Yggdrasil
/// host links <see cref="StylesheetPath"/> once, in its own root document — this is the one
/// place that path string is allowed to exist.
/// </summary>
public static class NorseThemeAssets
{
	public const string StylesheetPath = "_content/Norse.DesignSystem.Tokens/norse-design-tokens.css";
}
```

- [ ] **Step 5: Run the test to confirm it passes**

```bash
dotnet test tests/Infrastructure.Components.Theme.Tests/Infrastructure.Components.Theme.Tests.csproj
```
Expected: PASS.

- [ ] **Step 6: Wire both projects into `Midgard.slnx`**

Add under `/src/` and `/tests/` folders respectively, matching the existing `Infrastructure.Migrations` entries' shape:

```xml
	<Folder Name="/src/">
		<File Path="src/Directory.Build.props" />
		<File Path="src/Directory.Build.targets" />
		<Project Path="src/Infrastructure.Migrations/Infrastructure.Migrations.csproj" />
		<Project Path="src/Infrastructure.Components.Theme/Infrastructure.Components.Theme.csproj" />
	</Folder>
	<Folder Name="/tests/">
		<File Path="tests/Directory.Build.props" />
		<File Path="tests/Directory.Build.targets" />
		<Project Path="tests/Infrastructure.Migrations.Tests/Infrastructure.Migrations.Tests.csproj" />
		<Project Path="tests/Infrastructure.Components.Theme.Tests/Infrastructure.Components.Theme.Tests.csproj" />
	</Folder>
```

- [ ] **Step 7: Full solution build to confirm nothing else broke**

```bash
dotnet build Midgard.slnx
```
Expected: builds clean.

- [ ] **Step 8: Commit**

```bash
git add src/Infrastructure.Components.Theme tests/Infrastructure.Components.Theme.Tests Midgard.slnx
git commit -m "feat: add Infrastructure.Components.Theme, the plain-CSS half of theme selection"
```

---

## Task 7: Midgard — `Infrastructure.Components.Theme.FluentUI` (bootstraps FluentUI's DesignTokens)

**Files:**
- Create: `Midgard/src/Infrastructure.Components.Theme.FluentUI/Infrastructure.Components.Theme.FluentUI.csproj`
- Create: `Midgard/src/Infrastructure.Components.Theme.FluentUI/ServiceCollectionExtensions.cs`
- Create: `Midgard/src/Infrastructure.Components.Theme.FluentUI/NorseFluentDesignTheme.razor`
- Create: `Midgard/tests/Infrastructure.Components.Theme.FluentUI.Tests/Infrastructure.Components.Theme.FluentUI.Tests.csproj`
- Create: `Midgard/tests/Infrastructure.Components.Theme.FluentUI.Tests/ServiceCollectionExtensionsTests.cs`
- Modify: `Midgard/Midgard.slnx`

**Interfaces:**
- Consumes: Task 6's `Infrastructure.Components.Theme` project (`ProjectReference`), Task 4's `Norse.DesignSystem.Tokens` package (`NorseRef`, for `Norse.DesignSystem.FluentTokenSeed`).
- Produces: `Norse.Infrastructure.Components.Theme.FluentUI.ServiceCollectionExtensions.AddNorseFluentUiTheme(this IServiceCollection)` and the `<NorseFluentDesignTheme />` Razor component — both consumed by Task 10/11 (Yggdrasil hosts).

- [ ] **Step 1: Write the failing test**

`Midgard/tests/Infrastructure.Components.Theme.FluentUI.Tests/ServiceCollectionExtensionsTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Shouldly;

namespace Norse.Infrastructure.Components.Theme.FluentUI.Tests;

public class ServiceCollectionExtensionsTests
{
	[Fact]
	public void AddNorseFluentUiTheme_RegistersFluentUiGlobalState()
	{
		var services = new ServiceCollection();

		services.AddNorseFluentUiTheme();

		services.ShouldContain(d => d.ServiceType == typeof(GlobalState));
	}
}
```

`Midgard/tests/Infrastructure.Components.Theme.FluentUI.Tests/Infrastructure.Components.Theme.FluentUI.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="../../src/Infrastructure.Components.Theme.FluentUI/Infrastructure.Components.Theme.FluentUI.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Run it to confirm it fails**

```bash
dotnet test tests/Infrastructure.Components.Theme.FluentUI.Tests/Infrastructure.Components.Theme.FluentUI.Tests.csproj
```
Expected: FAIL — project doesn't exist yet.

- [ ] **Step 3: Create `Infrastructure.Components.Theme.FluentUI.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
	<PropertyGroup>
		<Description>Norse.Infrastructure.Components.Theme.FluentUI: bootstraps FluentUI Blazor's DesignTokens from Naglfar's generated token seed. AddNorseFluentUiTheme() registers FluentUI's services; NorseFluentDesignTheme wraps FluentDesignTheme with Mode="System" and the seeded accent/neutral colors. The only project in this pair that references FluentUI.</Description>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Microsoft.FluentUI.AspNetCore.Components" Version="*" />
	</ItemGroup>
	<ItemGroup>
		<ProjectReference Include="../Infrastructure.Components.Theme/Infrastructure.Components.Theme.csproj" />
	</ItemGroup>
	<ItemGroup>
		<NorseRef Include="DesignSystem.Tokens">
			<Repo>Naglfar</Repo>
		</NorseRef>
	</ItemGroup>
</Project>
```

- [ ] **Step 4: Create `ServiceCollectionExtensions.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Norse.Infrastructure.Components.Theme.FluentUI;

public static class ServiceCollectionExtensions
{
	public static IServiceCollection AddNorseFluentUiTheme(this IServiceCollection services) =>
		services.AddFluentUIComponents();
}
```

- [ ] **Step 5: Create `NorseFluentDesignTheme.razor`**

```razor
@using Microsoft.FluentUI.AspNetCore.Components
@using Norse.DesignSystem

<FluentDesignTheme Mode="DesignThemeModes.System"
                    CustomColor="@FluentTokenSeed.AccentBaseColor"
                    NeutralBaseColor="@FluentTokenSeed.NeutralBaseColor"
                    StorageName="norse-fluent-theme" />
```

- [ ] **Step 6: Run the test to confirm it passes**

```bash
dotnet test tests/Infrastructure.Components.Theme.FluentUI.Tests/Infrastructure.Components.Theme.FluentUI.Tests.csproj
```
Expected: PASS.

- [ ] **Step 7: Wire both projects into `Midgard.slnx`**

```xml
	<Folder Name="/src/">
		...
		<Project Path="src/Infrastructure.Components.Theme/Infrastructure.Components.Theme.csproj" />
		<Project Path="src/Infrastructure.Components.Theme.FluentUI/Infrastructure.Components.Theme.FluentUI.csproj" />
	</Folder>
	<Folder Name="/tests/">
		...
		<Project Path="tests/Infrastructure.Components.Theme.Tests/Infrastructure.Components.Theme.Tests.csproj" />
		<Project Path="tests/Infrastructure.Components.Theme.FluentUI.Tests/Infrastructure.Components.Theme.FluentUI.Tests.csproj" />
	</Folder>
```

- [ ] **Step 8: Full solution build**

```bash
dotnet build Midgard.slnx
```
Expected: builds clean.

- [ ] **Step 9: Commit**

```bash
git add src/Infrastructure.Components.Theme.FluentUI tests/Infrastructure.Components.Theme.FluentUI.Tests Midgard.slnx
git commit -m "feat: add Infrastructure.Components.Theme.FluentUI, bootstraps FluentUI DesignTokens from Naglfar's seed"
```

---

## Task 8: Midgard — CLAUDE.md reflects the converged theming slice

**Files:**
- Modify: `Midgard/CLAUDE.md`

- [ ] **Step 1: Update the bare-shell language**

Current (`Midgard/CLAUDE.md`, in the realm-overview paragraph):
> Everything else in this realm — the `DbContext` family, repository implementations, the mediator runtime, UI composition — is still a bare shell; no other specs have converged here yet.

Replace with:
> Everything else in this realm — the `DbContext` family, repository implementations, the mediator runtime — is still a bare shell; no other specs have converged here yet. **`Infrastructure.Components.Theme`/`.Theme.FluentUI` are live** — the first slice of "UI composition": app-wide theme bootstrapping, seeded from Naglfar's generated token package (`../Glitnir/docs/Platform/specs/2026-07-11-blazor-component-architecture-design.md`, Addendum 2026-07-12). The other half of "UI composition" — the mechanism that composes N registered dashboard widgets into a rendered, user-arranged layout — remains unconverged.

- [ ] **Step 2: Stage and show the diff**

```bash
git add CLAUDE.md
git diff --cached
```

---

## Task 9: Yggdrasil — seed the two new `PackageVersion` entries

**Files:**
- Modify: `Yggdrasil/Directory.Packages.props`

**Interfaces:**
- Consumes: nothing from other tasks (can run any time after Task 7 lands the projects it names, but the file edit itself has no code dependency).
- Produces: `$(MidgardVersion)`-pinned versions for `Norse.Infrastructure.Components.Theme` and `Norse.Infrastructure.Components.Theme.FluentUI` — required before Task 10/11's `NorseRef` items can resolve in package mode (local Bifrost dev resolves via `ProjectReference` regardless, per `UseProjectReferences=true`, but the CPM entries must still exist or restore fails outside Bifrost).

- [ ] **Step 1: Add the two entries, alongside the existing `Norse.Infrastructure.Migrations` line**

Current (`Yggdrasil/Directory.Packages.props`):
```xml
		<PackageVersion Include="Norse.Infrastructure.Migrations" Version="$(MidgardVersion)" />
```

Replace with:
```xml
		<PackageVersion Include="Norse.Infrastructure.Components.Theme" Version="$(MidgardVersion)" />
		<PackageVersion Include="Norse.Infrastructure.Components.Theme.FluentUI" Version="$(MidgardVersion)" />
		<PackageVersion Include="Norse.Infrastructure.Migrations" Version="$(MidgardVersion)" />
```

(Alphabetical order, matching every other entry in this file.)

- [ ] **Step 2: Stage and show the diff**

```bash
git add Directory.Packages.props
git diff --cached
```

---

## Task 10: Yggdrasil — wire `Hosting.Stories.Client`

**Files:**
- Modify: `Yggdrasil/src/Hosting.Stories.Client/Hosting.Stories.Client.csproj`
- Modify: `Yggdrasil/src/Hosting.Stories.Client/Program.cs`
- Modify: `Yggdrasil/src/Hosting.Stories.Client/App.razor`
- Modify: `Yggdrasil/src/Hosting.Stories.Client/wwwroot/index.html`
- Modify: `Yggdrasil/src/Hosting.Stories.Client/wwwroot/iframe.html`

**Interfaces:**
- Consumes: Task 6's `NorseThemeAssets.StylesheetPath`, Task 7's `AddNorseFluentUiTheme()`/`<NorseFluentDesignTheme />`, Task 9's CPM entries.
- Produces: nothing further downstream — this is a leaf host.

- [ ] **Step 1: Add the `NorseRef` to `Hosting.Stories.Client.csproj`**

Current:
```xml
	<ItemGroup>
		<PackageReference Include="BlazingStory" />
		<PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" />
		<PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.DevServer" PrivateAssets="all" />
	</ItemGroup>
```
Add a second `ItemGroup` after it:
```xml
	<ItemGroup>
		<NorseRef Include="Infrastructure.Components.Theme.FluentUI">
			<Repo>Midgard</Repo>
		</NorseRef>
	</ItemGroup>
```

- [ ] **Step 2: Register the service in `Program.cs`**

Current:
```csharp
using Norse.Hosting.Stories.Client;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Build().RunAsync().ConfigureAwait(false);
```
Replace with:
```csharp
using Norse.Hosting.Stories.Client;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Norse.Infrastructure.Components.Theme.FluentUI;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddNorseFluentUiTheme();

await builder.Build().RunAsync().ConfigureAwait(false);
```

- [ ] **Step 3: Wrap the root component in `App.razor`**

Current:
```razor
<BlazingStoryApp Assemblies="[typeof(App).Assembly, typeof(AssemblyMarker).Assembly]" DefaultLayout="typeof(DefaultLayout)" />
```
Replace with:
```razor
@using Norse.Infrastructure.Components.Theme.FluentUI

<NorseFluentDesignTheme />
<BlazingStoryApp Assemblies="[typeof(App).Assembly, typeof(AssemblyMarker).Assembly]" DefaultLayout="typeof(DefaultLayout)" />
```

- [ ] **Step 4: Link the plain-CSS stylesheet in both HTML documents**

Both `index.html` and `iframe.html` are static (non-Razor) documents — the `NorseThemeAssets.StylesheetPath` constant can't be evaluated here, so the literal path is hardcoded with a comment pointing at its source of truth. In `wwwroot/index.html`, add right after the existing `<link rel="stylesheet" href="css/blazor-ui.css" />` line:
```html
    <!-- Path defined in Norse.Infrastructure.Components.Theme.NorseThemeAssets.StylesheetPath -->
    <link rel="stylesheet" href="_content/Norse.DesignSystem.Tokens/norse-design-tokens.css" />
```
Do the same in `wwwroot/iframe.html`, after its existing `<link rel="stylesheet" href="css/blazor-ui.css" />` line.

- [ ] **Step 5: Build to confirm it compiles**

```bash
dotnet build src/Hosting.Stories.Client/Hosting.Stories.Client.csproj
```
Expected: builds clean.

- [ ] **Step 6: Manual verification — run the host, flip OS dark mode, reload**

```bash
dotnet run --project src/Hosting.Stories.Server
```
Open the printed URL, confirm the page renders. Flip the OS/browser color-scheme preference, reload, and confirm both BlazingStory's own chrome and the Loader spinner (visible while the WASM payload loads) stay legible in both themes.

- [ ] **Step 7: Commit**

```bash
git add src/Hosting.Stories.Client
git commit -m "feat: wire NorseFluentDesignTheme into Hosting.Stories.Client"
```

---

## Task 11: Yggdrasil — wire `Hosting.Web.Client` and `Hosting.Web.Server`

**Files:**
- Modify: `Yggdrasil/src/Hosting.Web.Client/Hosting.Web.Client.csproj`
- Modify: `Yggdrasil/src/Hosting.Web.Client/Program.cs`
- Modify: `Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj`
- Modify: `Yggdrasil/src/Hosting.Web.Server/Program.cs`
- Modify: `Yggdrasil/src/Hosting.Web.Server/Components/App.razor`

**Interfaces:**
- Consumes: Task 6/7/9, same as Task 10.
- Produces: nothing further downstream.

This host pair is a **Blazor Web App** (server-rendered `App.razor`, `InteractiveServer` + `InteractiveWebAssembly` render modes) — unlike Stories' standalone WASM shape, both `Program.cs` files independently call `AddFluentUIComponents()` today (raw, no tokens), and only `Hosting.Web.Server`'s `Components/App.razor` is the actual rendered root.

- [ ] **Step 1: Add the `NorseRef` to both csproj files**

`Hosting.Web.Client.csproj` — add after the existing `ItemGroup`:
```xml
	<ItemGroup>
		<NorseRef Include="Infrastructure.Components.Theme.FluentUI">
			<Repo>Midgard</Repo>
		</NorseRef>
	</ItemGroup>
```

`Hosting.Web.Server.csproj` — same addition, after its existing `ItemGroup`:
```xml
	<ItemGroup>
		<NorseRef Include="Infrastructure.Components.Theme.FluentUI">
			<Repo>Midgard</Repo>
		</NorseRef>
	</ItemGroup>
```

- [ ] **Step 2: Replace the raw `AddFluentUIComponents()` call in `Hosting.Web.Client/Program.cs`**

Current:
```csharp
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;

// ... architecture note unchanged ...

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services
	.AddAuthorizationCore()
	.AddCascadingAuthenticationState()
	.AddAuthenticationStateDeserialization()
	.AddFluentUIComponents();

await builder
	.Build()
	.RunAsync()
	.ConfigureAwait(false);
```
Replace the `using` and the chained call:
```csharp
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Norse.Infrastructure.Components.Theme.FluentUI;

// ... architecture note unchanged ...

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services
	.AddAuthorizationCore()
	.AddCascadingAuthenticationState()
	.AddAuthenticationStateDeserialization()
	.AddNorseFluentUiTheme();

await builder
	.Build()
	.RunAsync()
	.ConfigureAwait(false);
```

- [ ] **Step 3: Replace the raw `AddFluentUIComponents()` call in `Hosting.Web.Server/Program.cs`**

Current line:
```csharp
builder.Services.AddFluentUIComponents();
```
Replace with:
```csharp
builder.Services.AddNorseFluentUiTheme();
```
Add the `using` alongside the existing `Microsoft.FluentUI.AspNetCore.Components` one (keep both — this file still uses other FluentUI types elsewhere? Confirm by building; if `Microsoft.FluentUI.AspNetCore.Components` becomes unused after this change, remove that `using` line):
```csharp
using Norse.Infrastructure.Components.Theme.FluentUI;
```

- [ ] **Step 4: Link the stylesheet and wrap the render root in `Components/App.razor`**

Current `<head>`:
```razor
    <link rel="stylesheet" href="@Assets["_content/Norse.Hosting.Web.Components/app.css"]" />
    <link rel="stylesheet" href="@Assets["Norse.Hosting.Web.Server.styles.css"]" />
```
Add, right after:
```razor
    <link rel="stylesheet" href="_content/Norse.DesignSystem.Tokens/norse-design-tokens.css" />
```

Current `<body>`:
```razor
    <Routes @rendermode="PageRenderMode" />
```
Replace with:
```razor
    <NorseFluentDesignTheme />
    <Routes @rendermode="PageRenderMode" />
```

Add the `@using` at the top of the file (alongside the existing implicit-usings — check `_Imports.razor` first; if `Norse.Infrastructure.Components.Theme.FluentUI` isn't already in scope, add `@using Norse.Infrastructure.Components.Theme.FluentUI` to `Components/_Imports.razor` rather than to `App.razor` directly, matching how this project's other global usings are organized).

- [ ] **Step 5: Build both projects**

```bash
dotnet build src/Hosting.Web.Client/Hosting.Web.Client.csproj
dotnet build src/Hosting.Web.Server/Hosting.Web.Server.csproj
```
Expected: both build clean. If `Hosting.Web.Server/Program.cs` shows an unused-`using` warning for `Microsoft.FluentUI.AspNetCore.Components`, remove that line (Step 3 note).

- [ ] **Step 6: Manual verification — run the host, flip OS dark mode, reload**

```bash
dotnet run --project src/Hosting.Web.Server
```
Open the printed URL, confirm the page renders (Login page or equivalent — this host still carries the prior-art Identity scaffold, unrelated to this task). Flip the OS/browser color-scheme preference, reload, confirm the page stays legible in both themes.

- [ ] **Step 7: Commit**

```bash
git add src/Hosting.Web.Client src/Hosting.Web.Server
git commit -m "feat: replace raw AddFluentUIComponents with AddNorseFluentUiTheme in Hosting.Web.Client/.Server"
```

---

## Task 12: Yggdrasil — CLAUDE.md reflects the new dependency

**Files:**
- Modify: `Yggdrasil/CLAUDE.md`

- [ ] **Step 1: Add the Midgard theming dependency to §1's realm description**

Locate the sentence naming what Yggdrasil rides on ("In the dependency chain it rides on Midgard and everything below") and add a trailing clause noting `Infrastructure.Components.Theme.FluentUI` is now a live, referenced dependency of `Hosting.Stories.Client`, `Hosting.Web.Client`, and `Hosting.Web.Server` — not just an abstract "rides on Midgard" statement.

- [ ] **Step 2: Stage and show the diff**

```bash
git add CLAUDE.md
git diff --cached
```

---

## Task 13: Doc sync — the three "Naglfar is npm-only, no .NET" claims

**Files:**
- Modify: `Bifrost/CLAUDE.md`
- Modify: `Naglfar/README.md`
- Modify: `Bragi/CLAUDE.md`

- [ ] **Step 1: `Bifrost/CLAUDE.md` §2 naming table, Naglfar row**

Find the cell reading:
> The token pipeline (`@norsearchitecture/design-tokens`, Style Dictionary) only. **npm-only, no .NET**...

Append: "— narrowed 2026-07-12 to 'no hand-authored C#': `DesignSystem.Tokens` is a single 100%-generated .NET package (`FluentTokenSeed` + `norse-design-tokens.css`), packed alongside the npm package in the same release step."

- [ ] **Step 2: `Naglfar/README.md`**

Find:
> **Naglfar is now JS-only.**

Replace with:
> **Naglfar is JS-first, with one 100%-generated .NET exception.** `DesignSystem.Tokens` packs the pipeline's C# seed (`FluentTokenSeed`) and CSS static asset as `Norse.DesignSystem.Tokens`, versioned identically to `@norsearchitecture/design-tokens` — no hand-authored C# anywhere in this repo.

- [ ] **Step 3: `Bragi/CLAUDE.md`**

Find:
> Naglfar keeps the npm/Style Dictionary token pipeline only — no .NET at all.

Replace with:
> Naglfar keeps the npm/Style Dictionary token pipeline, plus one 100%-generated .NET package (`DesignSystem.Tokens`, added 2026-07-12) — no hand-authored C# in either repo.

- [ ] **Step 4: Stage and show the diff across all three**

```bash
git -C Bifrost add CLAUDE.md
git -C Naglfar add README.md
git -C Bragi add CLAUDE.md
git -C Bifrost diff --cached
git -C Naglfar diff --cached
git -C Bragi diff --cached
```

---

## Self-Review

**Spec coverage:** Decision 1 (§1.2 `.Components` amendment) — not implemented here; it's a documentation-only decision about Heimdall/Mimir, both still bare shells with no code to touch yet, correctly out of this plan's scope. Decision 2 (Naglfar packs both, same version) — Tasks 4–5. Decision 3 (media query) — Task 3. Decision 4 (`Infrastructure.Components.Theme`/`.Theme.FluentUI`) — Tasks 6–7. Decision 5 (both hosts wire it) — Tasks 10–11. Naming rationale (why not `.Components.FluentUI`) — reflected in every `Description` property written. Documentation Consequences table — Tasks 8, 12, 13 cover all five listed documents. Success Criterion bullets — the package/version shape (Task 4/5/9), the media query (Task 3), zero third-party dependency in `Infrastructure.Components.Theme` (Task 6's csproj has no `PackageReference` at all), the OS-preference-driven verification (Task 10 Step 6, Task 11 Step 6), and "Asgard's `Loader.razor` unchanged" (no task in this plan touches `Asgard/` at all — confirmed by the Global Constraints line).

**Placeholder scan:** No TBDs. Task 5 Step 3 is the one step that says "read this and confirm an assumption" rather than a fixed action — that's intentional (a real unresolved risk about `ci-build-test.yml`'s test-project assumption, flagged explicitly rather than guessed past) and matches the "no silent fallback" platform principle rather than being a placeholder.

**Type consistency:** `AddNorseFluentUiTheme()` (Task 7 Step 4) is the exact name used in every consuming task (10 Step 2, 11 Steps 2–3). `NorseFluentDesignTheme` (Task 7 Step 5) matches its usage in Task 10 Step 3 and Task 11 Step 4. `NorseThemeAssets.StylesheetPath` (Task 6 Step 4) matches its referenced value (as a literal, since HTML/Razor markup can't share the C# constant across the WASM-boundary-crossing static HTML files) in Task 10 Step 4 and Task 11 Step 4 — the literal string `_content/Norse.DesignSystem.Tokens/norse-design-tokens.css` is identical in the constant and both hardcoded copies.

---

**Plan complete and saved to `Glitnir/docs/Platform/plans/2026-07-12-theme-selection-machinery.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

**Which approach?**
