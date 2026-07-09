# Naglfar — Style Dictionary Token Pipeline & FluentUI Blazor Seed

**Date:** 2026-07-09
**Status:** Approved design, ready for planning
**Owner:** Buvy

**Companion context:** Naglfar (`Norse.DesignSystem`) has been nomenclature-only since 2026-06-19 — codename and namespace registered in `Glitnir/docs/codenames.md`, explicitly *not* design authority. This spec is the first real content to land there. The palette this spec synthesizes is **provisional and functional, not final brand taste** — it proves the pipeline end to end and is expected to be replaced once real design expertise is brought in, per the standing 2026-06-19 decision.

---

## 0. Why This Comes Next

Naglfar exists to be the platform's single source of truth for design tokens — colors, typography, spacing, radii, elevation — built once and consumed everywhere: CSS for any web surface, JS for a future Vite/React app fronting a shadcn/ui component registry, and a generated C# seed for Blazor apps using FluentUI's `DesignTokens` adaptive color system. Today it's an empty repo with a README. This spec makes it a real, publishable npm package.

It also produces the platform's first concrete signal toward Midgard's open "component library: Blazorise, FluentUI, or hand-rolled" question (`Glitnir/docs/Midgard/specs/2026-06-05-ui-composition-design.md`, §11.1) — without formally closing it. That's a bigger decision than one token package; see §9.

---

## 1. Scope

Full token set, including component-level tokens: color, typography, spacing, radius, elevation, and a handful of representative component tokens (button, input, card) that demonstrate the reference pattern. Both light and dark themes from day one — FluentUI's `baseLayerLuminance`/`Mode` concept makes light/dark a first-class axis of the underlying system Naglfar is feeding, so treating it as a v2 add-on would mean a breaking token-shape change later.

---

## 2. Repo Shape

Naglfar becomes a **pure Node/npm package** — no `.csproj`. It is a token-authoring repo, not a .NET project. A future `Norse.DesignSystem.csproj` wrapping the generated C# output as a NuGet package is an explicit non-goal here; that's its own decision if/when it's needed.

```
Naglfar/
├── package.json
├── style-dictionary.config.js
├── tokens/
│   ├── color.json          # primitive scale + semantic aliases, light/dark
│   ├── typography.json     # font families, type scale, weights, line-heights
│   ├── spacing.json        # 4px-based spacing scale
│   ├── radius.json         # corner radii
│   ├── elevation.json      # shadow/elevation levels, light + dark treatments
│   └── components/
│       ├── button.json     # references semantic color/spacing/radius tokens
│       ├── input.json
│       └── card.json
├── formats/
│   └── csharp-fluent-token-seed.js   # custom SD format → FluentTokenSeed.g.cs
├── test/
│   └── build.test.js
└── dist/                    # build output, git-ignored
    ├── css/tokens.css
    ├── js/tokens.js
    ├── json/tokens.json
    └── csharp/FluentTokenSeed.g.cs
```

**Token tiers**, strictly enforced by reference direction:

1. **Primitive** — raw values, no semantic meaning. `color.amber.700 = "#b5610f"`.
2. **Semantic** — role-based aliases that reference primitives. `color.semantic.primary = {color.amber.700}` (light) / `{color.amber.500}` (dark).
3. **Component** — component-scoped tokens that reference semantics only, never primitives directly. `button.primary.background = {color.semantic.primary}`.

This is what keeps the palette swappable later without touching component definitions — a future design pass replaces the primitive scale, semantics and components inherit the change automatically.

---

## 3. Token Values (Provisional Palette — "Forge Amber")

Accent direction chosen for its lore fit (Naglfar/forge) and because it's distinctive without being brand-precious. All values below are placeholders in spirit, exact in value — swap-ready.

### 3.1 Color — primitives

| Scale | 100 | 300 | 500 | 700 | 800 | 900 |
|---|---|---|---|---|---|---|
| **Amber** (accent) | `#fcebcf` | `#f4c26b` | `#e08a1e` | `#b5610f` | — | `#7c3f0a` |
| **Neutral** (warm slate) | `#f6f4f0` | `#d3cdc2` | `#797265` | `#413c35` | `#2a2723` | `#1c1a17` |
| **Gold** (warning only) | — | — | `#e0bd4a` (400) | `#c9a227` (600) | — | — |
| **Red** (danger only) | — | — | `#e0685a` (400) | `#c0392b` (600) | — | — |
| **Green** (success only) | — | — | `#6bab6b` (400) | `#3f7d3f` (600) | — | — |
| **Blue** (info only) | — | — | `#6d9bd1` (400) | `#3468a6` (600) | — | — |

Neutral-800 exists solely to give dark theme a `surface` stop distinct from `background` (`900`) — mirrors the layering role FluentUI's own `neutralLayer2` recipe plays, though see the note in §5: within FluentUI-rendered components, layering is computed by their algorithm, not read from this token. `surface` here matters for the CSS/JS outputs feeding non-FluentUI consumers.

**Warning is deliberately off the accent hue.** If `warning` reused amber, a caution banner and a primary button would render as the same color. Warning uses gold instead — visually adjacent but distinguishable.

### 3.2 Color — semantic (referencing primitives above)

| Role | Light | Dark |
|---|---|---|
| `primary` | `{color.amber.700}` | `{color.amber.500}` |
| `warning` | `{color.gold.600}` | `{color.gold.400}` |
| `danger` | `{color.red.600}` | `{color.red.400}` |
| `success` | `{color.green.600}` | `{color.green.400}` |
| `info` | `{color.blue.600}` | `{color.blue.400}` |
| `background` | `{color.neutral.100}` | `{color.neutral.900}` |
| `surface` | `#ffffff` | `{color.neutral.800}` |
| `border` | `{color.neutral.300}` | `{color.neutral.700}` |
| `text` | `{color.neutral.900}` | `{color.neutral.100}` |

### 3.3 Typography

- `font.family.body` — `'Segoe UI', system-ui, -apple-system, sans-serif` (matches FluentUI's own default stack)
- `font.family.mono` — `'Cascadia Code', ui-monospace, monospace`
- Type scale (1.25 ratio, 16px base): `xs`=12, `sm`=14, `base`=16, `lg`=18, `xl`=20, `2xl`=24, `3xl`=30, `4xl`=36, `5xl`=48
- Weights: `regular`=400, `medium`=500, `semibold`=600, `bold`=700
- Line-heights: `tight`=1.2, `normal`=1.5, `relaxed`=1.75

### 3.4 Spacing (4px base unit)

`space-0`=0, `1`=4, `2`=8, `3`=12, `4`=16, `5`=20, `6`=24, `8`=32, `10`=40, `12`=48, `16`=64

### 3.5 Radius

`sm`=4, `md`=8, `lg`=12, `xl`=16, `full`=9999

### 3.6 Elevation

Light theme uses soft shadows (`elevation-1` = `0 1px 2px rgba(28,26,23,.08)`, scaling through `elevation-4`). Dark theme swaps to a subtle lighter-border + faint glow treatment at the same 4 levels — a shadow is nearly invisible against a dark surface, so shadow-only doesn't carry over.

### 3.7 Component tokens (representative — button shown, input/card follow the same pattern)

```json
{
  "button": {
    "primary": {
      "background": { "value": "{color.semantic.primary}" },
      "background-hover": { "value": "{color.amber.900}" },
      "foreground": { "value": "#ffffff" },
      "radius": { "value": "{radius.md}" },
      "padding-x": { "value": "{spacing.4}" },
      "padding-y": { "value": "{spacing.2}" }
    },
    "danger": {
      "background": { "value": "{color.semantic.danger}" },
      "foreground": { "value": "#ffffff" }
    }
  }
}
```

Full enumeration of every component/state (hover/active/focus/disabled × button/input/card) is implementation-plan detail, not spec detail — this establishes the reference pattern every component token follows.

---

## 4. Build Pipeline

`style-dictionary.config.js` (ESM, matching v5.5.0) defines four platforms consuming the same token tree:

| Platform | Output | Consumer |
|---|---|---|
| `css` | `dist/css/tokens.css` — `:root { --color-primary: ...; }` plus a `[data-theme="dark"]` override block | Any web surface — Blazor `wwwroot`, a future Vite/React app, plain HTML |
| `js` | `dist/js/tokens.js` — ES module exporting the token tree | Future Vite/React app, JS tooling, a future shadcn/ui `tailwind.config` |
| `json` | `dist/json/tokens.json` — flattened token JSON | Design-tooling interop (Figma Tokens Studio et al.) |
| `csharp` | `dist/csharp/FluentTokenSeed.g.cs` — custom format, scoped to only the FluentUI seed tokens | Blazor apps using FluentUI's `DesignTokens` |

`css`/`js`/`json` are stock Style Dictionary platforms with standard transforms — no custom code. `csharp` is the one custom piece: `formats/csharp-fluent-token-seed.js`, registered via `StyleDictionary.registerFormat()`, walking only `color.semantic.primary` (light) and `color.semantic.background`/neutral seed and emitting the class below.

---

## 5. FluentUI Blazor Integration

FluentUI Blazor wraps Microsoft FAST's Adaptive UI color system. Confirmed against `learn.microsoft.com/en-us/fluent-ui/web-components/getting-started/styling` and the `FluentDesignTheme.razor.cs` source in `microsoft/fluentui-blazor`:

- `accentBaseColor` / `neutralBaseColor` are each a **single seed swatch** — not a light/dark pair. FluentUI's recipes are "stateful": they algorithmically derive contrast-correct shades for every component state **and** for both light and dark mode from that one seed, per WCAG contrast targets.
- `baseLayerLuminance` is the light/dark switch, exposed in Blazor as `<FluentDesignTheme Mode="DesignThemeModes.Light|Dark|System">` — not something Naglfar needs to emit; the consuming app controls it.
- `<FluentDesignTheme>` exposes both `CustomColor` (accent) and `NeutralBaseColor` as confirmed, first-class Razor parameters (not just accent — verified directly from source, not assumed).

This means `FluentTokenSeed` does **not** mirror the CSS output's light/dark split — pre-computing two accent hexes per mode would override a decision FluentUI's adaptive engine exists specifically to make, contradicting the "feed the base tokens only, we own the seed not every downstream shade" principle this whole integration is built on.

```csharp
namespace Norse.DesignSystem;

// Generated by Style Dictionary from tokens/color.json — do not edit by hand.
public static class FluentTokenSeed
{
	public const string AccentBaseColor = "#b5610f";
	public const string NeutralBaseColor = "#797265";
}
```

Consumption:

```razor
<FluentDesignTheme Mode="DesignThemeModes.System"
                    CustomColor="@FluentTokenSeed.AccentBaseColor"
                    NeutralBaseColor="@FluentTokenSeed.NeutralBaseColor" />
```

`AccentBaseColor` sources from `color.amber.700` (the light-theme primary primitive) — a single representative seed, not tied to either theme; FluentUI computes both modes from it. `NeutralBaseColor` sources from `color.neutral.500`, the midpoint of the neutral scale.

---

## 6. Package Identity & Publishing

- **Name:** `@norsearchitecture/design-tokens` — scoped, function-named (not `naglfar`/unscoped, not `design-system`). "design-system" would conflate the token layer with a future, independently-versioned component-registry package; "tokens" alone is ambiguous on the public registry (auth/i18n tokens are common false-cognates). "design-tokens" is the established term of art for this exact artifact.
- **Registry:** GitHub Packages (`npm.pkg.github.com`, scoped to `NorseArchitecture`) — not the public npm registry. This mirrors the platform's existing posture: `release-nuget.yml` already publishes to `nuget.pkg.github.com/NorseArchitecture`, not nuget.org. Held there "until someone professional comes in and curates it properly" (Buvy, 2026-07-09) — consistent with the provisional-palette framing throughout this spec.
- **Initial version:** `0.1.0` — pre-1.0 signals provisional/not-yet-stable, matching intent.
- `package.json` essentials: `"publishConfig": {"registry": "https://npm.pkg.github.com"}`, `devDependencies: { "style-dictionary": "^5.5.0" }`, `"engines": {"node": ">=22"}` (LTS floor, auto-resolving per the platform's version-manager convention — no hard pin).

---

## 7. CI

New reusable workflows in Ginnungagap (`.github`), mirroring the existing `.NET` pair:

- **`ci-build-test-npm.yml`** — `npm ci`, `npm run build` (runs Style Dictionary), `npm test`. Parallel to `ci-build-test.yml`.
- **`release-npm.yml`** — calls the above, then `npm publish` to `npm.pkg.github.com` using `GITHUB_TOKEN`, generates an SBOM, cuts a GitHub Release. Structurally identical to `release-nuget.yml`'s `pack-and-publish` job.

Naglfar's own `.github/workflows/ci.yml` and `release.yml` are thin callers referencing these `@master`, same pattern every other realm already follows.

---

## 8. Testing

Style Dictionary output is config-driven, not logic-heavy — TDD applies to the *output*, not an algorithm. `node --test` (no added test-framework dependency, appropriate for a package this size) runs the actual build and asserts:

- `tokens.css` contains `--color-primary` and its `[data-theme="dark"]` override
- `tokens.json` parses and has the expected key shape
- `FluentTokenSeed.g.cs` contains both constants with valid 6-digit hex values

This catches token-authoring mistakes (a typo'd reference, a component token pointing at a primitive instead of a semantic alias) without re-testing Style Dictionary itself.

---

## 9. Docs Sync (Boy-Scout Law)

Ships in the same change:

- **Naglfar's README** — "Status: Nomenclature only" is no longer accurate; update to describe the token package while keeping the "design rules/taste deferred to real experts" framing, since the palette remains explicitly provisional.
- **Bifröst's CLAUDE.md** — Naglfar's realm-table row currently reads "standalone realm, no declared consumers yet." This ships a real consumer path (FluentUI Blazor via `FluentTokenSeed`), so that line needs updating.
- **Explicitly not resolved:** Midgard's open "component library: Blazorise, FluentUI, or hand-rolled" question (`2026-06-05-ui-composition-design.md`, §11.1) stays open. This work validates the FluentUI direction is technically viable end-to-end; it does not itself constitute the platform decision. Flagged here so that decision gets closed deliberately, not by inference from this spec shipping.

---

## 10. Explicitly Out of Scope

- A `Norse.DesignSystem.csproj` / NuGet packaging of the C# output (§2).
- The Vite/React app and shadcn/ui component registry this package is *built to eventually feed* — not built here. The `css`/`js`/`json` outputs exist so that future work has something to consume, not because that work is starting now.
- Resolving Midgard's component-library open question (§9).
- Full enumeration of every component token/state beyond the button reference pattern (§3.7).
