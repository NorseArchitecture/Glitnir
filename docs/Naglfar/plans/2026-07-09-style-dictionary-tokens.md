# Naglfar Style Dictionary Token Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn Naglfar from an empty README into a real, publishable `@norsearchitecture/design-tokens` npm package — a Style Dictionary pipeline that builds color/typography/spacing/radius/elevation/component tokens into CSS, JS, JSON, and a generated C# seed for FluentUI Blazor's `DesignTokens`.

**Architecture:** Three-tier token source (primitive → semantic → component) in `tokens/**/*.json`, W3C DTCG syntax (`$type`/`$value`), built by a single self-executing `style-dictionary.config.js` into four platforms. Light/dark themed tokens are authored as `{...}.light` / `{...}.dark` leaf pairs; a custom CSS format collapses that into a single `tokens.css` with a `:root` block and a `[data-theme="dark"]` override block. A custom C# format emits exactly two constants (`AccentBaseColor`, `NeutralBaseColor`) for FluentUI's adaptive engine to consume — no light/dark split there, since FluentUI derives both modes from one seed.

**Tech Stack:** `style-dictionary@5.5.0` (ESM), Node's built-in `node:test` runner, GitHub Actions + GitHub Packages (`npm.pkg.github.com`).

## Global Constraints

- Full spec: `Glitnir/docs/Naglfar/specs/2026-07-09-style-dictionary-tokens-design.md` — every task below implements a section of it; re-read it if a step's rationale is unclear.
- Palette is **provisional/functional**, not final brand taste (spec, Companion context) — don't second-guess the exact hex values; they're deliberately swap-ready, not sacred.
- Naglfar is a **pure Node/npm package** — no `.csproj`, no `.sln`, ever, in this plan (spec §2).
- Style Dictionary tokens use **W3C DTCG syntax** (`$type`/`$value`, not legacy `type`/`value`) — confirmed required by the installed `5.5.0` package (its DTCG auto-detection only activates once *any* `$value` key appears in the source tree, so consistency across every token file matters).
- Dimension-type token values are authored as **already-unit-suffixed strings** (`"4px"`, `"8px"`) — confirmed empirically safe with the `css` transform group in this version; do not author bare unitless numbers for dimensions.
- **No automatic git commits** — per this repo's law, "commit" steps in this plan mean `git add` + `git status` to show the staged diff. The human (Buvy) runs the actual `git commit`. This applies even though the generic plan template below shows a `git commit` command — do not run it; stage and stop.
- 2-space indentation for JSON/YAML/Markdown, tabs for JS/C# — matches this platform's existing files (confirmed by reading `ci-build-test.yml`, `release-nuget.yml`, and Glitnir's `.editorconfig` convention).
- `devDependencies: { "style-dictionary": "^5.5.0" }` — exact version confirmed current/latest on the npm registry.
- **Correction found during Task 1 execution (2026-07-09):** `"test": "node --test test/"` (directory form) fails with `MODULE_NOT_FOUND` on Node v24.18.0 — Node's CLI arg parsing collides the path with the entry-module argument. Fixed to `"node --test test/*.test.js"` (glob form), confirmed working via the npm script itself, not just direct `node` invocation. This is already corrected in Task 1's `package.json` code block below.
- **Correction found during Task 7 execution (2026-07-09):** the original `release-npm.yml` YAML only attached the SBOM to the GitHub Release (no npm tarball, unlike `release-nuget.yml`'s `.nupkg` attachment) and had no duplicate-version guard on `npm publish` (unlike `.NET`'s `--skip-duplicate`, a re-run against an already-published version hard-failed). Both fixed: an `npm pack` step now produces a `.tgz` attached alongside the SBOM, and `npm publish` is now guarded by an `npm view` existence check that skips (logs, doesn't fail) if the exact version is already published. Already corrected in Task 7's `release-npm.yml` code block below.

All file paths below are relative to the **Bifrost** meta-repo root (the session working directory), not to this plan document's own location in Glitnir.

---

### Task 1: Package scaffold, primitive/semantic color tokens, and the themed CSS output

**Files:**
- Create: `Naglfar/package.json`
- Create: `Naglfar/.gitignore`
- Create: `Naglfar/tokens/color.json`
- Create: `Naglfar/style-dictionary.config.js`
- Create: `Naglfar/test/build.test.js`

**Interfaces:**
- Produces: `tokens/color.json` keys `color.amber.{100,300,500,700,900}`, `color.neutral.{100,300,500,700,800,900}`, `color.gold.{400,600}`, `color.red.{400,600}`, `color.green.{400,600}`, `color.blue.{400,600}`, and `color.semantic.{primary,warning,danger,success,info,background,surface,border,text}.{light,dark}` — every later task (2, 3, 6) references these exact paths.
- Produces: `style-dictionary.config.js` exporting nothing (self-executing script) but registering format `css/theme-variables` — Tasks 2 and 3 add platforms/formats to this same file.
- Produces: `npm run build` (runs the config script) and `npm test` (runs `node --test test/`) — every later task's verification step uses these two commands.

- [ ] **Step 1: Create the package directory scaffold and `.gitignore`**

Create `Naglfar/.gitignore`:

```gitignore
node_modules/
dist/
```

- [ ] **Step 2: Write `package.json`**

Create `Naglfar/package.json`:

```json
{
  "name": "@norsearchitecture/design-tokens",
  "version": "0.1.0",
  "private": false,
  "type": "module",
  "description": "Norse Architecture design tokens — colors, typography, spacing, radius, elevation, and component tokens, built with Style Dictionary.",
  "license": "MIT",
  "repository": {
    "type": "git",
    "url": "https://github.com/NorseArchitecture/Naglfar.git"
  },
  "scripts": {
    "build": "node style-dictionary.config.js",
    "test": "node --test test/*.test.js"
  },
  "engines": {
    "node": ">=22"
  },
  "publishConfig": {
    "registry": "https://npm.pkg.github.com"
  },
  "devDependencies": {
    "style-dictionary": "^5.5.0"
  }
}
```

- [ ] **Step 3: Install dependencies**

Run: `npm install --prefix Naglfar`
Expected: `node_modules/style-dictionary` created, `package-lock.json` written, no errors.

- [ ] **Step 4: Write the failing test**

Create `Naglfar/test/build.test.js`:

```js
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

test('tokens.css exposes --color-semantic-primary and a dark override', () => {
	const css = readFileSync(new URL('../dist/css/tokens.css', import.meta.url), 'utf8');
	assert.match(css, /--color-semantic-primary:\s*#[0-9a-f]{6};/);
	assert.match(css, /\[data-theme="dark"\]\s*{[^}]*--color-semantic-primary:\s*#[0-9a-f]{6};/s);
});

test('tokens.css exposes every semantic color role', () => {
	const css = readFileSync(new URL('../dist/css/tokens.css', import.meta.url), 'utf8');
	for (const role of ['primary', 'warning', 'danger', 'success', 'info', 'background', 'surface', 'border', 'text']) {
		assert.match(css, new RegExp(`--color-semantic-${role}:`), `missing --color-semantic-${role}`);
	}
});
```

- [ ] **Step 5: Run the test to verify it fails**

Run: `npm test --prefix Naglfar`
Expected: FAIL — `ENOENT: no such file or directory, open '.../dist/css/tokens.css'` (nothing has been built yet).

- [ ] **Step 6: Write the color token source**

Create `Naglfar/tokens/color.json`:

```json
{
  "color": {
    "amber": {
      "100": { "$type": "color", "$value": "#fcebcf" },
      "300": { "$type": "color", "$value": "#f4c26b" },
      "500": { "$type": "color", "$value": "#e08a1e" },
      "700": { "$type": "color", "$value": "#b5610f" },
      "900": { "$type": "color", "$value": "#7c3f0a" }
    },
    "neutral": {
      "100": { "$type": "color", "$value": "#f6f4f0" },
      "300": { "$type": "color", "$value": "#d3cdc2" },
      "500": { "$type": "color", "$value": "#797265" },
      "700": { "$type": "color", "$value": "#413c35" },
      "800": { "$type": "color", "$value": "#2a2723" },
      "900": { "$type": "color", "$value": "#1c1a17" }
    },
    "gold": {
      "400": { "$type": "color", "$value": "#e0bd4a" },
      "600": { "$type": "color", "$value": "#c9a227" }
    },
    "red": {
      "400": { "$type": "color", "$value": "#e0685a" },
      "600": { "$type": "color", "$value": "#c0392b" }
    },
    "green": {
      "400": { "$type": "color", "$value": "#6bab6b" },
      "600": { "$type": "color", "$value": "#3f7d3f" }
    },
    "blue": {
      "400": { "$type": "color", "$value": "#6d9bd1" },
      "600": { "$type": "color", "$value": "#3468a6" }
    },
    "semantic": {
      "primary": {
        "light": { "$type": "color", "$value": "{color.amber.700}" },
        "dark": { "$type": "color", "$value": "{color.amber.500}" }
      },
      "warning": {
        "light": { "$type": "color", "$value": "{color.gold.600}" },
        "dark": { "$type": "color", "$value": "{color.gold.400}" }
      },
      "danger": {
        "light": { "$type": "color", "$value": "{color.red.600}" },
        "dark": { "$type": "color", "$value": "{color.red.400}" }
      },
      "success": {
        "light": { "$type": "color", "$value": "{color.green.600}" },
        "dark": { "$type": "color", "$value": "{color.green.400}" }
      },
      "info": {
        "light": { "$type": "color", "$value": "{color.blue.600}" },
        "dark": { "$type": "color", "$value": "{color.blue.400}" }
      },
      "background": {
        "light": { "$type": "color", "$value": "{color.neutral.100}" },
        "dark": { "$type": "color", "$value": "{color.neutral.900}" }
      },
      "surface": {
        "light": { "$type": "color", "$value": "#ffffff" },
        "dark": { "$type": "color", "$value": "{color.neutral.800}" }
      },
      "border": {
        "light": { "$type": "color", "$value": "{color.neutral.300}" },
        "dark": { "$type": "color", "$value": "{color.neutral.700}" }
      },
      "text": {
        "light": { "$type": "color", "$value": "{color.neutral.900}" },
        "dark": { "$type": "color", "$value": "{color.neutral.100}" }
      }
    }
  }
}
```

- [ ] **Step 7: Write the build script with the custom themed-CSS format**

Create `Naglfar/style-dictionary.config.js`:

```js
import StyleDictionary from 'style-dictionary';

function cssVarName(token) {
	return token.path
		.filter((segment) => segment !== 'light' && segment !== 'dark')
		.join('-');
}

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

const sd = new StyleDictionary({
	source: ['tokens/**/*.json'],
	platforms: {
		css: {
			transformGroup: 'css',
			buildPath: 'dist/css/',
			files: [
				{
					destination: 'tokens.css',
					format: 'css/theme-variables',
				},
			],
		},
	},
});

await sd.buildAllPlatforms();
```

- [ ] **Step 8: Run the build**

Run: `npm run build --prefix Naglfar`
Expected:
```
css
✔︎ dist/css/tokens.css
```

- [ ] **Step 9: Run the test to verify it passes**

Run: `npm test --prefix Naglfar`
Expected: PASS — both tests green (`# pass 2`, `# fail 0`).

- [ ] **Step 10: Stage the change and show the diff**

```bash
git -C Naglfar add package.json .gitignore tokens/color.json style-dictionary.config.js test/build.test.js package-lock.json
git -C Naglfar status --short
```

Do not run `git commit` — stage only, per this repo's process law. Show the staged diff and stop for review.

---

### Task 2: JS and JSON output platforms

**Files:**
- Modify: `Naglfar/style-dictionary.config.js`
- Modify: `Naglfar/test/build.test.js`

**Interfaces:**
- Consumes: `color.json`'s token tree from Task 1 (no changes to it).
- Produces: `dist/js/tokens.js` (flat `export const` per token, PascalCase names) and `dist/json/tokens.json` (nested, fully-resolved) — both are stock Style Dictionary platforms, no custom format code.

- [ ] **Step 1: Write the failing tests**

Add to `Naglfar/test/build.test.js`:

```js
test('tokens.json parses and resolves references to literal hex', () => {
	const json = JSON.parse(readFileSync(new URL('../dist/json/tokens.json', import.meta.url), 'utf8'));
	assert.equal(json.color.semantic.primary.light, '#b5610f');
	assert.equal(json.color.semantic.primary.dark, '#e08a1e');
});

test('tokens.js exports a flat named constant per color token', () => {
	const js = readFileSync(new URL('../dist/js/tokens.js', import.meta.url), 'utf8');
	assert.match(js, /export const ColorSemanticPrimaryLight = "#b5610f";/);
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `npm test --prefix Naglfar`
Expected: FAIL — `ENOENT` on `dist/json/tokens.json` and `dist/js/tokens.js` (platforms don't exist yet).

- [ ] **Step 3: Add the `js` and `json` platforms**

In `Naglfar/style-dictionary.config.js`, add to the `platforms` object (after `css`):

```js
		js: {
			transformGroup: 'js',
			buildPath: 'dist/js/',
			files: [
				{
					destination: 'tokens.js',
					format: 'javascript/es6',
				},
			],
		},
		json: {
			transformGroup: 'js',
			buildPath: 'dist/json/',
			files: [
				{
					destination: 'tokens.json',
					format: 'json/nested',
				},
			],
		},
```

- [ ] **Step 4: Run the build**

Run: `npm run build --prefix Naglfar`
Expected: four platform lines now (`css`, `js`, `json` all with `✔︎` — `csharp` doesn't exist until Task 3).

- [ ] **Step 5: Run the tests to verify they pass**

Run: `npm test --prefix Naglfar`
Expected: PASS — 4 tests green.

- [ ] **Step 6: Stage the change and show the diff**

```bash
git -C Naglfar add style-dictionary.config.js test/build.test.js
git -C Naglfar status --short
```

---

### Task 3: FluentUI Blazor C# seed (`FluentTokenSeed`)

**Files:**
- Modify: `Naglfar/style-dictionary.config.js`
- Modify: `Naglfar/test/build.test.js`

**Interfaces:**
- Consumes: `color.amber.700` and `color.neutral.500` from Task 1's `color.json` — the two primitives chosen as the FluentUI seed values (spec §5).
- Produces: `dist/csharp/FluentTokenSeed.g.cs` — `Norse.DesignSystem.FluentTokenSeed.AccentBaseColor` / `.NeutralBaseColor`, both `public const string`. This is the class a future Blazor app's `<FluentDesignTheme CustomColor="@FluentTokenSeed.AccentBaseColor" NeutralBaseColor="@FluentTokenSeed.NeutralBaseColor">` consumes — no other task produces or consumes this file.

- [ ] **Step 1: Write the failing test**

Add to `Naglfar/test/build.test.js`:

```js
test('FluentTokenSeed.g.cs contains both constants with valid hex values, no light/dark split', () => {
	const cs = readFileSync(new URL('../dist/csharp/FluentTokenSeed.g.cs', import.meta.url), 'utf8');
	assert.match(cs, /namespace Norse\.DesignSystem;/);
	assert.match(cs, /public const string AccentBaseColor = "#b5610f";/);
	assert.match(cs, /public const string NeutralBaseColor = "#797265";/);
	assert.doesNotMatch(cs, /class Light/);
	assert.doesNotMatch(cs, /class Dark/);
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npm test --prefix Naglfar`
Expected: FAIL — `ENOENT` on `dist/csharp/FluentTokenSeed.g.cs`.

- [ ] **Step 3: Register the custom C# format**

In `Naglfar/style-dictionary.config.js`, add this format registration alongside the existing `css/theme-variables` one (before `const sd = new StyleDictionary(...)`):

```js
StyleDictionary.registerFormat({
	name: 'csharp/fluent-token-seed',
	format: async ({ dictionary }) => {
		const accent = dictionary.allTokens.find((t) => t.path.join('.') === 'color.amber.700');
		const neutral = dictionary.allTokens.find((t) => t.path.join('.') === 'color.neutral.500');
		return (
			`namespace Norse.DesignSystem;\n\n` +
			`// Generated by Style Dictionary from tokens/color.json — do not edit by hand.\n` +
			`public static class FluentTokenSeed\n{\n` +
			`\tpublic const string AccentBaseColor = "${accent.$value}";\n` +
			`\tpublic const string NeutralBaseColor = "${neutral.$value}";\n` +
			`}\n`
		);
	},
});
```

- [ ] **Step 4: Add the `csharp` platform**

In the `platforms` object, add after `json`:

```js
		csharp: {
			transformGroup: 'css',
			buildPath: 'dist/csharp/',
			files: [
				{
					destination: 'FluentTokenSeed.g.cs',
					format: 'csharp/fluent-token-seed',
				},
			],
		},
```

- [ ] **Step 5: Run the build**

Run: `npm run build --prefix Naglfar`
Expected: all four platforms (`css`, `js`, `json`, `csharp`) report `✔︎`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `npm test --prefix Naglfar`
Expected: PASS — 5 tests green.

- [ ] **Step 7: Stage the change and show the diff**

```bash
git -C Naglfar add style-dictionary.config.js test/build.test.js
git -C Naglfar status --short
```

---

### Task 4: Typography, spacing, and radius primitive tokens

**Files:**
- Create: `Naglfar/tokens/typography.json`
- Create: `Naglfar/tokens/spacing.json`
- Create: `Naglfar/tokens/radius.json`
- Modify: `Naglfar/test/build.test.js`

**Interfaces:**
- Produces: `font.family.{body,mono}`, `font.size.{xs,sm,base,lg,xl,2xl,3xl,4xl,5xl}`, `font.weight.{regular,medium,semibold,bold}`, `font.lineHeight.{tight,normal,relaxed}`, `spacing.{0,1,2,3,4,5,6,8,10,12,16}`, `radius.{sm,md,lg,xl,full}` — Task 6's component tokens reference `spacing.*` and `radius.*` by these exact keys.
- These are all themeless (no `.light`/`.dark` split) — they land in `style-dictionary.config.js`'s existing `themeless` bucket automatically; no config changes needed, only new token source files.

- [ ] **Step 1: Write the failing tests**

Add to `Naglfar/test/build.test.js`:

```js
test('typography, spacing, and radius primitives resolve in tokens.json', () => {
	const json = JSON.parse(readFileSync(new URL('../dist/json/tokens.json', import.meta.url), 'utf8'));
	assert.equal(json.font.family.body, "'Segoe UI', system-ui, -apple-system, sans-serif");
	assert.equal(json.font.size.base, '16px');
	assert.equal(json.font.weight.bold, 700);
	assert.equal(json.font.lineHeight.normal, 1.5);
	assert.equal(json.spacing['4'], '16px');
	assert.equal(json.radius.md, '8px');
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npm test --prefix Naglfar`
Expected: FAIL — `TypeError: Cannot read properties of undefined (reading 'family')` (`json.font` doesn't exist yet).

- [ ] **Step 3: Write the typography tokens**

Create `Naglfar/tokens/typography.json`:

```json
{
  "font": {
    "family": {
      "body": { "$type": "fontFamily", "$value": "'Segoe UI', system-ui, -apple-system, sans-serif" },
      "mono": { "$type": "fontFamily", "$value": "'Cascadia Code', ui-monospace, monospace" }
    },
    "size": {
      "xs": { "$type": "dimension", "$value": "12px" },
      "sm": { "$type": "dimension", "$value": "14px" },
      "base": { "$type": "dimension", "$value": "16px" },
      "lg": { "$type": "dimension", "$value": "18px" },
      "xl": { "$type": "dimension", "$value": "20px" },
      "2xl": { "$type": "dimension", "$value": "24px" },
      "3xl": { "$type": "dimension", "$value": "30px" },
      "4xl": { "$type": "dimension", "$value": "36px" },
      "5xl": { "$type": "dimension", "$value": "48px" }
    },
    "weight": {
      "regular": { "$type": "fontWeight", "$value": 400 },
      "medium": { "$type": "fontWeight", "$value": 500 },
      "semibold": { "$type": "fontWeight", "$value": 600 },
      "bold": { "$type": "fontWeight", "$value": 700 }
    },
    "lineHeight": {
      "tight": { "$type": "number", "$value": 1.2 },
      "normal": { "$type": "number", "$value": 1.5 },
      "relaxed": { "$type": "number", "$value": 1.75 }
    }
  }
}
```

- [ ] **Step 4: Write the spacing tokens**

Create `Naglfar/tokens/spacing.json`:

```json
{
  "spacing": {
    "0": { "$type": "dimension", "$value": "0px" },
    "1": { "$type": "dimension", "$value": "4px" },
    "2": { "$type": "dimension", "$value": "8px" },
    "3": { "$type": "dimension", "$value": "12px" },
    "4": { "$type": "dimension", "$value": "16px" },
    "5": { "$type": "dimension", "$value": "20px" },
    "6": { "$type": "dimension", "$value": "24px" },
    "8": { "$type": "dimension", "$value": "32px" },
    "10": { "$type": "dimension", "$value": "40px" },
    "12": { "$type": "dimension", "$value": "48px" },
    "16": { "$type": "dimension", "$value": "64px" }
  }
}
```

- [ ] **Step 5: Write the radius tokens**

Create `Naglfar/tokens/radius.json`:

```json
{
  "radius": {
    "sm": { "$type": "dimension", "$value": "4px" },
    "md": { "$type": "dimension", "$value": "8px" },
    "lg": { "$type": "dimension", "$value": "12px" },
    "xl": { "$type": "dimension", "$value": "16px" },
    "full": { "$type": "dimension", "$value": "9999px" }
  }
}
```

- [ ] **Step 6: Run the build**

Run: `npm run build --prefix Naglfar`
Expected: all four platforms report `✔︎`.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `npm test --prefix Naglfar`
Expected: PASS — 6 tests green.

- [ ] **Step 8: Stage the change and show the diff**

```bash
git -C Naglfar add tokens/typography.json tokens/spacing.json tokens/radius.json test/build.test.js
git -C Naglfar status --short
```

---

### Task 5: Elevation tokens (light/dark shadow treatments)

**Files:**
- Create: `Naglfar/tokens/elevation.json`
- Modify: `Naglfar/test/build.test.js`

**Interfaces:**
- Produces: `elevation.{1,2,3,4}.{light,dark}` — themed like color, proving the same `.light`/`.dark` split mechanism from Task 1 generalizes beyond color (spec §3.6).
- Consumes: nothing new; reuses the `css/theme-variables` format from Task 1 unchanged — this task is pure token content, no pipeline code.

- [ ] **Step 1: Write the failing test**

Add to `Naglfar/test/build.test.js`:

```js
test('elevation tokens are themed the same way color tokens are', () => {
	const css = readFileSync(new URL('../dist/css/tokens.css', import.meta.url), 'utf8');
	assert.match(css, /--elevation-1: 0 1px 2px rgba\(28, 26, 23, 0\.08\);/);
	assert.match(css, /\[data-theme="dark"\]\s*{[^}]*--elevation-1:/s);
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npm test --prefix Naglfar`
Expected: FAIL — the `--elevation-1` custom property doesn't exist yet.

- [ ] **Step 3: Write the elevation tokens**

Create `Naglfar/tokens/elevation.json`:

```json
{
  "elevation": {
    "1": {
      "light": { "$type": "shadow", "$value": "0 1px 2px rgba(28, 26, 23, 0.08)" },
      "dark": { "$type": "shadow", "$value": "0 0 0 1px rgba(246, 244, 240, 0.06)" }
    },
    "2": {
      "light": { "$type": "shadow", "$value": "0 2px 4px rgba(28, 26, 23, 0.10)" },
      "dark": { "$type": "shadow", "$value": "0 0 0 1px rgba(246, 244, 240, 0.09), 0 0 8px rgba(246, 244, 240, 0.04)" }
    },
    "3": {
      "light": { "$type": "shadow", "$value": "0 4px 8px rgba(28, 26, 23, 0.12)" },
      "dark": { "$type": "shadow", "$value": "0 0 0 1px rgba(246, 244, 240, 0.12), 0 0 16px rgba(246, 244, 240, 0.06)" }
    },
    "4": {
      "light": { "$type": "shadow", "$value": "0 8px 16px rgba(28, 26, 23, 0.14)" },
      "dark": { "$type": "shadow", "$value": "0 0 0 1px rgba(246, 244, 240, 0.16), 0 0 24px rgba(246, 244, 240, 0.08)" }
    }
  }
}
```

- [ ] **Step 4: Run the build**

Run: `npm run build --prefix Naglfar`
Expected: all four platforms report `✔︎`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `npm test --prefix Naglfar`
Expected: PASS — 7 tests green.

- [ ] **Step 6: Stage the change and show the diff**

```bash
git -C Naglfar add tokens/elevation.json test/build.test.js
git -C Naglfar status --short
```

---

### Task 6: Component tokens (button, input, card)

**Files:**
- Create: `Naglfar/tokens/components/button.json`
- Create: `Naglfar/tokens/components/input.json`
- Create: `Naglfar/tokens/components/card.json`
- Modify: `Naglfar/test/build.test.js`

**Interfaces:**
- Consumes: `color.semantic.*` (Task 1), `spacing.*` / `radius.*` (Task 4), `elevation.*` (Task 5).
- Produces: `button.{primary,danger}.*`, `input.{default,focus,danger}.*`, `card.default.*` — nothing later in this plan consumes these; they're the terminal reference-pattern example the spec calls for (spec §3.7, §10 — full enumeration beyond this is explicitly out of scope).
- **Tier rule note:** per spec §2, component tokens reference semantics, not primitives — with one deliberate exception the spec's own example already established: interaction-state deltas (e.g. a hover shade) may reference a primitive scale step directly, since it's a mechanical state change, not a new semantic meaning. Applied below as `button.primary.background-hover`.

- [ ] **Step 1: Write the failing test**

Add to `Naglfar/test/build.test.js`:

```js
test('component tokens resolve through semantic/spacing/radius references', () => {
	const json = JSON.parse(readFileSync(new URL('../dist/json/tokens.json', import.meta.url), 'utf8'));
	assert.equal(json.button.primary.background.light, '#b5610f');
	assert.equal(json.button.primary.background.dark, '#e08a1e');
	assert.equal(json.button.primary.radius, '8px');
	assert.equal(json.button.primary['padding-x'], '16px');
	assert.equal(json.input.default.radius, '8px');
	assert.equal(json.card.default.padding, '24px');
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npm test --prefix Naglfar`
Expected: FAIL — `json.button` is undefined.

- [ ] **Step 3: Write the button component tokens**

Create `Naglfar/tokens/components/button.json`:

```json
{
  "button": {
    "primary": {
      "background": {
        "light": { "$type": "color", "$value": "{color.semantic.primary.light}" },
        "dark": { "$type": "color", "$value": "{color.semantic.primary.dark}" }
      },
      "background-hover": {
        "light": { "$type": "color", "$value": "{color.amber.900}" },
        "dark": { "$type": "color", "$value": "{color.amber.300}" }
      },
      "foreground": { "$type": "color", "$value": "#ffffff" },
      "radius": { "$type": "dimension", "$value": "{radius.md}" },
      "padding-x": { "$type": "dimension", "$value": "{spacing.4}" },
      "padding-y": { "$type": "dimension", "$value": "{spacing.2}" }
    },
    "danger": {
      "background": {
        "light": { "$type": "color", "$value": "{color.semantic.danger.light}" },
        "dark": { "$type": "color", "$value": "{color.semantic.danger.dark}" }
      },
      "foreground": { "$type": "color", "$value": "#ffffff" },
      "radius": { "$type": "dimension", "$value": "{radius.md}" },
      "padding-x": { "$type": "dimension", "$value": "{spacing.4}" },
      "padding-y": { "$type": "dimension", "$value": "{spacing.2}" }
    }
  }
}
```

- [ ] **Step 4: Write the input component tokens**

Create `Naglfar/tokens/components/input.json`:

```json
{
  "input": {
    "default": {
      "background": {
        "light": { "$type": "color", "$value": "{color.semantic.surface.light}" },
        "dark": { "$type": "color", "$value": "{color.semantic.surface.dark}" }
      },
      "border": {
        "light": { "$type": "color", "$value": "{color.semantic.border.light}" },
        "dark": { "$type": "color", "$value": "{color.semantic.border.dark}" }
      },
      "foreground": {
        "light": { "$type": "color", "$value": "{color.semantic.text.light}" },
        "dark": { "$type": "color", "$value": "{color.semantic.text.dark}" }
      },
      "radius": { "$type": "dimension", "$value": "{radius.md}" },
      "padding-x": { "$type": "dimension", "$value": "{spacing.3}" },
      "padding-y": { "$type": "dimension", "$value": "{spacing.2}" }
    },
    "focus": {
      "border": {
        "light": { "$type": "color", "$value": "{color.semantic.primary.light}" },
        "dark": { "$type": "color", "$value": "{color.semantic.primary.dark}" }
      }
    },
    "danger": {
      "border": {
        "light": { "$type": "color", "$value": "{color.semantic.danger.light}" },
        "dark": { "$type": "color", "$value": "{color.semantic.danger.dark}" }
      }
    }
  }
}
```

- [ ] **Step 5: Write the card component tokens**

Create `Naglfar/tokens/components/card.json`:

```json
{
  "card": {
    "default": {
      "background": {
        "light": { "$type": "color", "$value": "{color.semantic.surface.light}" },
        "dark": { "$type": "color", "$value": "{color.semantic.surface.dark}" }
      },
      "border": {
        "light": { "$type": "color", "$value": "{color.semantic.border.light}" },
        "dark": { "$type": "color", "$value": "{color.semantic.border.dark}" }
      },
      "radius": { "$type": "dimension", "$value": "{radius.lg}" },
      "padding": { "$type": "dimension", "$value": "{spacing.6}" },
      "elevation": {
        "light": { "$type": "shadow", "$value": "{elevation.1.light}" },
        "dark": { "$type": "shadow", "$value": "{elevation.1.dark}" }
      }
    }
  }
}
```

- [ ] **Step 6: Run the build**

Run: `npm run build --prefix Naglfar`
Expected: all four platforms report `✔︎`.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `npm test --prefix Naglfar`
Expected: PASS — 8 tests green.

- [ ] **Step 8: Stage the change and show the diff**

```bash
git -C Naglfar add tokens/components/button.json tokens/components/input.json tokens/components/card.json test/build.test.js
git -C Naglfar status --short
```

---

### Task 7: Ginnungagap reusable npm CI/release workflows

**Files:**
- Create: `../.github/.github/workflows/ci-build-test-npm.yml`
- Create: `../.github/.github/workflows/release-npm.yml`

**Interfaces:**
- Produces: two reusable workflows (`workflow_call`) that Task 8's caller workflows in Naglfar reference by `NorseArchitecture/.github/.github/workflows/{name}.yml@master`.
- Modeled directly on the existing `.NET` pair — `ci-build-test.yml` (build/test job) and `release-nuget.yml` (build-test → pack-and-publish, pushing to GitHub Packages) — read both before writing these, they set the exact structural precedent (job names, SBOM step, `--skip-duplicate` equivalent, GitHub Release creation).

- [ ] **Step 1: Write `ci-build-test-npm.yml`**

Create `../.github/.github/workflows/ci-build-test-npm.yml`:

```yaml
name: CI Build & Test (npm)

# Setup steps (checkout, Node, install) are intentionally inline.
# Composite actions cannot be called from within a reusable workflow — the runner
# checks out the CALLER's repository, so any ./ path resolves there, not here.

on:
  workflow_call:

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v7

      - name: Setup Node
        uses: actions/setup-node@v4
        with:
          node-version: '22'
          registry-url: 'https://npm.pkg.github.com'
          scope: '@norsearchitecture'

      - name: Install dependencies
        run: npm ci

      - name: Build
        run: npm run build

      - name: Test
        run: npm test
```

- [ ] **Step 2: Write `release-npm.yml`**

Create `../.github/.github/workflows/release-npm.yml`:

```yaml
name: npm Release

on:
  workflow_call:

permissions:
  contents: write
  packages: write
  pull-requests: write

jobs:
  build-test:
    uses: ./.github/workflows/ci-build-test-npm.yml
    secrets: inherit

  pack-and-publish:
    needs: [build-test]
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v7

      - name: Setup Node
        uses: actions/setup-node@v4
        with:
          node-version: '22'
          registry-url: 'https://npm.pkg.github.com'
          scope: '@norsearchitecture'

      - name: Install dependencies
        run: npm ci

      - name: Build
        run: npm run build

      - name: Generate SBOM
        uses: anchore/sbom-action@v0
        with:
          path: .
          format: cyclonedx-json
          output-file: sbom.cyclonedx.json

      - name: Pack
        run: npm pack

      - name: Publish to GitHub Packages
        run: |
          PKG_VERSION=$(node -p "require('./package.json').version")
          PKG_NAME=$(node -p "require('./package.json').name")
          if npm view "$PKG_NAME@$PKG_VERSION" version --registry https://npm.pkg.github.com &>/dev/null; then
            echo "Version $PKG_VERSION of $PKG_NAME already published, skipping."
          else
            npm publish
          fi
        env:
          NODE_AUTH_TOKEN: ${{ secrets.GITHUB_TOKEN }}

      - name: Create GitHub Release
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          gh release create "${{ github.ref_name }}" \
            ./*.tgz \
            ./sbom.cyclonedx.json \
            --generate-notes
```

- [ ] **Step 3: Stage the change and show the diff**

```bash
git -C ../.github add .github/workflows/ci-build-test-npm.yml .github/workflows/release-npm.yml
git -C ../.github status --short
```

No automated test for this step (it's GitHub Actions YAML — verified by actually running in CI, which happens once Task 8 wires a caller workflow and the branch is pushed). Note for the human reviewer: this can't be locally unit-tested; the real verification is the first CI run after Task 8 lands and a PR is opened.

---

### Task 8: Naglfar's own CI/release caller workflows

**Files:**
- Create: `Naglfar/.github/workflows/ci.yml`
- Create: `Naglfar/.github/workflows/release.yml`

**Interfaces:**
- Consumes: `ci-build-test-npm.yml` and `release-npm.yml` from Task 7, referenced by `@master`.
- Modeled directly on Svartalfheim's `ci.yml` / `release.yml` (read as reference — same trigger shape: PR-triggered CI, tag-triggered release).

- [ ] **Step 1: Write `ci.yml`**

Create `Naglfar/.github/workflows/ci.yml`:

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
```

- [ ] **Step 2: Write `release.yml`**

Create `Naglfar/.github/workflows/release.yml`:

```yaml
name: Release

on:
  push:
    tags:
      - 'v*.*.*'

permissions:
  contents: write
  packages: write
  pull-requests: write

jobs:
  release:
    uses: NorseArchitecture/.github/.github/workflows/release-npm.yml@master
    secrets: inherit
```

- [ ] **Step 3: Stage the change and show the diff**

```bash
git -C Naglfar add .github/workflows/ci.yml .github/workflows/release.yml
git -C Naglfar status --short
```

---

### Task 9: Docs sync — Naglfar README and Bifröst CLAUDE.md

**Files:**
- Modify: `Naglfar/README.md`
- Modify: `Bifrost/CLAUDE.md`

**Interfaces:**
- Pure documentation — no code interfaces produced or consumed. This is the boy-scout-law cleanup the spec (§9) requires in the same change as everything above.

- [ ] **Step 1: Update Naglfar's README status section**

In `Naglfar/README.md`, replace the `## Status` section (currently "Nomenclature only, for now...") with:

```markdown
## Status

The token pipeline is live — `@norsearchitecture/design-tokens`, built with [Style Dictionary](https://styledictionary.com/), publishes to GitHub Packages. Colors, typography, spacing, radius, elevation, and a first pass at component tokens (button/input/card) build into CSS custom properties, a JS module, flattened JSON, and a generated C# seed (`FluentTokenSeed`) for FluentUI Blazor's `DesignTokens`.

**The palette is provisional, not final brand taste.** It exists to prove the pipeline works end to end and is expected to be replaced once real design expertise is brought in — see `Glitnir/docs/Naglfar/specs/2026-07-09-style-dictionary-tokens-design.md` for the full design and that standing caveat.
```

- [ ] **Step 2: Update Bifröst's CLAUDE.md realm table**

In `Bifrost/CLAUDE.md`, find the Naglfar row in the realm table (§2, currently reads `standalone realm, no declared consumers yet`) and update the description column to:

```
first token pipeline live (`@norsearchitecture/design-tokens`); FluentUI Blazor is a validated but not yet platform-decided consumer — see Midgard's open component-library question
```

- [ ] **Step 3: Stage the change and show the diff**

```bash
git -C Naglfar add README.md
git add CLAUDE.md
git -C Naglfar status --short
git status --short
```

---

## Self-Review Notes

- **Spec coverage:** §1 scope (Task 1-6), §2 repo shape (Task 1), §3 all token categories (Tasks 1, 4, 5, 6), §4 build pipeline (Tasks 1-3), §5 FluentUI integration (Task 3), §6 package identity (Task 1), §7 CI (Tasks 7-8), §8 testing (every task's test steps), §9 docs sync (Task 9), §10 out-of-scope items — none of them have a task, confirmed correctly excluded.
- **Corrected against spec during planning (validated by actually running Style Dictionary 5.5.0, not assumed from docs):** tokens must use DTCG `$value`/`$type` syntax, not the legacy `value`/`type` the spec's §3.7 example used — every token file in this plan uses `$value`/`$type`. The spec's §3.7 button example also referenced `{color.semantic.primary}` without a light/dark suffix, which doesn't match §3.2's actual light/dark-leaf token structure — Task 6 resolves this by threading `.light`/`.dark` through every color-referencing component token, consistent with the rest of the tree.
- **Type/name consistency:** `FluentTokenSeed.AccentBaseColor` / `.NeutralBaseColor` (Task 3) match the spec §5 final decision (single seed pair, no `.Light`/`.Dark` nesting) exactly — confirmed no earlier task introduces a conflicting shape.
