# Story-Fake Scenario Pattern — Pinned States, Stateless Fakes, Catalog Taxonomy

**Date:** 2026-08-08
**Status:** Approved design, ready for planning
**Owner:** Buvy
**Realm:** Bragi (`Norse.DesignSystem.Stories`), with one doc-comment correction in Asgard

---

## 0. Why This Comes Up Now

Bragi's `FakeAuthenticationService` landed 2026-08-07 with a deliberately deferred design session attached: expand the fake to a realistic error-state inventory and pin it with contract-level parity tests. This is that session, run product-owner-first: the requirement is "a way to mock the gRPC service in Bragi for BlazingStory," with the solution space explored rather than prescribed.

The conceptual anchor the whole design hangs on: **a story is a component pinned in one named state.** "Login / Locked Out" is a bookmarkable page that renders identically every time — sendable to a designer, screenshot-able, never dependent on typing magic inputs or on what the visitor browsed first. The fake's job is making states reachable and deterministic, not simulating a backend. The catalog is a deployable (WASM bundle, dockerized, pushed to GHCR), so bundle size, AOT-cleanliness, and determinism are production concerns — a very different fitness function than a test project's.

## 1. Decisions in Force

### 1.1 The scenario seam

A story declares which state it pins; the fake obeys. Three pieces, all in Bragi — story tooling never ships downstream, and Bragi already owns the fakes:

- **A scenario enum per fake family.** `AuthenticationScenario { Unspecified = 0, Success = 1, InvalidCredentials = 2, LockedOut = 3, NotAllowed = 4, RegistrationConflict = 5, RegistrationValidation = 6, Fault = 7 }`. Explicit integer values, `0` as the unspecified sentinel, real states from `1` — full compliance with the platform enum law, no catalog exemption carved.
- **An ambient holder.** `sealed class Scenario<TScenario>`, registered as a singleton and **constructed with its family's initial value** (`Success` for authentication) — that constructor argument, not the enum's CLR default, is why an unwrapped story renders the happy path. The fake reads `Value`; nothing else touches it. The fake's `switch` throws `InvalidOperationException` on `Unspecified` — unreachable by construction, and loud if construction is ever broken.
- **A wrapper component.** `<ScenarioScope Value="AuthenticationScenario.LockedOut">…</ScenarioScope>` in the story template. Sets the ambient in `OnParametersSet` — every render, so BlazingStory's persistent canvas iframe cannot leak one story's scenario into the next — and resets to the holder's initial value on dispose. Determinism comes from the wrapper's lifecycle, not from hoping.

```razor
<Story Name="Locked Out">
	<Template>
		<ScenarioScope Value="AuthenticationScenario.LockedOut">
			<LayoutView Layout="typeof(GateLayout)">
				<Login />
			</LayoutView>
		</ScenarioScope>
	</Template>
</Story>
```

No reflection, no proxies, no packages — a `switch` over an enum is the most compile-time-honest scenario dispatch there is.

**Lifetime correction:** the current `AddScoped` registration becomes explicit singletons for both the fake and `Scenario<T>`. WASM makes scoped effectively singleton anyway; say what you mean. `AddNorseStoryFakes()` keeps its signature — the story host stays a dumb composition root.

### 1.2 Catalog taxonomy: surfaces organize by state; widgets keep their own stories

The catalog splits on what kind of thing is being cataloged:

- **Reusable widgets** (`StatusMessage`, `Loader`, `ModelValidationSummary`) get their own story file — parts-bin components someone reaches for independently, whose states are their own states.
- **Flow surfaces** organize by state. A confirmation page is not a catalog component; it is the *succeeded state of its owning surface* that happens to be implemented as a separate `.razor` file. "ForgotPasswordConfirmation" as a top-level sidebar entry is file structure leaking into the catalog; "Forgot Password → Email Sent" is what a designer actually looks for.

Target sidebar shape:

```
Authentication/
├─ Login            Default · Validation Errors · Invalid Credentials · Locked Out · Not Allowed
├─ Register         Default · Validation Errors · Email Taken · Invalid Password
├─ Two-Factor       Locked Out                    ← Lockout.razor page; the 2FA form itself is not yet portable
├─ Forgot Password  Email Sent                    ← only state portable today
├─ Reset Password   Invalid Link · Password Reset
├─ Access Denied    Default                       ← AccessDenied.razor, an authorization page — its own surface
└─ Recovery Codes   Default
Primitives/
├─ Loader
├─ StatusMessage             per-severity states
└─ ModelValidationSummary    harnessed — see below
```

Three placements that must not be conflated:

- **`Login / Locked Out` and the `Lockout.razor` page are different states of different surfaces.** The driven Login story pins the *form feedback* for a `Failed(LockedOut)` outcome (`CategoryDisplay`: "This account is locked out. Try again later."). `Lockout.razor` is a routed page the **two-factor flow** navigates to (`LoginWith2fa` redirects to `Account/Lockout` on 2FA lockout) — it appears as `Two-Factor / Locked Out`, a cheap state of a surface whose form still lives in Himinbjörg. Both exist; neither substitutes for the other.
- **`NotAllowed` is not "Access Denied."** `NotAllowed` is a login *precondition* failure (e.g. sign-in not permitted yet), rendered inline in the Login form — the state is named `Not Allowed`. `AccessDenied.razor` is an *authorization* page (authenticated but forbidden), its own surface in the sidebar.
- **`ModelValidationSummary` cannot stand alone** — it throws by design without a cascaded `EditContext` from an `EditForm` ancestor. Its story prescribes a harness: a story-only wrapper rendering it inside an `EditForm` whose `EditContext` is seeded with model-level messages via a `ValidationMessageStore`. Cheap state, no driver — the harness is the story wrapper.

Pinned states come in two costs, same idiom:

- **Cheap:** the state is a different component — the story template renders `<ForgotPasswordConfirmation />` under the "Forgot Password" title. No scenario, no driver.
- **Driven:** the state is post-submit form feedback — scenario armed via `ScenarioScope`, submit driven per §3.

**The not-yet-ported wrinkle resolves itself.** `ForgotPassword`/`ResetPassword` forms still live in Himinbjörg (not injection-clean yet), so those surfaces exist in the sidebar with only their portable states; when a form ports over, its `Default` story slots in beside states already waiting. Mechanically fine in BlazingStory: the `[Stories("…")]` title string drives grouping, and `TComponent` can be the confirmation component until the surface component arrives.

**Bragi's inclusion rule is rephrased to match:** every WASM-clean, visually-rendering component appears in the catalog — widgets as their own story files, flow-surface components as states under their surface's title. Non-visual components still get nothing (`Logout` remains the canonical absence).

The former parked note about "flow-level login/logout stories" is retired, not deferred — on examination it was a misstatement of exactly this taxonomy decision (confirmation components viewable without walking the flow), which the per-component stories already satisfied and this section now organizes properly.

### 1.3 The fake, rebuilt: a stateless switch over the scenario

`FakeAuthenticationService` becomes a stateless `switch` over the ambient `AuthenticationScenario`, returning one canonical outcome per member — the same `Problem` shapes the real producers build (`LoginHandler` builds model-level `ModelError`s; `RegisterHandler` builds **field-keyed** dictionaries whose keys decide where messages render — a wrong key renders nowhere, reproduced live 2026-08-07). The scenario inventory mirrors what the real flow actually emits (states the UI can be in), not the full `ErrorCategory` enum:

| Scenario | Outcome | Authoritative source |
|---|---|---|
| `Unspecified` | throws `InvalidOperationException` — never a state, per platform enum law | — |
| `Success` | `Ok` (`LoginResult.NextUrl = "/"`, `RegisterResult.Succeeded = true`) | happy path |
| `InvalidCredentials` | `Failed(ModelError(InvalidCredentials, "Invalid email or password."))` | Himinbjörg `LoginHandler` |
| `LockedOut` | `Failed(ModelError(LockedOut, …))` | Himinbjörg `LoginHandler` |
| `NotAllowed` | `Failed(ModelError(NotAllowed, …))` | Himinbjörg `LoginHandler` |
| `RegistrationConflict` | `Failed(Conflict)` with `Errors` exactly: `{ "Email": ["Email 'taken@example.com' is already taken."] }` | Himinbjörg `RegisterHandler` (`FieldFor` maps `DuplicateEmail`/`DuplicateUserName` onto `Email`; stock `IdentityErrorDescriber` text) |
| `RegistrationValidation` | `Failed(Validation)` with `Errors` exactly: `{ "Password": ["Passwords must have at least one non alphanumeric character.", "Passwords must have at least one digit ('0'-'9').", "Passwords must have at least one uppercase ('A'-'Z')."] }` | Himinbjörg `RegisterHandler` (`FieldFor` maps password-policy codes onto `Password`; stock `IdentityErrorDescriber` texts). No `PasswordTooShort`: Heimdall's `RegisterRequestValidator` enforces `MinimumLength(8)` client-side, so a too-short password never reaches the handler through the composed flow — the canonical set is exactly what the proven fixture `"aaaaaaaa"` produces (`RegisterHandlerTests`) |
| `Fault` | `Failed(Fault)` with `CorrelationId` fixed at the catalog constant `0badc0de-0bad-c0de-0bad-c0de0badc0de` | Midgard `ExceptionTranslationBehavior` (mints the id via `Guid.NewGuid()`; `OutcomeServerInterceptor` only transports it) |

The `Fault` correlation ID must be a fixed constant, not a fresh GUID: `CategoryDisplay` renders it in visible text ("… Reference: {id}"), and a per-load GUID would make the pinned story fail the identical-screenshot bar. The obviously-synthetic value is deliberate — it can never be mistaken for a real incident reference. The canonical `Errors` dictionaries above are the pinned shapes §5's parity tests assert verbatim.

`EmailExists` keeps answering "not taken" unconditionally. The `fail@example.com` sentinel survives in the `Default` playground story only — a garnish for live interaction, never the pinning mechanism — documented on a scenario catalog page (§6).

### 1.4 The `InvalidCredentials` ruling (drift found this session)

Asgard's `ErrorCategory.InvalidCredentials` doc comment claims "Vestigial — not actively produced, per the anti-enumeration ruling," while Himinbjörg's `LoginHandler` emits exactly that category. **Ruled: the doc comment is the stale artifact; the working code stands.** Stale docs are always corrected in favor of working code. The anti-enumeration intent, restated correctly: login must never disclose *which* credential failed — one generic `InvalidCredentials` with "Invalid email or password." is precisely right. The `EmailExists` operation (serving the register flow's validator) is a known account-enumeration vector that partially undercuts this; that is an accepted product trade-off, recorded here deliberately, not an oversight.

Consequence: Asgard's enum doc comment is corrected (drop the "vestigial" claim; state the never-disclose-which-credential intent). One doc line; no behavioral change anywhere.

## 2. Rejected Approaches (recorded so they stay rejected)

- **NSubstitute-configured mock.** Castle DynamicProxy is runtime reflection-emit inside a shipping WASM bundle — interpreter-bound, AOT-hostile, against compile-time-over-runtime law. Mechanically dead on arrival regardless: BlazingStory's host is one app-wide DI container, so there is no per-story arrangement — a scenario-switching wrapper around one global substitute is a hand-rolled fake wearing a proxy library as a hat. NSubstitute's value (terse per-test arrangement, received-call verification) only exists in tests; stories assert nothing. NSubstitute remains the platform's test-double library *in test projects*.
- **EF InMemory seeded with known records.** EF Core in the catalog bundle is megabytes plus model-building startup cost; the InMemory provider is the one provider Microsoft says not to use, and platform law already bans mocked-DB tests because fake stores lie about database semantics. Deepest failure: statefulness is anti-catalog — a store the Register story writes to makes the Login story's `EmailExists` depend on browsing order, destroying exactly the determinism pinned states exist for.
- **Hand-rolled mutable store (the no-EF middle ground).** Same statefulness disease without the payload. The pull toward a store always means one of two things is actually wanted: a pinned scenario (this spec) or an immutable fixture (§4, law 2).

## 3. Pinning Post-Submit States: `StoryDriver` and the Spike

`OutcomeFormComponentBase.SubmitAsync` renders failure by `editContext.ApplyServerErrors(problem)` — the error state exists only after a submit, and each page's `EditContext` is private. The one honest way to pin it without touching shipping code is the Storybook play-function idiom: a story-side `StoryDriver` component (sibling of `ScenarioScope`, Bragi-only) that, after first render, fills the form inputs via JS interop (dispatching `change` so Blazor binds), then clicks submit. Blazilla's client validation passes because the driver fills valid-shaped values; the armed scenario makes the fake return `Failed`; `ApplyServerErrors` renders the pinned state.

The rejected alternative — an "initial problem" parameter on `OutcomeFormComponentBase` — is story tooling seeping into what ships. Not proposed, not open.

`StoryDriver` has two modes, both pin-on-load: **`FillAndSubmit`** (fill valid-shaped values, then submit — the server-error states above) and **`SubmitOnly`** (submit the untouched form — `Validation Errors`, where Blazilla's client-side validation *is* the state; no scenario needed, but the submit must still be driven or the bookmarked story initially shows a pristine form). The Register driver's password fixture is `"aaaaaaaa"` — it passes client-side `MinimumLength(8)` so the submit reaches the fake, matching the §1.3 canonical dictionary by construction.

**The plan's first task is a spike** (same discipline as the 2026-07-12 RCL-split spike), and it proves **two** driven stories before further stories build on the driver: `Login / Locked Out` (synchronous client validation) and `Register / Invalid Password` (the asynchronous validation path — Blazilla's `EmailExists` rule runs an async gRPC-shaped call before submit completes, a timing hazard the Login story never exercises).

## 4. Doctrine — the pattern every future fake family follows

1. **Story fakes are stateless scenario responders.** Behavior is selected, never accumulated; no fake holds mutable state, ever.
2. **Data-serving fakes return immutable seed fixtures.** Mímir's future fake hardcodes a canonical handful of countries/currencies in the fake itself. Fixture data is not state — that is precisely why it is allowed. No EF, no store, no TSV parsing in the catalog bundle.
3. **Fakes stay non-public, ship beside their stories, and register only via `AddNorseStoryFakes()`.** The story host remains a pure composition root.
4. **Scenario selection is declarative in story markup via `ScenarioScope`.** Sentinel inputs are playground garnish, never the pinning mechanism.

Mímir's `Reference.Components.FluentUI` follows this idiom when it ships: its own scenario enum (e.g. populated / empty / `NotFound` / `Fault`), its own fake beside its stories, fixtures per law 2. Nothing further is designed for it here, deliberately.

## 5. Testing and CI

This is the behavioral logic Bragi's design-system exemption clause anticipated — it rides the full TDD discipline:

- **Parity tests** pin the fake's emissions verbatim against §1.3's table: every `AuthenticationScenario` member maps to an outcome (`Unspecified` throws, asserted too); each `Failed` carries the expected `ErrorCategory` and the exact `Errors` dictionary — keys included, since `RegisterHandler`'s field-keyed contract is what decides whether a message renders at all; `Fault` carries the fixed catalog correlation constant; `Success` scenarios carry the expected result payloads. Cross-realm string parity with Himinbjörg's handlers cannot be compile-checked from Bragi (a content RCL cannot reference a server realm), so §1.3's table records the authoritative source per scenario and the tests pin Bragi's side; drift is a boy-scout-law concern, stated honestly.
- **`ScenarioScope` lifecycle tests** — set-on-render, reset-to-initial-value-on-dispose — via bUnit, which enters Bragi's test stack with this work.
- **CI gating:** this session is the standing trigger Bragi's CLAUDE.md named. With a real test surface landing, the `gate / build` check becomes required by branch protection.

## 6. Documentation Consequences (required follow-up)

| Document | What changes |
|---|---|
| `Bragi/CLAUDE.md` | Fake section rewritten (scenario pattern, singletons, doctrine pointer here); inclusion rule rephrased per §1.2; the "flow-level stories" parked note retired per §1.2; ungated-CI paragraph replaced per §5; test-surface sentence updated |
| `Bragi/README.md` | Same story at public altitude — boy-scout pair law |
| Bragi scenario catalog page (`Scenarios` story/markdown page) | New: every scenario, its trigger, what it renders, the `fail@example.com` sentinel |
| Asgard `ErrorCategory.cs` | `InvalidCredentials` doc comment corrected per §1.4 |
| `docs/codenames.md` / realm tables | No change — no new repo, no new package |

## 7. Explicitly Out of Scope

- **Mímir's actual fake and stories** — the pattern is ready for it; the instance waits for `Reference.Components.FluentUI`.
- **Any change to shipping components** — Heimdall's components and `OutcomeFormComponentBase` are untouched by construction.
- **The `EmailExists` enumeration vector** — acknowledged and accepted in §1.4; closing it (rate limiting, response shaping) is a real-backend concern, not a catalog concern.
- **`IIdentityService` disclosure stories** — `PersonalData.razor` earns its stories under this pattern in a later pass, once its story-worthiness (visual, WASM-clean — it is) meets someone wanting to catalog it; nothing blocks it, nothing schedules it.

## 8. Success Criterion

- `Login / Locked Out` and `Register / Invalid Password` are bookmarkable story URLs that render their pinned feedback on load, driven by `ScenarioScope` + `StoryDriver` (the latter through the async `EmailExists` validation path), with no sentinel typing and no shipping-code changes.
- The sidebar matches §1.2's taxonomy; confirmation components appear only as states under their surfaces.
- `FakeAuthenticationService` is a stateless scenario switch; parity and lifecycle tests are green; Bragi's CI gate is required.
- Asgard's `InvalidCredentials` doc comment no longer contradicts `LoginHandler`.
- Mímir can adopt the pattern from this spec alone, with zero new framework code — the same "template proven by first instance" bar the migrations framework set.

---

## Addendum (2026-08-08, same day): the wire-shape adaptation — `NavigationResult` and wire-stamped scalars

**This section amends §1.3's outcome shapes and §5's construction details.** Heimdall v0.0.13 (PR #46, with the Asgard release carrying `NavigationResult`) landed a same-day refactor of the issuance wire tier — designed in `../../Platform/specs/2026-08-08-wire-stamped-request-scalars-design.md`, which this addendum consumes, not re-argues:

- **`LoginResult`/`RegisterResult`/`LogoutResult` are deleted.** All three issuance ops return `Task<Outcome<NavigationResult>>` — `NavigationResult` (Asgard `Abstractions.Contracts`): one `required string NextUrl`, the server-resolved next hop.
- **Requests carry wire-stamped scalars:** `LoginRequest.Email`/`RegisterRequest.Email`/`EmailExistsRequest.Email` are `Result<EmailAddress>` (the serialized member); `EmailInput` is the never-serialized form buffer whose setter stamps the parse. Construction in fakes/tests goes through `EmailInput`.
- **§1.3 amendments:** `Success` for Login returns `Ok(new NavigationResult { NextUrl = "/" })`; for Register, catalog-canonical `NextUrl = "/"` — **a deliberate placeholder**: Himinbjörg's handlers have not yet adapted to v0.0.13, so no real producer ruling exists for register-success's hop; the parity test pins "/" and the authoritative-source note updates when Himinbjörg lands. The sentinel comparison matches the parsed `Success<EmailAddress>` wire value case-insensitively. `Logout` returns `Outcome<NavigationResult>` on the wire, but the fake's stance is unchanged — throws, non-visual, never in a story. Every canonical error message and `Errors` dictionary in §1.3's table is **verified unchanged verbatim** in the landed handler source.
- **Validator reality for driven stories:** the Email rule chain is now `Cascade(Stop)`: empty → "Enter your email address.", unparseable → "Enter a valid email address (local@domain.tld).", then the async `EmailExists` round trip. The driven FillAndSubmit path is unchanged in mechanics (the driver fills the `EmailInput`-bound input; binding stamps the scalar).

### Findings from the integrated browser verification (2026-08-08, post-landing)

1. **Story-host DI must carry the client validators.** Blazilla's `FluentValidator` resolves from DI; `AddNorseStoryFakes()` now also registers `LoginRequestValidator` and `RegisterRequestValidator` — the async email-availability rule rides the fake, which is the pattern working as intended. (Browser-confirmed defect: "No validator found for model type …".)
2. **`StoryDriver` disables native constraint validation** (`form.noValidate = true` before `requestSubmit()`): Chrome blocks submission of an empty `Required` form whose invalid controls sit in shadow roots ("invalid form control is not focusable"), so the Blazor submit event never fired for SubmitOnly stories.
3. **What SubmitOnly actually renders:** the password-field messages ("'Password' must not be empty." + the length message). The empty-email message ("Enter your email address.") does **not** display anywhere in the form — the rule registers on the stamped `Email` property while the form binds `EmailInput`, and no message surface listens on `Email`. Recorded as a **Heimdall UX question** (whether field-level email messages should display via the `StampFieldBridge` path), not a catalog defect — the catalog faithfully renders what the components do.
4. **Upstream BlazingStory finding:** the canvas does not tear down previous story iframes — every visited story leaves a live WASM app instance behind, and a long browsing session accumulates until the browser exhausts (`ERR_INSUFFICIENT_RESOURCES`). Per-story isolation is also why the pattern's pinning is so robust (every hop boots fresh and re-drives). Not addressable from Bragi; candidate upstream issue against BlazingStory.

## Self-Review

**Placeholder scan:** No TBDs. Deferred items (§7) are named deferrals with reasons, not gaps.

**Internal consistency:** §1.1 complies with the platform enum law (`Unspecified = 0` sentinel, fail-fast) while unwrapped stories still render the happy path via the holder's constructor-supplied initial value — no zero-default shortcut, no catalog exemption. §1.2's three non-conflation placements (`Lockout.razor` vs the driven Login state, `NotAllowed` vs `AccessDenied.razor`, `ModelValidationSummary`'s harness) keep the sidebar and the inclusion rule simultaneously satisfiable. §1.3's registration dictionaries and fixed `Fault` correlation constant are the exact shapes §5's parity tests pin. §3's two driver modes cover both driven-state costs, including `Validation Errors`' submit-only pin. §2's statefulness rejection is consistent with §4 law 1 and with §1.2's determinism rationale. §1.4's ruling direction (docs bend to working code) matches the platform's standing correction pattern. The §1.3 table's sources were each verified against realm source this session (`LoginHandler`, `RegisterHandler` + `FieldFor`, `RegisterRequestValidator` (`MinimumLength(8)` — why `PasswordTooShort` is unreachable), `RegisterHandlerTests` (the `"aaaaaaaa"` fixture and its three-error yield), `ExceptionTranslationBehavior`, `CategoryDisplay`, `ModelValidationSummary`, `Lockout.razor`, `AccessDenied.razor`, `LoginWith2fa`).

**Scope check:** One realm's implementation plus one doc line in Asgard; plan-sized. The platform-pattern ambition is carried by doctrine (§4), not by speculative Mímir code — YAGNI holds.

**Ambiguity check:** "Pinned" is operationally defined (§0, §8: bookmarkable URL, renders identically on load). The two pinning mechanisms are named with their costs (§1.2). The spike's pass condition is concrete (§3, §8).

## Correction (2026-08-22): the lifetime reverts to Scoped

§1.1's "Lifetime correction" ruled Singleton for the fake and `Scenario<T>`, reasoning "WASM makes
scoped effectively singleton anyway — say what you mean." That reasoning assumed the story host
would always be a standalone WASM app, where each browser tab is its own runtime instance. It does
not hold once the host is Blazor Interactive Server (`../Platform/specs/2026-08-22-stories-blazor-server-mcp-design.md`):
under Interactive Server a Singleton is shared across every connected circuit on the process, so
one visitor pinning a scenario would leak into every other visitor's tab.

`AddNorseStoryFakes()` now registers every fake, its `Scenario<T>`, and
`RecordingSessionTransition`/`ISessionTransition` as Scoped — Blazor Server's DI scope is already
the framework's own per-circuit boundary, so this is the direct DI-native equivalent of what the
Singleton-as-scoped assumption approximated, not a new concept. Doctrine restated: match the
registration lifetime to the real isolation boundary of the host actually consuming it — never to
whichever host happens to be consuming it today.
