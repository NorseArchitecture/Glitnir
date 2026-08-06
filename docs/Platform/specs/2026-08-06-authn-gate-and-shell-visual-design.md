# The Gate and the Shell — Visual Design for the AuthN Pages and the App Chrome

**Date:** 2026-08-06
**Status:** Design note — approved in session (browser-companion comp signed off), awaiting written review
**Owner:** Buvy
**Companion:** `../../Heimdall/specs/2026-08-06-blazor-validation-composition-design.md` — the same session's mechanism spec. This note is aesthetics and layout only; every validation behavior (inline errors, model-error bar, no summary) is that spec's law and is assumed here, not restated.
**Origin:** live Bifröst run against `Hosting.Web.Server` (2026-08-06), Login/Register inspected in-browser with real failures on screen.

---

## 1. What the Live Run Showed

1. **Fields flow in a row.** Email, Password, Remember me, and the submit button lay out inline across the content area — an accident of unstyled flow, not a form.
2. **Error text resizes inputs.** The FluentUI field's message slot participates in the field's width: entering `u`/`u` ballooned the Password input to the width of its own error sentence.
3. **Every failure rendered twice** (summary + inline) — the companion spec's problem, recorded there.
4. **The pages wear scaffold clothes.** Bare `<h1>`, no width discipline, no cross-links between Login and Register, no forgot-password link.
5. **The shell is a sample app.** Nav: Home / Counter / Weather / Auth Required / Register / Login. Footer: FluentUI promo links. Header: unstyled product name. First impression of the reference architecture is "template nobody moved into."

## 2. Decisions (session record)

1. **Layout: the gate panel** — split screen; identity panel beside the form. Chosen over centered-card and in-shell-column alternatives.
2. **Signature: the prismatic seam** — the Bifröst rendered literally as a 3px vertical gradient line at the exact boundary where the identity panel meets the form. Chosen over aurora-wash and flat-runestone treatments. This is the page's one deliberate risk; everything else stays quiet.
3. **Palette: Naglfar's existing amber/rust, unchanged** — ruled explicitly ("keep the amber/rust theme"). The seam is the page's only cool-spectrum moment, which is what makes it read.
4. **Typography: no new webfont.** A reference architecture embeds no font-licensing decision, and WASM payload is real. Voice comes from scale, weight, and tracking on the existing `font.family.body` stack.
5. **Auth routes drop the app shell.** No nav, no header, no footer on the gate — you haven't crossed the bridge yet, so you don't get the realm chrome.
6. **Shell: tidy, keep the demos.** Counter/Weather/Auth Required are the template's living proof pages — they stay, grouped under a labeled Template nav section. Register/Login leave the nav (auth lives on the gate). The FluentUI promo footer is replaced by a thin platform line.

## 3. The Gate

### 3.1 Composition

Two columns filling the viewport; the seam between them.

- **Identity panel** (left, 52%): constant `color.neutral.900` (`#1c1a17`) in **both** themes — it is always night on the panel side of the bridge. Content, top to bottom: letterspaced eyebrow `NORSE ARCHITECTURE` (`font.size.xs`, weight 600, tracking ~0.28em, `color.neutral.500`); headline `Heimdall keeps the gate.` (`font.size.3xl`–`4xl`, weight 600, `lineHeight.tight`, `color.neutral.100`); one supporting line (`font.size.sm`, `color.neutral.500`); bottom-anchored credential line `norse_identity · OpenIddict · OAuth 2.1` (`font.size.xs`, `color.neutral.700`) — the audience is engineers evaluating a platform, so the lore carries the receipts.
- **The seam**: 3px, full height, gradient stops drawn from Naglfar's own hues — light `#c0392b → #e08a1e → #e0bd4a → #3f7d3f → #3468a6 → #6d5bd0`, dark swaps to the 400-weight variants (`#e0685a → #e08a1e → #e0bd4a → #6bab6b → #6d9bd1 → #8b7ae0`). Tokenized in Naglfar (§5), never hand-coded in a component.
- **Form column** (right): theme-following surface (`#ffffff` light / `color.neutral.800` dark). Form content capped at 280px max-width and centered vertically and horizontally.

### 3.2 The form

- Heading `Log in` / `Create your account` (`font.size.xl`, weight 600), one-line subtitle beneath (`font.size.sm`, `color.neutral.500`): `Welcome back to the bridge.` / `One identity for every realm.`
- **Vertical stack, always.** Label above input; full-width inputs within the capped column; primary button full-width, amber (`semantic.primary`: `amber.700` light / `amber.500` dark — dark-mode button text is `neutral.900` for contrast).
- **Inputs never resize.** The field's message slot is excluded from width calculation; error text wraps beneath the fixed-width input. This kills §1.2 structurally — the CSS rides the shared auth stylesheet (§6), not per-page styles.
- The model-error bar (companion spec §3.2) renders between subtitle and first field.
- Cross-links: `Forgot password?` right-aligned beside the Password label; footer line `New to the platform? Create an account` on Login, `Already have an account? Log in` on Register. Links are amber, weight 500.
- Copy register: plain verbs, sentence case, active voice; the button says what it does (`Log in`, `Create account`) and toasts/redirects keep the same verb.

### 3.3 Responsive and quality floor

- **Narrow viewports stack**: the panel compresses to a compact band above the form (eyebrow + headline only), and the seam rotates with the boundary — a horizontal prismatic line between band and form. The crossing survives every width.
- Visible keyboard focus on every control (FluentUI defaults verified, not assumed); WCAG AA contrast holds on all specified pairs (amber-on-white is reserved for large/bold text and interactive accents, never body copy); no motion is introduced, so there is nothing for `prefers-reduced-motion` to disable — restraint is the animation strategy.

## 4. The Shell (Yggdrasil)

- **Nav**: Home at top; Counter / Weather / Auth Required move under a `Template` group label (FluentUI nav grouping) so the demo pages read as deliberate exhibits, not leftovers. Register/Login links leave the nav entirely; once authenticated, identity affordances live in the header (existing user-menu story, unchanged by this note).
- **Footer**: FluentUI promo links deleted. Replaced by one thin line: platform name and informational version (assembly informational version, already available to the host) — quiet, `font.size.xs`, `color.neutral.500`.
- **Header**: keeps the product title on the amber brand surface; no other change in this pass.

## 5. Naglfar

One content addition (token authoring — exempt from the spec cycle per Naglfar's own charter, recorded here for the paper trail): the seam gradient as semantic tokens (working names `semantic.bifrost` light/dark, emitted into `norse-design-tokens.css` as `--norse-bifrost-seam`). Components consume the custom property; nobody hand-codes the gradient. Five of the six stops are existing Naglfar hues (danger → primary → warning → success → info); the terminal violet (`#6d5bd0` light / `#8b7ae0` dark) is the **one net-new hue** this note introduces, entering `color.json` as a `violet` ramp alongside the others — a rainbow needs its last band, and the palette had none. The provisional-palette caveat in Naglfar's CLAUDE.md stands — if the palette is ever rebranded, the seam re-derives from the replacement hues by the same six-stop rule.

## 6. Placement and Sequencing

- **Gate pages and their stylesheet**: Heimdall `AuthN.Components.FluentUI` — the pages already live there; the gate layout component (`@layout` for auth routes, no shell) and the shared auth stylesheet land beside them.
- **Shell changes**: Yggdrasil `Hosting.Web.Components` (nav, footer, layout).
- **Seam tokens**: Naglfar `tokens/color.json` + build.
- **Sequencing**: the visual retrofit **follows** the validation retrofit (companion spec) — both rewrite the same two pages, and mechanism-then-skin keeps each diff reviewable. Naglfar's token addition has no dependency and can land any time; Yggdrasil's shell tidy-up is independent of both.

## 7. Non-Goals

- Validation behavior of any kind — companion spec's law.
- The component lift-and-shift; MAUI app chrome; the Stories host's look.
- New fonts, new palettes, motion systems, marketing copy beyond the strings in §3.
- Rebranding — the fork story (`Norse` → brand) is untouched; every string here rides the existing brand-injection seams.

## References

- `../../Heimdall/specs/2026-08-06-blazor-validation-composition-design.md` — mechanism companion.
- `../../Naglfar/specs/2026-07-09-style-dictionary-tokens-design.md` — the token pipeline the seam rides.
- `2026-07-11-blazor-component-architecture-design.md` — FluentUI theming architecture (accent seed, neutral derivation).
- Session artifacts: browser-companion comps under `.superpowers/brainstorm/` (Bifröst workspace, untracked).
