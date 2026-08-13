# Navigation transition seam — brainstorming brief

**Status:** Input to a brainstorming session, not a decision. Written 2026-08-11 to bootstrap a fresh context. Superseded by the spec it produces (`Asgard/specs/`).

**Start here:** you are in Bifröst, the working root. Read this file, then the two references in §7 before asking anything. Everything below is settled fact unless marked as an open question in §5.

---

## 1. The problem in one paragraph

Heimdall's `Login` and `Register` complete a successful submit by calling
`Navigation.NavigateTo(result.NextUrl, forceLoad: true)`. That is a real document
load. Performed inside BlazingStory's canvas iframe — which is where every Bragi
story renders — it navigates the iframe to the app root and boots the entire
catalog shell *inside the component preview pane*. The catalog appears nested
inside itself. We need a seam that keeps the forced reload in production and
makes it inert in the story catalog, without teaching components what
environment they are in.

## 2. Why the reload exists (do not "fix" it away)

`forceLoad: true` is not stylistic and not a bug. After a successful sign-in or
registration the authentication cookie has changed, and the running circuit is
still holding the old principal. The forced reload establishes a fresh circuit
under the new identity. Removing it, or downgrading it to a soft navigation,
breaks authentication.

This matters for the design: the behavior already *is* a distinct domain
concept — "the principal changed, re-establish the session here" — that
currently has no name and is therefore expressed as a transport primitive. The
seam names something that was always true. It is not a test-induced abstraction.
Say this plainly in the spec; the weaker framing ("we added an interface so
stories don't break") invites re-litigation later.

## 3. What already shipped, and what it did and did not fix

A form-validation hoist landed across four realms on 2026-08-10/11. Read
`Asgard/plans/2026-08-10-form-validation-hoist.md` for the full narrative. In short:

- Asgard's `OutcomeFormComponentBase.SubmitAsync` now owns the validation gate
  for every form on the platform, refuses to dispatch a form with no
  `FormValidator` attached, and returns `Task<bool>`.
- `FormValidator` fixes Blazilla's `AsyncMode` on and does not expose it.
- Heimdall's hand-rolled validation guard is deleted; `OnValidSubmit` became
  `OnSubmit` on every form.
- Shipped as Asgard v0.0.26, Heimdall v0.0.15, Mímir v0.0.6, Bragi v0.0.10.

**That closed the two paths that reached the forced reload** — a shadowed
`editContext.ValidateAsync()` that returned `true` for everything, and scenario
pin loss restoring the ambient default. It did **not** close the reload itself.
The ignition is still live and still one mistake away: `Success` is
simultaneously the scenario a released pin restores *and* the only scenario that
navigates. `Bragi/KNOWN-ISSUES.md` is the full re-diagnosis and is the single
best document to read before designing this.

Bragi's `DrivenStoryNavigationTests` currently pins this: every driven story
asserts `BunitNavigationManager` recorded no navigation, and
`An_unpinned_driven_story_force_navigates_which_is_what_boots_the_catalog_nested`
is a deliberate characterization test documenting the live hazard.

## 4. The proposal on the table

An injected navigation-transition abstraction owned by Asgard:

- **Production implementation** delegates to `NavigateTo(uri, forceLoad: true)`.
- **Bragi implementation** suppresses the transition and records it, leaving the
  story canvas in place.
- **Realm components** request the transition instead of touching
  `NavigationManager` directly.
- Ordinary in-app links and BlazingStory's own catalog navigation are untouched.

This is the same transport-dumb seam the platform already uses everywhere else:
a component declares the contract, the host decides what stands behind it. It
should feel unremarkable next to `I{Context}Service`.

**Settled going in — do not reopen without cause:**

- **No environment flag.** No `IsStoryMode`, no "story runtime" boolean threaded
  through the UI. The seam expresses behavior; the host chooses the behavior.
- **Suppress and record, not "navigate somewhere harmless."** Any navigation
  tears down the canvas and re-renders something else — a story-specific
  destination is still fatal to the state the story exists to display, merely
  less spectacular. It also invents a concept that exists only for the catalog,
  which the scenario doctrine already rejects. Recording is what keeps the
  suppressed case assertable.

## 5. Open questions for the brainstorm

1. **The name.** `RestartAt(uri)` names the mechanism; house rules want the role.
   The concept is "the principal changed, re-establish the session here."
   `ISessionTransition`, `IPrincipalTransition`, others — argue it.
2. **The contract's shape.** One method taking a URI? Does it take the whole
   `NavigationResult`? Is there a return value, and does anything downstream
   need to know whether the transition was honored or suppressed?
3. **Enforcement.** The proposal as written makes the right thing *available*
   without making the wrong thing *fail* — nothing stops a page author from
   calling `NavigateTo(..., forceLoad: true)` directly. That is a convention,
   and conventions are what this whole effort has been replacing. See §6.
4. **Scope of adoption.** Login and Register are the two live call sites.
   Logout also navigates. Does every realm component adopt the seam, or only
   the ones performing a principal transition?
5. **What Bragi's recorder is for.** With suppression, an unpinned driven story
   stops booting the catalog and starts silently rendering the *success* state
   instead of its pinned failure state — boring, but still wrong and still
   quiet. Making the recorder assertable is what converts that back into a loud
   failure. Decide deliberately rather than leaving it an implementation detail.

## 6. The enforcement question, stated properly

The platform's stated doctrine is the pit of success and compile-time over
runtime. A seam nobody is required to use is a naming convention with extra
steps. There is an existing, already-open analyzer gap of exactly the same
shape: nothing prevents a page author from writing `OnValidSubmit` on a form
bound via `EditContextFor`, which would run Blazor's synchronous pass ahead of
`SubmitAsync`'s gate and skip async rules entirely. Both rules want the same
Svartálfheim pass:

- ban `forceLoad: true` in component assemblies, point at the seam;
- ban `OnValidSubmit` on an `EditContextFor`-bound form, point at `OnSubmit`.

Whether that analyzer is in scope for this spec or is a sibling spec is a real
decision, not a foregone one — but shipping the seam with no enforcement and no
recorded plan for it would leave the hazard moved rather than closed.

## 7. Constraints that bind the design

- **Placement.** The contract must live in Asgard's `Abstractions.Components`,
  **not** `Abstractions.Web.Server` where `IDeferredSignIn` sits — components
  ship to WASM and MAUI, and a server-only assembly is unnameable from there by
  construction. `Abstractions.Components` takes no ASP.NET Core server, EF Core,
  or server-side infrastructure references; that wall is load-bearing.
- **Who implements what.** Production implementation belongs to Midgard
  (`Norse.Infrastructure.*`) or the host, not to Asgard. Asgard declares; it
  never implements. The Bragi implementation lives beside the stories, non-public,
  the same as the existing fakes.
- **House rules** (`house-rules.md`) — read it. Relevant: name the role not the
  mechanism, `Dto` banned, sealed by default, no silent fallbacks, usings hoisted,
  and the 2026-08-10 extension-style carve-out for shadowing instance methods.
- **Two discriminated unions** (`the-two-unions.md`) — if the contract returns
  anything, know which union it rhymes with and why, or deliberately neither.
- **Relative paths only** in every document produced. No machine-local absolute
  paths, ever.
- **Do not commit.** Stage, show the diff, stop. The human commits. This holds
  even when a skill's flow includes a commit step.

## 8. Reference material

| Subject | Where |
|---|---|
| The re-diagnosed nested-doll issue | `../Bragi/KNOWN-ISSUES.md` |
| The validation hoist that preceded this | `Asgard/plans/2026-08-10-form-validation-hoist.md` |
| House style, including the shadowing carve-out | `house-rules.md` |
| `Result<T>` vs `Outcome<T>` doctrine | `the-two-unions.md` |
| MSBuild / packaging / crossings doctrine | `the-runes.md` |
| The live call sites | `../Heimdall/src/AuthN.Components.FluentUI/Login.razor`, `Register.razor` |
| The form seam they ride | `../Asgard/src/Abstractions.Components/OutcomeFormComponentBase.cs` |
| The tests that pin the hazard today | `../Bragi/tests/DesignSystem.Stories.Tests/DrivenStoryNavigationTests.cs` |
| Story fake / scenario doctrine | `Bragi/specs/2026-08-08-story-fake-scenario-pattern-design.md` |

## 9. Process

Spec-first: brainstorm → spec → plan, human greenlight at every transition, none
of them inferred from momentum. This changes Asgard's public surface, so it does
not skip the design court. The spec lands in `Asgard/specs/`; the plan that
follows lands in `Asgard/plans/` and names
`superpowers:subagent-driven-development` paired with
`superpowers:test-driven-development`.

**One thing still unverified that could change this design.** No browser run has
yet confirmed the validation hoist actually fixed the nested doll, and
`KNOWN-ISSUES.md` records a loose end: the original investigation claims a cold
load "reliably works," which never fit the shadowed-validate mechanism, since
that path did not depend on scope disposal. If the Playwright verification turns
up a third mechanism, it lands here before the spec converges.
