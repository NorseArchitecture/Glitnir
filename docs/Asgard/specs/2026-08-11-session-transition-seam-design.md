# Session transition seam — design

**Status:** Approved design, 2026-08-11, revised same day across three review
passes (see §10). Supersedes the brainstorming brief
(`../../navigation-transition-seam-brainstorm.md`). The implementation plan
follows in `../plans/` and names `superpowers:subagent-driven-development`
paired with `superpowers:test-driven-development`.

**Inputs:** `../../../../Bragi/KNOWN-ISSUES.md` (the nested-doll re-diagnosis),
`../plans/2026-08-10-form-validation-hoist.md` (the validation hoist that closed
the two paths to the ignition without closing the ignition), `../../house-rules.md`,
`../../the-two-unions.md`.

---

## 1. The concept, precisely

Three distinct things were being treated as one, and the design is scoped by
telling them apart:

1. **A domain-wide principal transition** — who the user is changed (sign-in,
   sign-out, 2FA completion, external-login completion). A server-side fact.
2. **A transition of an interactive runtime** — a Blazor circuit or WASM
   runtime is holding a principal that just went stale and must be
   re-established under the new identity.
3. **A forced browser document load** — `NavigateTo(uri, forceLoad: true)`, a
   transport mechanism.

Today, (2) is expressed as (3) at interactive call sites, and that transport
mechanism is fatal inside BlazingStory's canvas iframe — a real document load
boots the entire catalog nested inside the preview pane (see the KNOWN-ISSUES
entry). **`ISessionTransition` names (2):** an interactive component announcing
"the principal changed; re-establish this session at the server-resolved next
hop." The host decides what stands behind it — in production, (3); in the story
catalog, suppress-and-record.

Not every (1) is a (2): Himinbjörg's scaffold pages (`LoginWith2fa`,
`LoginWithRecoveryCode`, `ExternalLogin`, the `RefreshSignInAsync` management
pages) perform principal transitions under static SSR and complete them with
genuine HTTP redirects — a real document response, no live runtime holding a
stale principal, nothing for this seam to fix. They adopt the seam if and when
they port to Heimdall under the existing injection-clean placement rule, not
before. And not every navigation after an auth operation is a (2) at all:
Register's handler never signs anyone in (§5), so its navigation is ordinary.

This seam names something that was always true at its call sites. It is not a
test-induced abstraction: the story catalog merely exposed that an unnamed
concept was being performed with a raw transport call in the one place the
transport behavior is fatal.

Settled and not to be reopened without cause:

- **No environment flag.** No `IsStoryMode`, no story-runtime boolean, and no
  honored-vs-suppressed report through the return channel — that would be the
  same flag readmitted by the back door. The seam expresses behavior; the host
  chooses the behavior; suppression is indistinguishable at the call site.
- **The hazard this design closes is the forced document load.** Not
  navigation in general: a soft navigation inside the canvas is a boring
  wrong-render — exactly the degraded state KNOWN-ISSUES names as the durable
  fix's success criterion ("a pinning gap degrades to a boring wrong-render
  instead of re-booting the catalog") — tolerated and asserted (§6), never a
  catalog reboot. A second seam to suppress ordinary navigation was considered
  and rejected: it is the general-navigation-wrapper shape the adoption ruling
  already refused, and it would dilute the role the name claims.
- **For the seam, suppress and record — not "navigate somewhere harmless."**
  A catalog-specific destination still tears down the state the story exists
  to display and invents a concept the scenario doctrine already rejects.
  Recording is what keeps the suppressed case assertable.

## 2. The contract — `ISessionTransition` (Heimdall)

Declared in `AuthN.Components` — the gate's own WASM/MAUI-safe headless
assembly — beside the rest of the headless authn machinery. `NavigationResult`
comes from Asgard's `Abstractions.Contracts`, already a dependency.

**This reverses the brainstorming brief's placement constraint, with cause**
(third review pass, 2026-08-11): the brief fixed the contract in Asgard's
`Abstractions.Components` on the theory that the seam is platform law. It is
not — a principal transition is something only the gate can perform. No other
realm changes who the user is; Himinbjörg's remaining Razor surface is
temporary (every page ports to Heimdall under the placement rule); and the
platform-wide sweep confirms every forced reload in the workspace is the authn
story (Heimdall's three sites plus `RedirectToLogin`, §5). A platform seam
with exactly one possible author is realm law wearing the wrong clothes —
Heimdall declares it, Heimdall implements it (§4), and the story host swaps it
(§6).

```csharp
namespace Norse.AuthN.Components;

/// <summary>
///     The principal changed — re-establish this interactive session at the server-resolved next
///     hop. Components performing a principal transition (sign-in, sign-out) request the transition
///     here instead of touching <c>NavigationManager</c>; the host decides what stands behind it.
/// </summary>
public interface ISessionTransition
{
	/// <summary>Begins the transition. Completion, if any, is the next document load's concern.</summary>
	void Begin(NavigationResult result);
}
```

Shape rulings:

- **Takes the domain record, not a string.** `NavigationResult` is the
  platform's "you're done here; go there" concept and every call site already
  holds one. A `string` parameter would re-open the client-cooked-URL door the
  record's own doctrine closes. With the Logout rework in §5, every `Begin`
  call site passes a **server-resolved** record — the "no default of its own to
  apply" doctrine holds at the seam with no exceptions to footnote.
- **Returns nothing.** After a real transition the circuit is tearing down;
  nothing downstream can meaningfully consume a result, and any report of
  honored-vs-suppressed is the banned environment flag. Assertability belongs
  to Bragi's recorder (§6), not the caller.
- **Two unions:** the contract rhymes with **neither**, deliberately. A one-way
  request with no **domain** failure arm — there is no expected-failure state
  to represent, so nothing to carry in a union. Exceptional failures (a
  throwing `NavigationManager`) propagate to the circuit's error boundary,
  the same doctrine §3 states for the bases.
- **`Begin`** is honest about mechanism-neutral semantics: the seam starts the
  transition; in production the next document load completes it, and in the
  catalog a transition that begins and never completes is exactly the truth.

## 3. The non-form sibling — `OutcomeComponentBase` (Asgard)

`OutcomeFormComponentBase.SubmitAsync` exists so a form declares only the
success continuation and the machinery owns the `Failed` render. A non-form
outcome consumer (Logout's confirm action, §5) had no equivalent rung, which is
why Logout hand-rolled its own outcome match, and why a silent `Failed => "/"`
fallback could exist there at all. Asgard therefore gains the sibling:

```csharp
namespace Norse.Abstractions.Components;

public abstract class OutcomeComponentBase : AsyncComponentBase
{
	/// <summary>The failure of the last dispatch, rendered by the page's markup. Null until a dispatch fails.</summary>
	protected Problem? Problem { get; private set; }

	/// <summary>True while a dispatch is in flight — bind to the trigger's disabled state.</summary>
	protected bool IsDispatching { get; private set; }

	protected Task DispatchAsync<T>(Func<CancellationToken, Task<Outcome<T>>> call, Action<T> onSuccess)
		where T : notnull;
	protected Task DispatchAsync<T>(Func<CancellationToken, Task<Outcome<T>>> call, Func<T, Task> onSuccess)
		where T : notnull;
}
```

Same doctrine as `SubmitAsync` minus the form machinery — no `EditContext`, no
validator gate; `Failed` lands in the protected `Problem` for the markup to
render instead of `ApplyServerErrors`; exceptions propagate to the circuit's
error boundary deliberately. The state rules are explicit, not implied:

1. **`Problem` clears when a dispatch starts** — a later successful dispatch
   never renders a stale failure.
2. **Concurrent dispatch is rejected** — `IsDispatching` guards re-entry the
   same way `IsSubmitting` does; a second call while one is in flight returns
   without dispatching.
3. **Cancellation is checked after the awaited call, before either
   continuation** — disposal during the service call means neither `onSuccess`
   nor a `Problem` write runs. The component is gone; there is nothing to
   render onto and no continuation worth running.
4. **A result arriving after disposal writes no result state** — `Problem`
   stays untouched and the continuation never runs. Rule 3 is the mechanism;
   this is the law it enforces. The in-flight guard still releases in
   `finally` — re-entrancy bookkeeping, deliberately exempt from this rule.

**`SubmitAsync` gets rule 3 in the same change.** Today it checks cancellation
before dispatch but not after the awaited call
(`OutcomeFormComponentBase.SubmitAsync`), so disposal during the service call
can still run `onSuccess`. Both bases obey the same law after this effort —
"carries over" is true because it is made true in both places.

## 4. The production implementation (Heimdall)

The gate owns its own reload. `AuthN.Components` carries, beside the contract:

```csharp
sealed class ForceLoadSessionTransition(NavigationManager navigation) : ISessionTransition
{
	public void Begin(NavigationResult result) =>
		navigation.NavigateTo(result.NextUrl, forceLoad: true);
}
```

Internal, sealed, named for its mechanism (contracts name the role,
implementations name what distinguishes them — the `Persistence.EntityFramework`
pattern), with a public `AddNorseSessionTransition()` extension registering it
scoped (matching `NavigationManager`'s lifetime). Yggdrasil's web server and
WASM client hosts call it; the MAUI host calls it when it lands; Bragi's story
host never calls it — `AddNorseStoryFakes()` registers the recorder instead
(§6). No Midgard involvement anywhere: "Asgard declares, Midgard embodies"
governs platform law, and this is realm law — declared, embodied, and enforced
(§7) around one assembly.

## 5. Adoption (Heimdall)

The seam's interactive principal-transition call sites today are **Login and
Logout** — not Register, whose handler
(`../../../../Himinbjorg/src/Identity.Web.Server/RegisterHandler.cs`) creates
the user and returns `/Account/Login` without ever signing anyone in. No cookie
changes; no session transitions; Register's `forceLoad` was scaffold cargo cult
(the scaffold's Register signs in — ours does not).

- **`Login`:** `@inject ISessionTransition SessionTransition` replaces the
  `NavigationManager` inject; the success continuation becomes
  `result => SessionTransition.Begin(result)`. Nothing else changes —
  `SubmitAsync`'s gate, `IsSubmitting`, and the failure render are untouched.
- **`Register`:** keeps `NavigationManager` and drops `forceLoad` — the
  continuation becomes a soft `Navigation.NavigateTo(result.NextUrl)`. Ordinary
  navigation, no seam, honestly named. (A future email-verification flow
  changes the destination, not this shape — registration still signs nobody
  in. That flow is a separate discussion; see §9.)
- **`Logout` becomes click-to-confirm.** The state-changing GET dies: the page
  stops signing out from `OnInitializedAsync` and instead renders a confirm
  button; the click dispatches `IAuthenticationService.Logout` and the success
  continuation begins the session transition. The page adopts
  `OutcomeComponentBase`: declare-success-only, `Failed` renders in place
  (truthful — sign-out failed, you are still signed in) via headless markup on
  the scaffold alert classes. The `Failed => "/"` fallback is **deleted, not
  relocated**: a failed sign-out means the principal did not change, so
  beginning a session transition would be a lie by the seam's own definition.

```razor
@page "/Account/Logout"
@inherits OutcomeComponentBase
@inject IAuthenticationService AuthenticationService
@inject ISessionTransition SessionTransition

@if (Problem is not null)
{
	<div class="alert alert-danger">Sign-out failed — you are still signed in.</div>
}

<button class="btn btn-primary" disabled="@IsDispatching" @onclick="HandleLogoutAsync">Log out</button>

@code {
	Task HandleLogoutAsync() =>
		DispatchAsync(ct => AuthenticationService.Logout(ct),
			result => SessionTransition.Begin(result));
}
```

**The web-canonical logout path is unchanged.** The navigation menu's
antiforgery-protected POST to the mapped `/Account/Logout` endpoint
(`../../../../Yggdrasil/src/Hosting.Web.Components/Layout/NavMenu.razor`,
`../../../../Himinbjorg/src/Identity.Web.Server/IdentityComponentsEndpointRouteBuilderExtensions.cs`)
signs out and HTTP-redirects without ever rendering this page; it stays. The
reworked page is the uniform-authn logout surface — the component-driven path
WASM and MAUI can use — no longer a state-changing GET shadowing the POST.

Heimdall's component tests substitute `ISessionTransition` (NSubstitute) and
assert `Begin` received the expected `NavigationResult`; Register's tests
assert the soft navigation (no forced load); the Logout tests cover the
confirm-click flow, the Failed-renders case, and that a bare render performs no
sign-out.

**`RedirectToLogin` (Yggdrasil, `Hosting.Web.Components`) drops its forced
reload in the same effort.** It is the workspace's only forceLoad site outside
Heimdall, and it fires when no principal changed — an unauthenticated visitor
being pointed at the gate — so the seam would be dishonest there and the
forced reload is scaffold cargo cult (our Login renders interactively on every
host). It becomes a soft `NavigateTo` preserving the return URL, verified in
the same browser pass as Register (§9).

Scaffold pages in Himinbjörg (`LoginWith2fa`, `LoginWithRecoveryCode`,
`ExternalLogin`, the management pages) are **explicitly out of this rollout**
(§1) — they adopt the seam at port time, page by page, under the existing
placement rule.

## 6. The catalog (Bragi) — suppress, record, assert

`RecordingSessionTransition` — non-public, beside the existing fakes —
implements `Begin` by appending the `NavigationResult` to an inspectable
`IReadOnlyList<NavigationResult>`. `AddNorseStoryFakes()` registers it as the
story host's `ISessionTransition`. The canvas stays clean: no badge, no
indicator — the recorder exists to be asserted, and the spec says so
deliberately rather than leaving it an implementation detail.

Test doctrine in `DrivenStoryNavigationTests`:

- The characterization test inverts around **Login**, the remaining forced-load
  ignition: an unpinned driven Login story that reaches `Success` now asserts
  exactly one recorded transition to the server-resolved hop **and** an empty
  `Navigation.History`. Pin loss stays a loud CI failure; the canvas stops
  paying for it with a nested catalog.
- **Register's** unpinned-story test documents the new truth: a soft
  navigation recorded in `Navigation.History` with `ForceLoad` false — a
  boring wrong-render in the canvas, never a document load, never a nested
  doll.
- Pinned-failure stories additionally assert the recorder is empty — a failure
  scenario that transitions is a new bug class this catches.
- Every `Navigation.History.ShouldBeEmpty()` assertion on non-navigating
  stories stays. From adoption day forward it locks against raw forced-load
  reintroduction — the tests' job until NORSE074 takes it over at compile
  time, and cheap defense in depth after.

**Logout becomes story-eligible.** Click-to-confirm makes the page visual, and
Bragi's catalog rule is that every WASM-clean, visually-rendering component
appears. Logout gains a story (the confirm state, and a pinned sign-out-failed
state driven by a button click); the suppressed-transition success state
renders identically to the confirm state, so it is asserted in the driven-story
tests rather than staged as a story that would show nothing. Bragi's
CLAUDE.md — which cites headless Logout as the canonical no-story example —
updates in the same gate, boy-scout law.

## 7. Enforcement (Svartálfheim, in scope) — NORSE074 / NORSE075

A seam nobody is required to use is a naming convention with extra steps. Both
rules land in `Architecture.Analyzers` as `NotConfigurable` errors, IDs per the
block ledger (NORSE070–079 is the architecture-law block; the ledger lives in
`Primitives.Analyzers`' `Diagnostics.cs` header and updates in the same
change):

- **NORSE074 — forced document load outside the seam's implementation.**
  Border law sharpened to a single type: the only absolved call site is
  `ForceLoadSessionTransition` itself — matched by **both** its full type name
  (`Norse.AuthN.Components.ForceLoadSessionTransition`) **and** its assembly,
  so no other assembly can mint the name and the gate's own *pages* (Logout
  lives in the same assembly) are convicted like everyone else. An
  assembly-wide exemption would be the rejected interface opt-out widened to
  an assembly; a type-name-only exemption would be mintable anywhere. Both
  keys together are unforgeable: assembly identity is build-injected, and a
  second type of that name in that assembly cannot compile.
  **Enforcement posture is fail-loud: anything the analyzer cannot prove soft
  is convicted.** The `forceLoad` argument convicts unless it is provably the
  constant `false` (the omitted-argument default counts as constant `false`;
  a variable, negation, or method result convicts). The `NavigationOptions`
  overload demands an inline initializer the analyzer can read — an options
  value built elsewhere convicts outright, and an inline initializer convicts
  unless `ForceLoad` is absent or provably `false`. A false positive costs
  the author a constant; a false negative costs the platform the hazard —
  priced deliberately. A future legitimate non-authentication forced reload
  gets its own named seam and its own exemption amendment rather than lying
  about being a session transition.
- **NORSE075 — `OnValidSubmit` on an `EditContextFor`-bound form.** An error
  pointing at `OnSubmit` + `FormValidator`, detected over the razor-generated
  C# (the `EditForm`'s `EditContext` argument is an `EditContextFor`
  invocation). **Deliberately qualified, not blanket:** Himinbjörg's scaffold
  forms (13 files carrying `OnValidSubmit` today, DataAnnotations-validated,
  outside the Norse validation seam) are untouched and come under the rule
  naturally as they port to the seam. `OnValidSubmit` on a seam-bound form runs
  EditForm's synchronous pass ahead of `SubmitAsync`'s gate and skips async
  rules entirely — the analyzer gap the validation hoist left open.

**NORSE075's proof obligation:** its tests must run against **actual
Razor-generated output** — real `.razor` sources compiled through the Razor
source generator inside the analyzer test harness — never hand-authored C#
approximations of render-tree calls. Associating `OnValidSubmit` with the
`EditContextFor(...)`-bound `EditContext` attribute in the generated builder
calls is the difficult part of the rule; a test that fakes the generated shape
proves nothing about it.

Enforcement ships **last** in the rollout (§8): the analyzer's fix points at the
seam, so the seam must exist — and every violation must already be converted —
before the rules turn on. No red builds mid-train.

## 8. Testing and ship order

Test-driven per realm, red first: Svartálfheim's analyzer tests (NORSE075
against real Razor-generated output, per §7's proof obligation); Heimdall's
delegation test (a recording navigation manager captures the forced load);
Bragi's recorder tests and the inverted characterization test; Heimdall's
swapped component tests, including the new Logout confirm-flow and
no-signout-on-render facts and the `SubmitAsync`/`DispatchAsync` post-await
cancellation facts. Ship gates in strict dependency order, each behind its own
gate (PR merged, CI green, tagged, published):

1. **Asgard** — `OutcomeComponentBase` and the `SubmitAsync` post-await
   cancellation fix.
2. **Heimdall** — `ISessionTransition`, `ForceLoadSessionTransition`,
   `AddNorseSessionTransition()`; Login adopts the seam; Register drops
   `forceLoad`; Logout becomes click-to-confirm.
3. **Yggdrasil** — web server + WASM client host registration;
   `RedirectToLogin` drops its forced reload.
4. **Bragi** — recorder, `AddNorseStoryFakes()` wiring, test inversion, the new
   Logout story, CLAUDE.md canonical-example update.
5. **Svartálfheim** — NORSE074/NORSE075 turn on, zero violations remaining.

Himinbjörg ships nothing in this train — its scaffold pages are out of scope by
design (§1, §7), and the qualified NORSE075 convicts none of them.

## 9. Carried forward

- **The nested-doll browser verification is complete** — KNOWN-ISSUES records
  it browser-confirmed closed 2026-08-10: two independent Chromium runs
  against the released package graph proved both mechanisms closed, with the
  historical "cold load reliably works" contradiction explicitly left
  unresolved (no pre-fix package was rerun; possibly confounded with the
  native-submit race). That completed investigation is KNOWN-ISSUES' record,
  not this spec's. What KNOWN-ISSUES leaves **live** is the architectural
  hazard this design exists to close: `Success` is still the value a released
  pin restores and still the only scenario that navigates destructively.
- **One new browser check is legitimately pending and belongs to this
  effort:** confirming the two soft navigations (Register, `RedirectToLogin`)
  carry no hidden dependency on the old forced reloads. It rides the rollout
  (after Yggdrasil's gate) and its findings land in KNOWN-ISSUES alongside the
  hazard-closure amendment after Bragi's gate.
- **The return URL is carried but never honored** (surfaced in review,
  2026-08-11, pre-existing): `RedirectToLogin` forwards `returnUrl` to the
  login page, but `LoginRequest` has no return-URL member, Login never reads
  the query, and `LoginHandler` resolves every successful login to the app
  root. Honoring it end to end is new wire surface plus a server-validated
  local-redirect check (open-redirect security) touching Heimdall and
  Himinbjörg — its own spec-first effort, deliberately not smuggled into this
  train. Until then, this train's verification only asserts the query is
  preserved and Login renders there.
- **Registration and verification flow** — a future design-court discussion,
  recorded here so it isn't lost: registration should force email
  verification (the state change belongs to verification, not registration);
  email-provider third-party logins (Google/Microsoft) should skip
  verification and sign in; only social third-party logins should require it.
  Not germane to this seam — registration signs nobody in either way.
- **Theme relocation** (`Infrastructure.Components.Theme`/`.Theme.FluentUI`
  down from Midgard to Yggdrasil — the theme is the first thing a consumer
  rips out for their own Style Dictionary-curated house style) — a future
  curation pass, deliberately not this effort.
- **Scaffold-page ports** (`LoginWith2fa`, `LoginWithRecoveryCode`,
  `ExternalLogin`, management pages) adopt the seam page by page under the
  existing placement rule as they move to Heimdall — each port is where that
  page's principal transition becomes an interactive one.

## 10. Review record (2026-08-11)

Six objections, all verified against the code and sustained; the revisions
above are their disposition:

1. Register is not a principal transition (handler never signs in) → dropped
   from the seam, soft navigation (§5).
2. "Today's complete set" ignored existing principal transitions → the
   three-way taxonomy scopes the seam to interactive-circuit transitions;
   scaffold SSR pages adopt at port time (§1, §5).
3. The proposed Logout work missed the production POST path and left a
   state-changing GET → POST stays web-canonical; the page becomes
   click-to-confirm (§5).
4. Blanket NORSE075 would convict 13 Himinbjörg scaffold files with no
   migration in the rollout, as a non-configurable error → qualified to
   `EditContextFor`-bound forms (§7).
5. NORSE074's implements-the-interface exemption was an opt-out disguised as
   enforcement → reshaped as border law, Midgard/Yggdrasil only (§7).
6. The new base overstated inherited cancellation behavior (`SubmitAsync`
   checks before dispatch, not after the await) → explicit state rules, and
   both bases get the post-await check (§3).

Second pass, three follow-ups, all sustained:

1. Register's soft navigation contradicted the "any navigation is fatal"
   invariant → the invariant is narrowed: the forced document load is the
   hazard this design closes; a soft navigation in the canvas is the boring
   wrong-render KNOWN-ISSUES itself names as the durable fix's success
   criterion, asserted rather than suppressed (§1). A second
   ordinary-navigation seam was rejected as the general-navigation-wrapper
   shape the adoption ruling already refused.
2. "The seam cannot fail" was inaccurate → no **domain** failure arm;
   exceptional failures propagate to the error boundary (§2).
3. The Playwright status was stale — the nested-doll verification completed
   2026-08-10 → §9 now separates the completed investigation (KNOWN-ISSUES'
   record) from the one new pending check this effort owns (Register's soft
   navigation).

Plus one proof obligation added: NORSE075's tests run against real
Razor-generated output, never hand-authored approximations (§7).

Third pass (design ruling, 2026-08-11): the platform-wide sweep found every
forced reload on the platform is the authn story — Heimdall's three sites plus
Yggdrasil's `RedirectToLogin`, nothing else. Ruled: the seam is realm law, not
platform law. The contract and production implementation move from
Asgard/Midgard to Heimdall's `AuthN.Components` (§2, §4), reversing the
brief's placement constraint with cause; Midgard exits the design entirely;
NORSE074's jurisdiction narrows to `Norse.AuthN.Components` alone (§7); the
train shortens to five gates (§8); and `RedirectToLogin` drops its forced
reload as scaffold cargo cult, soft-navigating pending the same browser
verification as Register (§5, §9).

Fourth pass (enforcement review, 2026-08-11), all sustained: NORSE074's
assembly-wide exemption was the rejected interface opt-out at assembly blast
radius (the gate assembly contains pages) → narrowed to the
`ForceLoadSessionTransition` type itself, keyed on type name AND assembly
(§7). Constant-only detection left variable and prebuilt-options evasions →
the fail-loud posture: anything not provably soft convicts (§7). The plan's
browser verification had drifted later than this spec orders → realigned to
the Yggdrasil and Bragi gates. Rule 4 of §3 clarified: the dispatch guard's
`finally` release is re-entrancy bookkeeping, exempt from the
no-result-state-after-disposal law. Plan-level defects (bUnit history
ordering, SDK-hermetic build proof, diagnostic meta-test coverage, Task 12
precision) fixed in the plan directly.

Fifth pass (2026-08-11): the plan's browser check asserted login honors
`returnUrl`, which the system has never done (no wire member, no query read,
handler resolves to root) — a pre-existing gap the forced redirect masked by
carrying the query it dropped. Verification narrowed to query preservation +
correct render; honoring it end to end recorded above as its own spec-first
effort. Also: case-inclusive pre-flight sweep, the implementation XML doc
aligned to the type-level exemption, and the NORSE079 meta-test omission
adopted as a boy-scout fix while editing `DiagnosticsTests`.
