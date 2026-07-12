# DesignSystem.Stories Hosting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split `DesignSystem.Stories` into a content-only RCL in Naglfar and a runnable, dockerizable WASM host in Yggdrasil, ship the host's container image to GHCR, and wire up the freshness loop so a new component version reaches the hosted catalog with no manual redeploy step.

**Architecture:** Naglfar's `DesignSystem.Stories` becomes `Microsoft.NET.Sdk.Razor` — `.stories.razor` files and generated markdown pages only, no runnable shell. Yggdrasil gains `Hosting.Stories.Client` (`Microsoft.NET.Sdk.BlazorWebAssembly` — the WASM bootstrap, `NorseRef`'s Naglfar's RCL) and `Hosting.Stories.Server` (`Microsoft.NET.Sdk.Web` — `ProjectReference`s the Client, serves its static output, gets dockerized). Ginnungagap's shared `release-container.yml` gains a fourth image block for `Hosting.Stories.Server`, publishing to `ghcr.io/norsearchitecture/hosting/stories`. Naglfar's existing `release.yml` → Gjallarhorn plumbing needs one seeded property in Yggdrasil's `Directory.Packages.props` to start working for this realm.

**Tech Stack:** .NET 11 (preview), Blazor WebAssembly, BlazingStory 1.0.0-preview.88, xUnit v3 (Microsoft.Testing.Platform) + Shouldly, GitHub Actions (Ginnungagap reusable workflows).

## Global Constraints

- **No automatic git commits** in any of the three repos touched here (Naglfar, Yggdrasil, Ginnungagap `.github`) — stage and show the diff; the human commits. (Bifrost CLAUDE.md §6, Glitnir CLAUDE.md §8.)
- **Cross-repo sequencing:** Naglfar's `DesignSystem.Stories` must be tagged and published to NuGet (Task 1's deliverable, tagged by the human afterward) *before* Yggdrasil's `Hosting.Stories.Client`/`.Server` PR can pass Yggdrasil's own standalone CI — that CI checks out only Yggdrasil, so it resolves `Norse.DesignSystem.Stories` as a real NuGet package, which won't exist until Naglfar's first release ships. Building and testing everything from *this* Bifröst checkout (submodules present) works throughout regardless, because `NorseRef` resolves to `ProjectReference` locally (`UseProjectReferences=true` at Bifröst's root) — that's the environment every verification step below uses. This is a known, expected gap, not a plan defect.
- **CPM (Central Package Management)** is `true` at Yggdrasil's `src/` root and `false` under `tests/` — `src/` package references carry no `Version` attribute (versions live in `Directory.Packages.props`); `tests/` package references use a plain `Version="..."` attribute directly.
- **Naglfar has no CPM** — its package references use an explicit `Version` attribute, in the floating `major.*-*` style already used throughout `DesignSystem.Stories.csproj`.
- **`sealed`/`internal` by default, `omit_if_default` accessibility** — apply throughout; new types below follow this.
- Version pins used below: `DotNetVersion = 11.0.0-preview.5.26302.115` (already in Yggdrasil's `Directory.Packages.props`), `BlazingStoryVersion = 1.0.0-preview.88` (new).

---

### Task 1: Naglfar — restructure `DesignSystem.Stories` into a content-only RCL

**Files:**
- Modify: `Naglfar/src/DesignSystem.Stories/DesignSystem.Stories.csproj`
- Modify: `Naglfar/src/DesignSystem.Stories/_Imports.razor`
- Create: `Naglfar/src/DesignSystem.Stories/AssemblyMarker.cs`
- Delete: `Naglfar/src/DesignSystem.Stories/Program.cs`
- Delete: `Naglfar/src/DesignSystem.Stories/App.razor`
- Delete: `Naglfar/src/DesignSystem.Stories/Shared/DefaultLayout.razor` (and the now-empty `Shared/` directory)
- Delete: `Naglfar/src/DesignSystem.Stories/wwwroot/` (entire directory — `index.html`, `iframe.html`, `css/blazor-ui.css`, `favicon.ico`)
- Delete: `Naglfar/src/DesignSystem.Stories/Properties/launchSettings.json` (and the now-empty `Properties/` directory)
- Unchanged (verify still present, do not touch): `Naglfar/src/DesignSystem.Stories/Stories/Loader.stories.razor`, `Naglfar/src/DesignSystem.Stories/Welcome.md`

**Interfaces:**
- Produces: `Norse.DesignSystem.Stories.AssemblyMarker` — the anchor type Task 2's `App.razor` uses to locate this assembly via `typeof(AssemblyMarker).Assembly`, without guessing the compiler-generated type name for `Loader.stories.razor`.
- Produces: `Norse.DesignSystem.Stories` as a packable NuGet id (`Norse.DesignSystem.Stories`), consumed by Task 2 via `NorseRef`.

- [ ] **Step 1: Move the hosting-shell files out, delete the rest**

```bash
rm Naglfar/src/DesignSystem.Stories/Program.cs
rm Naglfar/src/DesignSystem.Stories/App.razor
rm -rf Naglfar/src/DesignSystem.Stories/Shared
rm -rf Naglfar/src/DesignSystem.Stories/wwwroot
rm -rf Naglfar/src/DesignSystem.Stories/Properties
```

(Task 2 recreates equivalent files under `Yggdrasil/src/Hosting.Stories.Client/` — this step is a straight delete here, not a move across repos.)

- [ ] **Step 2: Add the assembly anchor type**

```csharp
namespace Norse.DesignSystem.Stories;

/// <summary>Anchor type for locating this assembly by reflection — BlazingStory's <c>Assemblies</c> parameter takes a list of <see cref="System.Reflection.Assembly"/> instances, and this avoids depending on the compiler-generated type name for a <c>.stories.razor</c> file.</summary>
static class AssemblyMarker
{
}
```

Save this as `Naglfar/src/DesignSystem.Stories/AssemblyMarker.cs`.

- [ ] **Step 3: Trim `_Imports.razor` to drop what left with the deleted files**

Replace the full contents of `Naglfar/src/DesignSystem.Stories/_Imports.razor` with:

```razor
@using System.Net.Http
@using System.Net.Http.Json
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using Microsoft.JSInterop
@using BlazingStory.Components
@using BlazingStory.Components.Layouts
@using BlazingStory.Configurations
@using BlazingStory.Types
@using Norse.DesignSystem.Stories
@using Norse.DesignSystem.Stories.Stories
```

(Dropped `@using Microsoft.AspNetCore.Components.WebAssembly.Http` — that type comes from the `Microsoft.AspNetCore.Components.WebAssembly` package, which no longer belongs on this project per Step 4. Dropped `@using Norse.DesignSystem.Stories.Shared` — `DefaultLayout` no longer lives here.)

- [ ] **Step 4: Rewrite the csproj as a plain Razor Class Library**

Replace the full contents of `Naglfar/src/DesignSystem.Stories/DesignSystem.Stories.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">

	<PropertyGroup>
		<MD2RazorDefaultBaseClass>global::BlazingStory.Internals.Pages.MarkdownPageBase</MD2RazorDefaultBaseClass>
	</PropertyGroup>

	<ItemGroup>
		<PackageReference Include="Microsoft.AspNetCore.Components.Web" Version="11.*-*" />
		<PackageReference Include="BlazingStory" Version="1.*-*" />
		<PackageReference Include="MD2RazorGenerator" Version="1.*">
			<PrivateAssets>all</PrivateAssets>
			<IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
		</PackageReference>
	</ItemGroup>

	<ItemGroup>
		<NorseRef Include="Abstractions.Components">
			<Repo>Asgard</Repo>
		</NorseRef>
	</ItemGroup>

</Project>
```

This drops `OverrideHtmlAssetPlaceholders` (no `wwwroot/*.html` left to override), the `IsPackable=false` property and its comment (a plain RCL packs by the platform's existing default — see `src/Directory.Build.props`'s `PackageId`/pack wiring, which every other `/src` project already relies on), the `Microsoft.AspNetCore.Components.WebAssembly`/`.DevServer` package references (WASM-host-specific, moved to Task 2), and the `RestoreBlazorWebAssemblyOutputType` target (a plain RCL wants `OutputType=Library`, which is already `src/Directory.Build.targets`'s unconditional default — no override needed).

- [ ] **Step 5: Build and pack to verify the restructure**

Run: `dotnet build Naglfar/src/DesignSystem.Stories/DesignSystem.Stories.csproj -c Release`
Expected: Build succeeds with zero errors. (If `Microsoft.AspNetCore.Components.Web.Virtualization` or `Forms`/`Routing` types fail to resolve, the explicit `Microsoft.AspNetCore.Components.Web` reference added in Step 4 is what covers them — check that reference is present before investigating further.)

Run: `dotnet pack Naglfar/src/DesignSystem.Stories/DesignSystem.Stories.csproj -c Release -o /tmp/naglfar-pack-check`
Expected: Produces `/tmp/naglfar-pack-check/Norse.DesignSystem.Stories.0.0.1-*.nupkg` (or similar, per MinVer's versioning from git state) with no pack errors — this is DesignSystem.Stories' first-ever successful pack, confirming `IsPackable` really is `true` by default now.

- [ ] **Step 6: Stage**

```bash
git -C Naglfar add src/DesignSystem.Stories
git -C Naglfar status --short
```

Show the diff to the human. Do not commit.

---

### Task 2: Yggdrasil — `Hosting.Stories.Client` (WASM bootstrap)

**Files:**
- Create: `Yggdrasil/src/Hosting.Stories.Client/Hosting.Stories.Client.csproj`
- Create: `Yggdrasil/src/Hosting.Stories.Client/Program.cs`
- Create: `Yggdrasil/src/Hosting.Stories.Client/_Imports.razor`
- Create: `Yggdrasil/src/Hosting.Stories.Client/App.razor`
- Create: `Yggdrasil/src/Hosting.Stories.Client/Shared/DefaultLayout.razor`
- Create: `Yggdrasil/src/Hosting.Stories.Client/wwwroot/index.html`
- Create: `Yggdrasil/src/Hosting.Stories.Client/wwwroot/iframe.html`
- Create: `Yggdrasil/src/Hosting.Stories.Client/wwwroot/css/blazor-ui.css` (copy verbatim from the deleted `Naglfar/src/DesignSystem.Stories/wwwroot/css/blazor-ui.css` — read its current content before deleting it in Task 1, or restore via `git show` if Task 1 already ran)
- Create: `Yggdrasil/src/Hosting.Stories.Client/wwwroot/favicon.ico` (copy verbatim from the deleted `Naglfar/src/DesignSystem.Stories/wwwroot/favicon.ico`, same caveat)
- Modify: `Yggdrasil/Directory.Packages.props`

**Interfaces:**
- Consumes: `Norse.DesignSystem.Stories.AssemblyMarker` (Task 1) — referenced as `typeof(AssemblyMarker).Assembly` in `App.razor`.
- Produces: `Norse.Hosting.Stories.Client.App` (root component, mounted at `#app`) and the project's compiled static web assets (`wwwroot` output + `_framework`), consumed by Task 3's `Hosting.Stories.Server` via `ProjectReference`.

- [ ] **Step 1: Seed `NaglfarVersion` in Yggdrasil's CPM file**

In `Yggdrasil/Directory.Packages.props`, in the "Norse versions" group, insert alphabetically between `<MimisbrunnrVersion>` and `<RatatoskrVersion>`:

```xml
		<NaglfarVersion>0.0.0</NaglfarVersion>
```

In the "3rd party versions" group, insert alphabetically before `<ContainerVersion>`:

```xml
		<BlazingStoryVersion>1.0.0-preview.88</BlazingStoryVersion>
```

In the `<ItemGroup>` of `<PackageVersion>` entries, add two new lines (placement doesn't need to be alphabetical — the file isn't currently sorted that way, just append near related entries):

```xml
		<PackageVersion Include="BlazingStory" Version="$(BlazingStoryVersion)" />
		<PackageVersion Include="Microsoft.AspNetCore.Components.WebAssembly.DevServer" Version="$(DotNetVersion)" />
		<PackageVersion Include="Norse.DesignSystem.Stories" Version="$(NaglfarVersion)" />
```

(`Microsoft.AspNetCore.Components.WebAssembly` and `.WebAssembly.Server` already have `PackageVersion` entries — do not duplicate them.)

- [ ] **Step 2: Create the project file**

`Yggdrasil/src/Hosting.Stories.Client/Hosting.Stories.Client.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">

	<PropertyGroup>
		<OverrideHtmlAssetPlaceholders>true</OverrideHtmlAssetPlaceholders>
	</PropertyGroup>

	<ItemGroup>
		<PackageReference Include="BlazingStory" />
		<PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" />
		<PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.DevServer" PrivateAssets="all" />
	</ItemGroup>

	<ItemGroup>
		<NorseRef Include="DesignSystem.Stories">
			<Repo>Naglfar</Repo>
		</NorseRef>
	</ItemGroup>

	<!--
		src/Directory.Build.targets hard-codes OutputType=Library for every /src project — true for
		every other Norse.* class library, but this is a runnable BlazingStory WASM host, matching
		the same override Hosting.Migrations.Service and (formerly) Naglfar's DesignSystem.Stories
		needed for their own genuinely different project shapes.
	-->
	<Target Name="RestoreBlazorWebAssemblyOutputType" BeforeTargets="CoreCompile;Publish;GetTargetPath">
		<PropertyGroup>
			<OutputType>Exe</OutputType>
		</PropertyGroup>
	</Target>

</Project>
```

- [ ] **Step 3: Create `Program.cs`**

`Yggdrasil/src/Hosting.Stories.Client/Program.cs`:

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

- [ ] **Step 4: Create `_Imports.razor`**

`Yggdrasil/src/Hosting.Stories.Client/_Imports.razor`:

```razor
@using System.Net.Http
@using System.Net.Http.Json
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using Microsoft.AspNetCore.Components.WebAssembly.Http
@using Microsoft.JSInterop
@using BlazingStory.Components
@using BlazingStory.Components.Layouts
@using BlazingStory.Configurations
@using BlazingStory.Types
@using Norse.DesignSystem.Stories
@using Norse.Hosting.Stories.Client.Shared
```

- [ ] **Step 5: Create `App.razor`, updated to scan both this assembly and Naglfar's RCL**

`Yggdrasil/src/Hosting.Stories.Client/App.razor`:

```razor
<BlazingStoryApp Assemblies="[typeof(App).Assembly, typeof(AssemblyMarker).Assembly]" DefaultLayout="typeof(DefaultLayout)" />
```

- [ ] **Step 6: Create `Shared/DefaultLayout.razor`**

`Yggdrasil/src/Hosting.Stories.Client/Shared/DefaultLayout.razor`:

```razor
@inherits LayoutComponentBase
@* See also: https://blazingstory.github.io/docs/configure-layouts/ *@
@Body
```

- [ ] **Step 7: Create `wwwroot/index.html`**

`Yggdrasil/src/Hosting.Stories.Client/wwwroot/index.html`:

```html
<!DOCTYPE html>
<html lang="en">

<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>DesignSystem.Stories</title>
    <base href="/" />
    <link rel="preload" id="webassembly" />
    <!--
    DON'T PUT ANY ADDITIONAL CSS OR JAVASCRIPT LINKS IN THIS FILE.
    Please do that in the "iframe.html" instead.
    -->
    <link rel="stylesheet" href="css/blazor-ui.css" />
    <script type="importmap"></script>
</head>

<body>
    <div id="app">
        <div class="loading-progress">
            <svg>
                <circle r="40%" cx="50%" cy="50%" />
                <circle r="40%" cx="50%" cy="50%" />
            </svg>
            <div class="text"></div>
            <img src="_content/BlazingStory/images/icon.min.svg" />
        </div>
    </div>

    <div id="blazor-error-ui">
        An unhandled error has occurred.
        <a href="" class="reload">Reload</a>
        <a class="dismiss">🗙</a>
    </div>

    <!--
    DON'T PUT ANY ADDITIONAL JAVASCRIPT LINKS IN THIS FILE.
    Please do that in the "iframe.html" instead.
    -->
    <script src="_framework/blazor.webassembly#[.{fingerprint}].js"></script>
</body>

</html>
```

- [ ] **Step 8: Create `wwwroot/iframe.html`**

`Yggdrasil/src/Hosting.Stories.Client/wwwroot/iframe.html`:

```html
<!DOCTYPE html>
<html lang="en">

<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>DesignSystem.Stories</title>
    <base href="/" />
    <link rel="preload" id="webassembly" />
    <!--
    If you need to add <link> or <script> elements to include CSS
    or JavaScript files for canvas views of your Stories, 
    YOU SHOULD PLACE THEM HERE, not in the "index.html" file.
    -->
    <link rel="stylesheet" href="css/blazor-ui.css" />
    <link rel="stylesheet" href="Hosting.Stories.Client.styles.css" />
    <script type="importmap"></script>
</head>

<body>
    <div id="app">
    </div>

    <div id="blazor-error-ui">
        An unhandled error has occurred.
        <a href="" class="reload">Reload</a>
        <a class="dismiss">🗙</a>
    </div>

    <!--
    If you need to add <script> elements to include 
    JavaScript files for canvas views of your Stories, 
    YOU SHOULD PLACE THEM HERE, not in the "index.html" file.
    -->
    <script src="_framework/blazor.webassembly#[.{fingerprint}].js"></script>
</body>

</html>
```

(Note the scoped-CSS link changed from `DesignSystem.Stories.styles.css` to `Hosting.Stories.Client.styles.css` — Blazor's scoped-CSS bundle filename tracks the project name, which changed.)

- [ ] **Step 9: Copy `css/blazor-ui.css` and `favicon.ico` byte-for-byte**

```bash
cp Naglfar/src/DesignSystem.Stories/wwwroot/css/blazor-ui.css Yggdrasil/src/Hosting.Stories.Client/wwwroot/css/blazor-ui.css
cp Naglfar/src/DesignSystem.Stories/wwwroot/favicon.ico Yggdrasil/src/Hosting.Stories.Client/wwwroot/favicon.ico
```

Run this **before** Task 1 Step 1 deletes the source files, or restore them first with `git -C Naglfar show HEAD:src/DesignSystem.Stories/wwwroot/css/blazor-ui.css` / `...favicon.ico` if Task 1 already ran.

- [ ] **Step 10: Build**

Run: `dotnet build Yggdrasil/src/Hosting.Stories.Client/Hosting.Stories.Client.csproj -c Release`
Expected: Build succeeds with zero errors. `NorseRef` resolves to a `ProjectReference` against `Naglfar/src/DesignSystem.Stories/DesignSystem.Stories.csproj` (Bifröst root sets `UseProjectReferences=true`), so this build exercises Task 1's restructure directly — a failure here can mean either project has a problem.

- [ ] **Step 11: Run and verify BlazingStory discovers the relocated story — this is the spec's named risk, verify it directly**

Run: `dotnet run --project Yggdrasil/src/Hosting.Stories.Client --urls http://localhost:5299 &` (background it, note the PID)

Wait for the dev server to report it's listening, then:

Run: `curl -sf http://localhost:5299/ | grep -o '_framework/blazor.webassembly'`
Expected: prints `_framework/blazor.webassembly` — confirms the static shell is served (this alone does not prove the story renders; WASM execution happens client-side, past what `curl` can observe).

**Manual verification (required — do not skip):** open `http://localhost:5299/` in an actual browser. Confirm BlazingStory's sidebar shows "Primitives/Loader" and that clicking into it renders both the "Default" and "Custom Label" story variants without a console error about the story not being found. This is the concrete proof that `Assemblies="[typeof(App).Assembly, typeof(AssemblyMarker).Assembly]"` correctly makes BlazingStory discover `.stories.razor` content living in a separately-referenced assembly — the exact mechanism this spec's risk section flagged as unproven. If the sidebar is empty or the story fails to load, stop here and investigate `AssemblyMarker`'s namespace/visibility and the `Assemblies` array before proceeding to Task 3.

Stop the background dev server: `kill %1` (or the noted PID).

- [ ] **Step 12: Add to `Yggdrasil.slnx`**

In `Yggdrasil/Yggdrasil.slnx`, inside the `<Folder Name="/src/">` block, add (keep the existing alphabetical-by-project ordering):

```xml
		<Project Path="src/Hosting.Stories.Client/Hosting.Stories.Client.csproj" />
```

(placed before `<Project Path="src/Hosting.Web.Client/...` — "Stories" < "Web" alphabetically)

- [ ] **Step 13: Stage**

```bash
git -C Yggdrasil add src/Hosting.Stories.Client Directory.Packages.props Yggdrasil.slnx
git -C Yggdrasil status --short
```

Show the diff to the human. Do not commit.

---

### Task 3: Yggdrasil — `Hosting.Stories.Server` (dockerizable host) + tests

**Files:**
- Create: `Yggdrasil/src/Hosting.Stories.Server/Hosting.Stories.Server.csproj`
- Create: `Yggdrasil/src/Hosting.Stories.Server/Program.cs`
- Create: `Yggdrasil/tests/Hosting.Stories.Server.Tests/Hosting.Stories.Server.Tests.csproj`
- Create: `Yggdrasil/tests/Hosting.Stories.Server.Tests/StoriesServerTests.cs`
- Modify: `Yggdrasil/Yggdrasil.slnx`

**Interfaces:**
- Consumes: `Norse.Hosting.Stories.Client.App` and the Client project's compiled static web assets (Task 2), via `ProjectReference` + `UseBlazorFrameworkFiles()`.
- Produces: the `Hosting.Stories.Server` deployable — `Program` (top-level statements, `internal` by default) is what Task 4's container build publishes and what Task 3's own tests exercise via `WebApplicationFactory<Program>`.

- [ ] **Step 1: Write the failing tests first**

`Yggdrasil/tests/Hosting.Stories.Server.Tests/Hosting.Stories.Server.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="..\..\src\Hosting.Stories.Server\Hosting.Stories.Server.csproj" />
		<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="11.0.0-preview.5.26302.115" />
	</ItemGroup>
</Project>
```

`Yggdrasil/tests/Hosting.Stories.Server.Tests/StoriesServerTests.cs`:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;

namespace Norse.Hosting.Stories.Server.Tests;

public class StoriesServerTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
	readonly HttpClient _client = factory.CreateClient();

	[Fact]
	async Task Root_serves_the_blazor_app_shell()
	{
		var response = await _client.GetAsync(new Uri("/", UriKind.Relative));

		response.EnsureSuccessStatusCode();
		var body = await response.Content.ReadAsStringAsync();
		body.ShouldContain("_framework/blazor.webassembly");
	}

	[Fact]
	async Task Deep_client_route_falls_back_to_the_app_shell()
	{
		var response = await _client.GetAsync(new Uri("/some/deep/client/route", UriKind.Relative));

		response.EnsureSuccessStatusCode();
		var body = await response.Content.ReadAsStringAsync();
		body.ShouldContain("_framework/blazor.webassembly");
	}
}
```

- [ ] **Step 2: Confirm the tests fail because the Server project doesn't exist yet**

Run: `dotnet test Yggdrasil/tests/Hosting.Stories.Server.Tests -- --report-trx 2>&1 | head -30`
Expected: build FAILS — `Hosting.Stories.Server.csproj` doesn't exist yet, so the `ProjectReference` can't resolve. This is the expected red state before Step 3.

- [ ] **Step 3: Create the Server project**

`Yggdrasil/src/Hosting.Stories.Server/Hosting.Stories.Server.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
	<PropertyGroup>
		<ContainerBaseImage>mcr.microsoft.com/dotnet/aspnet:$(ContainerVersion)</ContainerBaseImage>
	</PropertyGroup>
	<ItemGroup>
		<ProjectReference Include="..\Hosting.Stories.Client\Hosting.Stories.Client.csproj" />
		<PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.Server" />
	</ItemGroup>
</Project>
```

`Yggdrasil/src/Hosting.Stories.Server/Program.cs`:

```csharp
Console.Title = "Norse Stories Server";
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
	app.UseWebAssemblyDebugging();
}

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapFallbackToFile("index.html");

await app
	.RunAsync()
	.ConfigureAwait(false);
```

- [ ] **Step 4: Run the tests again to verify they pass**

Run: `dotnet test Yggdrasil/tests/Hosting.Stories.Server.Tests -- --report-trx 2>&1 | tail -30`
Expected: both `StoriesServerTests` tests PASS.

- [ ] **Step 5: Manually verify the dockerizable host actually serves the story catalog**

Run: `dotnet run --project Yggdrasil/src/Hosting.Stories.Server --urls http://localhost:5300 &`

Open `http://localhost:5300/` in a browser. Confirm the same BlazingStory catalog verified in Task 2 Step 11 renders identically when served through this project instead of the Client's own dev server — this is the actual production-shaped path (`UseBlazorFrameworkFiles`/`MapFallbackToFile`), not the WASM dev server.

Stop it: `kill %1` (or the noted PID).

- [ ] **Step 6: Add both new projects to `Yggdrasil.slnx`**

In `Yggdrasil/Yggdrasil.slnx`, inside `<Folder Name="/src/">`, add after the `Hosting.Stories.Client` entry from Task 2 Step 12:

```xml
		<Project Path="src/Hosting.Stories.Server/Hosting.Stories.Server.csproj" />
```

Inside `<Folder Name="/tests/">`, add (alphabetically, before `Hosting.Web.Server.Tests`):

```xml
		<Project Path="tests/Hosting.Stories.Server.Tests/Hosting.Stories.Server.Tests.csproj" />
```

- [ ] **Step 7: Stage and commit**

```bash
git -C Yggdrasil add src/Hosting.Stories.Server tests/Hosting.Stories.Server.Tests Yggdrasil.slnx
git -C Yggdrasil status --short
```

Show the diff to the human. Do not commit.

---

### Task 4: Ginnungagap (`.github`) — publish `Hosting.Stories.Server` to GHCR

**Files:**
- Modify: `NorseArchitecture/.github/.github/workflows/release-container.yml`

**Interfaces:**
- Consumes: `Yggdrasil/src/Hosting.Stories.Server/Hosting.Stories.Server.csproj` (Task 3) via `dotnet publish`.
- Produces: `ghcr.io/norsearchitecture/hosting/stories:{version}` image on every stable-tag release of Yggdrasil, alongside the existing migrations/web/worker images. No other repo needs to change — Yggdrasil's own `release.yml` already calls this workflow unconditionally.

- [ ] **Step 1: Add the stories image block**

In `NorseArchitecture/.github/.github/workflows/release-container.yml`, in the `package` job, after the `# ── worker ──` section and before `# ── release ──`, add:

```yaml
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
```

- [ ] **Step 2: Add the new SBOM to the GitHub Release step**

In the same file, in the `Create GitHub Release` step, add `sbom-stories.cdx.json` to the `gh release create` argument list:

```yaml
      - name: Create GitHub Release
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          gh release create "${{ github.ref_name }}" \
            sbom-migrations.cdx.json \
            sbom-web.cdx.json \
            sbom-worker.cdx.json \
            sbom-stories.cdx.json \
            --generate-notes
```

- [ ] **Step 3: Verify YAML is well-formed**

Run: `python3 -c "import yaml; yaml.safe_load(open('NorseArchitecture/.github/.github/workflows/release-container.yml'))" && echo OK`
Expected: `OK` (this workflow can't be executed locally — it's a `workflow_call`-only reusable workflow — so a YAML parse check is the available automated verification; the real test is the next tag push on Yggdrasil, which is outside this plan's scope to trigger).

- [ ] **Step 4: Stage**

```bash
git -C NorseArchitecture/.github add .github/workflows/release-container.yml
git -C NorseArchitecture/.github status --short
```

Show the diff to the human. Do not commit.

---

### Task 5: Documentation sync

**Files:**
- Modify: `Bifrost/CLAUDE.md`
- Modify: `Yggdrasil/CLAUDE.md`
- Modify: `Naglfar/README.md`

**Interfaces:** None — doc-only task, no code interfaces.

- [ ] **Step 1: Update Bifrost's realm table**

In `Bifrost/CLAUDE.md` §2's realm table, the Naglfar row currently reads:

```
| Naglfar | `Norse.DesignSystem.*` | first token pipeline live (`@norsearchitecture/design-tokens`); FluentUI Blazor is a validated but not yet platform-decided consumer — see Midgard's open component-library question |
```

Change to:

```
| Naglfar | `Norse.DesignSystem.*` | token pipeline live (`@norsearchitecture/design-tokens`); `DesignSystem.Stories` is a content-only RCL of `.stories.razor`/markdown catalog pages — the runnable BlazingStory host lives in Yggdrasil, not here (`../Glitnir/docs/Platform/specs/2026-07-12-designsystem-stories-hosting-design.md`) |
```

- [ ] **Step 2: Update Yggdrasil's project list**

In `Bifrost/CLAUDE.md` §2's realm table, the Yggdrasil row's function description currently reads:

```
| Yggdrasil | `Norse.Hosting.*` | Hosting runtimes and deployables: web server, worker, migration service, WASM client, and MAUI app |
```

Change to:

```
| Yggdrasil | `Norse.Hosting.*` | Hosting runtimes and deployables: web server, worker, migration service, WASM client, MAUI app, and the BlazingStory catalog host (`Hosting.Stories.Client`/`.Server`) |
```

In `Yggdrasil/CLAUDE.md` §1, the sentence currently reads:

```
Yggdrasil is **connective tissue** — `Norse.Hosting`: the web, worker, and migration service chassis (`Norse.Hosting.Web.Server`/`.Web.Client`/`.App`/`.Worker`/`.Migrations.Service`) and the deployables built on it.
```

Change to:

```
Yggdrasil is **connective tissue** — `Norse.Hosting`: the web, worker, and migration service chassis (`Norse.Hosting.Web.Server`/`.Web.Client`/`.App`/`.Worker`/`.Migrations.Service`/`.Stories.Client`/`.Stories.Server`) and the deployables built on it.
```

- [ ] **Step 3: Add a Naglfar README line**

In `Naglfar/README.md`, under the "## Status" heading, after the existing token-pipeline paragraph, add a new paragraph:

```markdown
`DesignSystem.Stories` is Naglfar's first .NET project — a content-only Razor Class Library of `.stories.razor` catalog pages for the platform's Blazor components. It ships no runnable app of its own; Yggdrasil hosts the BlazingStory catalog built from it (`Hosting.Stories.Client`/`.Server`), published as a container to `ghcr.io/norsearchitecture/hosting/stories`. Full design: `../Glitnir/docs/Platform/specs/2026-07-12-designsystem-stories-hosting-design.md`.
```

- [ ] **Step 4: Stage all three**

```bash
git -C Bifrost add CLAUDE.md
git -C Yggdrasil add CLAUDE.md
git -C Naglfar add README.md
git -C Bifrost status --short
git -C Yggdrasil status --short
git -C Naglfar status --short
```

Show the diffs to the human. Do not commit.

---

## Self-Review

**Spec coverage:** §1.1 (Naglfar RCL split + Client/Server pair) → Tasks 1–3. §1.2 (Gjallarhorn freshness, seeding `NaglfarVersion`) → Task 2 Step 1. §1.3 (GHCR shipping via `release-container.yml`) → Task 4. §4 (documentation consequences) → Task 5. §6 (BlazingStory split risk, named first task) → Task 2 Step 11's manual browser verification is the concrete proof point the spec called for. §7 (success criterion: RCL shape, Client/Server projects, container tag, CPM entry, `dotnet watch` hot reload, no deploy target) — all covered except the explicit `dotnet watch` smoke test, which isn't its own task since it's mechanically identical to the `dotnet run` verification already done in Task 3 Step 5 (watch mode is the same host, just with file-change monitoring layered on — no separate code path to verify).

**Placeholder scan:** No TBDs. Every code block is complete, copy-pasteable content, not a description of content. The one place a step could look uncertain — Task 2 Step 11's browser check — is written as a concrete pass/fail condition ("sidebar shows Primitives/Loader… both variants render… no console error") rather than a vague "verify it works."

**Type consistency:** `AssemblyMarker` (Task 1) is referenced by exact name in Task 2 Step 5's `App.razor` and imported via the `@using Norse.DesignSystem.Stories` line added in Task 2 Step 4 — checked against Task 1 Step 2's actual namespace. `Program` (Task 3's top-level statements, implicitly `internal partial class Program`) is referenced by Task 3's own `WebApplicationFactory<Program>` in the same project's test suite, which is the standard pattern requiring no explicit `InternalsVisibleTo` beyond what `src/Directory.Build.props` already grants platform-wide. `Hosting.Stories.Client`/`Hosting.Stories.Server` project names and relative paths are consistent between the `.csproj` files (Tasks 2–3), the `ProjectReference` in Task 3 Step 3, and the `Yggdrasil.slnx` entries (Task 2 Step 12, Task 3 Step 6).
