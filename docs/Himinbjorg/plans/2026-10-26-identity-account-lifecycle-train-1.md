# Identity Account Lifecycle over gRPC — Train 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Pairs with superpowers:test-driven-development on every coding task, per standing law (Glitnir CLAUDE.md §2.8).

**Goal:** Ship the anonymous tier of Himinbjörg#48 sub-project A: the seven remaining anonymous identity flows (two-factor and recovery-code sign-in, forgot/reset password, confirm email, confirm email change, resend confirmation) run as gRPC operations behind Heimdall components, the two-factor hop rides a data-protected challenge token instead of the `TwoFactorUserId` cookie, and the prerequisites every later train needs (the anonymous role name in Asgard, client-side `AddNorsePolicies()`, `AuthNPolicies.Account`) are in place.

**Architecture:** Heimdall's `IAuthenticationService` grows seven operations with `[DataContract]` request records and FluentValidation validators; Himinbjörg wraps each in a `{Op}Command` + `{Op}Handler` pair behind the existing `AuthenticationService` hydrate-and-send surface, reaching Identity only through `.Outcome` views. `NorseSignInManager` mints a time-limited `v1|userId|securityStamp|isPersistent|loginProvider` token on `RequiresTwoFactor` and redeems it in two new sign-in methods; the remember-machine cookie and the identity cookie both ride the existing `IDeferredSignIn` stash, chained through one `DeferredCompletion` helper. Heimdall components are shim-over-unit (routable shim copies query values into a unit with plain parameters), Bragi pins every new state through `AuthenticationScenario`, and Yggdrasil's WASM host gains the generated client-side policy registration.

**Tech Stack:** .NET 11 RC3 (`DotNetVersion` `11.0.0-rc.3.26522.148` in Yggdrasil CPM), C# 15, ASP.NET Core Identity 11 RC3 (`IPasskeyHandler`, `ITimeLimitedDataProtector` in the shared framework), protobuf-net.Grpc 1.3.x code-first, Blazor (Server + WASM + bUnit 2.x), FluentUI Blazor 5.x, FluentValidation 12 via Blazilla, xUnit v3 on MTP, Shouldly, NSubstitute, Roslyn incremental generators (Midgard), BlazingStory (Bragi).

**Spec:** `../specs/2026-10-26-identity-account-lifecycle-over-grpc-design.md` (r3, approved 2026-10-26). Every §-reference below is to that spec unless marked otherwise. Trains 2 and 3 (§9) get their own plans after this one ships.

**Plan revision:** r3 — r2 answered the six plan-review findings (PL1–PL6); r3 answers the two follow-ups on PL5 and PL1 (recorded at the end), all verified against an `ilspycmd` decompile of the installed `Microsoft.AspNetCore.Identity` 11.0.0-rc.3 on 2026-10-26.

## Global Constraints

- **No automatic git commits — platform law overrides the skill template's commit steps.** Every task ends by staging the diff (`git add`) in the realm it touched and stopping. Never `git commit`, never push. The human commits.
- **Branch law (Bifröst CLAUDE.md §7):** never branch Bifröst. One feature fork per realm, created from `master` at the start of the realm's phase (every realm is on `master` with nothing in flight as of 2026-10-26): Asgard `feature/anonymous-role-name`, Midgard `feature/client-policy-registration`, Heimdall `feature/account-lifecycle-anonymous-tier`, Himinbjörg `feature/account-lifecycle-anonymous-tier`, Bragi `feature/account-lifecycle-stories`, Yggdrasil `feature/account-lifecycle-train-1`. Glitnir work lands on Glitnir `master`.
- **Ship gates (§9):** each realm phase ends with PR merged, CI green, tag, publish. The next realm's phase develops against `UseProjectReferences=true` (local upstream) and only needs the upstream package at its own PR. Yggdrasil's bump must consume a Himinbjörg release that already deletes the scaffold pages (duplicate-route hazard, §8).
- **House rules:** `../../house-rules.md`, read in full at plan time: tabs; BOM-free UTF-8; LF; target-typed `new()`; collection expressions; primary constructors; expression bodies with the arrow on the declaration line; `is null`/`is not null`; dot-leading fluent chains; `ConfigureAwait(false)` in src, never in tests; `CancellationToken cancellationToken = default` last on every async method; `LoggerMessage` delegates; every class `sealed`/`abstract`/`static`; modifiers omitted at the default; XML docs on every src member; records for request/response types; `Outcome<T>` over throwing; tests `public sealed`, bare `void`/`async Task` methods, sentence-shaped underscore names; warnings are errors.
- **Generator emitters** call `sb.AppendCSharp(...)` only, with raw string literals, and write `Utf8NoBom.Encoding` (Bifröst CLAUDE.md §5).
- **Wire records** are `[DataContract] sealed record` with `[DataMember(Order = n)]`; email is `Result<EmailAddress>` plus the un-serialized `required string EmailInput` buffer whose setter parses, copied from `LoginRequest`; tokens and codes are opaque `string`s (§4.1). No `[Authorize]` and no mediator marker on any wire type (`RequestContractTests` locks it).
- **Return-type rule (§4.1):** sign-in / sign-out / sign-in refresh → `Outcome<NavigationResult>`; acknowledgment → `Outcome<BoolResponse>`.
- **Identity access law:** handlers reach `UserManager` / `SignInManager` only through `.Outcome` views for anything returning `IdentityResult` / `SignInResult` (`OutcomeSurfaceEnforcementTests` IL scan). Read-only calls (`IsEmailConfirmedAsync`, `Generate*TokenAsync`, `GetUserIdAsync`, `GetSecurityStampAsync`, `VerifyTwoFactorTokenAsync`) stay raw (§5.2).
- **NORSE070:** no `System.Text.Json` outside Midgard/Yggdrasil — the challenge token is a `|`-delimited string.
- **Routes** keep the scaffold's `/Account/*` templates (Heimdall CLAUDE.md). `NextUrl` values are root-relative and PathBase-qualified via `UriHelper.BuildRelative` (the `LoginHandler` precedent).
- **Test commands:** `dotnet test <realm>.slnx` or `dotnet test tests/<Project> -- --filter-class "*.<ClassName>"`. VSTest `--filter` does not work on MTP.
- **Decompile ritual (§5.3):** before Task 4.4 is coded, `ilspycmd` the installed `Microsoft.AspNetCore.Identity` (`11.0.0-rc.3`) for `SignInManager<TUser>.TwoFactorAuthenticatorSignInAsync`, `TwoFactorRecoveryCodeSignInAsync`, `DoTwoFactorSignInAsync`, `RememberTwoFactorClientAsync`, `ForgetTwoFactorClientAsync`, `StoreRememberClient`, `RefreshSignInAsync`, and record what was found in the task's handoff note. The plan's reproduction below is written against the RC3 shape and must be corrected if the decompile disagrees.
- Realm paths are workspace-relative from the Bifröst root.

## Review Focus

The five input classes the spec implies but no task's tests would otherwise exercise, each pinned to the owning task:

1. **A challenge token whose user was erased between Login and redemption** (key destroyed, `FindByIdAsync` throws `KeyDestroyedException`): the wire must answer bad credentials, never 500. → Task 4.4 (`A_challenge_for_an_erased_user_lets_the_destroyed_key_surface`), Task 4.5 (`An_erased_user_answers_Erased` on the view), Task 4.6 (`Failed_collapses_onto_the_shared_invalid_credentials_instance` covers the `Erased` arm).
2. **A `ResetPassword` for an email that does not exist** must answer the same `InvalidCredentials` instance as an invalid token against a real account (anti-enumeration by reference identity; a success answer here would let a junk-token caller tell known accounts from unknown ones — ruled in execution, 2026-10-26). → Task 4.6, test `An_unknown_or_erased_email_answers_the_same_invalid_link_problem_instance`.
3. **A `ConfirmEmailChange` whose caller is signed in as a different user** than the link's `UserId`: the email changes (the token proves ownership) but the caller's session is not refreshed; `NextUrl = /Account/Login`. → Task 4.7, test `A_confirmed_change_for_another_user_does_not_refresh_the_callers_session`.
4. **Two-factor code typed with spaces and hyphens** (`"123 456"`, `"123-456"`) must verify after server-side stripping, and the validator must accept the raw 7-character form. → Task 3.3 (`TwoFactorSignInRequestValidatorTests`), Task 4.5 (`Spaces_and_hyphens_are_stripped_before_verification`).
5. **An expired or garbage `challenge` query value** reaching `TwoFactorForm` must render the expired message with the login link and never call the service on load. → Task 3.5, test `A_missing_challenge_renders_the_expired_state_without_calling_the_service`; Task 4.4, test `A_garbage_token_is_refused_without_throwing`.

## Plan boundary and gates

| Phase | Realm | Gate before it starts |
|---|---|---|
| 1 | Asgard | spec r3 approved (done) |
| 2 | Midgard | Phase 1 shipped (`AsgardVersion` bump) |
| 3 | Heimdall | Phase 1 shipped (`NorseRoles.Anonymous`); Phase 2 not required |
| 4 | Himinbjörg | Phase 3 shipped (`HeimdallVersion` bump) |
| 5 | Bragi | Phase 3 shipped |
| 6 | Yggdrasil | Phases 2, 3, 4, 5 shipped |

**Re-plan triggers:** the RC3 decompile shows `SignInWithClaimsAsync` is no longer the virtual seam `RefreshSignInAsync` reaches (Task 4.4 prelude); the chained `/_auth/complete` hop fails the end-to-end check in Task 6.3 (fallback: multi-action `DeferredSignInAction`, an Asgard + Midgard change, §5.4); `RuntimeTypeModel` refuses a `BoolResponse` surrogate already registered (not expected; `EmailExists` ships it today).

## File Structure

**Asgard** (`Asgard/`)
- Create `src/Abstractions.Components/Authorization/NorseRoles.cs` — `public static class NorseRoles { public const string Anonymous = "anonymous"; }`.
- Modify `tests/Abstractions.Components.Tests/Authorization/NorsePolicyDeclarationTests.cs` — pins the role name.

**Midgard** (`Midgard/`)
- Modify `src/Infrastructure.Web.Server/Authentication/NorseAnonymousOptions.cs` — `AnonymousRole` aliases `NorseRoles.Anonymous`.
- Modify `gen/Infrastructure.Web.Client.Generator/Infrastructure.Web.Client.Generator.csproj` — links the two policy files.
- Create `gen/Infrastructure.Web.Client.Generator/Policies/PolicyRegistrationGenerator.cs` — client-side generator (same shape as the server one; namespace `Norse.Infrastructure.Web.Client.Generator.Policies`).
- Create `gen/Infrastructure.Web.Client.Generator/Policies/Diagnostics.cs` — NORSE014/NORSE015 descriptors (copy of the server generator's `Policies/Diagnostics.cs`, or link it if it has no server-only dependency).
- Create `tests/Infrastructure.Web.Client.Generator.Tests/Policies/PolicyRegistrationGeneratorTests.cs`.
- Modify `tests/Infrastructure.Web.Server.Tests/Authentication/NorseAnonymousHandlerTests.cs` — one fact that the minted role equals `NorseRoles.Anonymous`.

**Heimdall** (`Heimdall/`)
- Modify `src/AuthN.Services/IAuthenticationService.cs` — seven operations.
- Create `src/AuthN.Services/TwoFactorSignInRequest.cs`, `RecoveryCodeSignInRequest.cs`, `ForgotPasswordRequest.cs`, `ResetPasswordRequest.cs`, `ConfirmEmailRequest.cs`, `ConfirmEmailChangeRequest.cs`, `ResendEmailConfirmationRequest.cs`.
- Modify `src/AuthN.Services/AuthNPolicies.cs` — `Account` + `ConfigureAccount`.
- Modify `src/AuthN.Services/IdentityPolicies.cs` — `ConfigureSelf` hardened.
- Create `src/AuthN.Components/TwoFactorSignInRequestValidator.cs`, `RecoveryCodeSignInRequestValidator.cs`, `ForgotPasswordRequestValidator.cs`, `ResetPasswordRequestValidator.cs`, `ConfirmEmailRequestValidator.cs`, `ConfirmEmailChangeRequestValidator.cs`, `ResendEmailConfirmationRequestValidator.cs`.
- Create `src/AuthN.Components.FluentUI/ForgotPassword.razor`, `ResendEmailConfirmation.razor`, `TwoFactorForm.razor`, `LoginWith2fa.razor`, `RecoveryCodeForm.razor`, `LoginWithRecoveryCode.razor`, `ResetPasswordForm.razor`, `ResetPassword.razor`, `ConfirmEmailPanel.razor`, `ConfirmEmail.razor`, `ConfirmEmailChangePanel.razor`, `ConfirmEmailChange.razor`.
- Modify `tests/AuthN.Services.Tests/RequestContractTests.cs`, `PolicyContributorTests.cs`.
- Create `tests/AuthN.Components.Tests/{Record}ValidatorTests.cs` (seven).
- Create `tests/AuthN.Components.FluentUI.Tests/ForgotPasswordTests.cs`, `ResendEmailConfirmationTests.cs`, `TwoFactorFormTests.cs`, `LoginWith2faTests.cs`, `RecoveryCodeFormTests.cs`, `ResetPasswordFormTests.cs`, `ResetPasswordTests.cs`, `ConfirmEmailPanelTests.cs`, `ConfirmEmailChangePanelTests.cs`.
- Modify `README.md`, `CLAUDE.md` — project table rows for `AuthN.Services` / `AuthN.Components` / `AuthN.Components.FluentUI` list the new surface.

**Himinbjörg** (`Himinbjorg/`)
- Create `src/Identity.Outcomes/IRecoveryCodeReplacement.cs`.
- Modify `src/Identity.Outcomes/UserManagerOutcomeView.cs` — `GetUserAsync`, `FindByIdAsync`, `ResetPasswordAsync`, `ConfirmEmailAsync`, `ChangeEmailAsync`, `ReplaceRecoveryCodesAsync`.
- Modify `src/Identity.Web.Server/NorseUserManager.cs` — implements `IRecoveryCodeReplacement`; overrides `GenerateNewTwoFactorRecoveryCodesAsync`.
- Modify `src/Identity.Web.Server/NorseSignInManager.cs` — `IDataProtectionProvider` dependency; token mint; `TwoFactorChallengeSignInAsync`; `RecoveryCodeChallengeSignInAsync`; remember/forget overrides; `StoreTwoFactorInfo` deleted.
- Create `src/Identity.Web.Server/TwoFactorChallenge.cs` — the token record and its protector purpose.
- Create `src/Identity.Web.Server/DeferredCompletion.cs`.
- Modify `src/Identity.Web.Server/SignInManagerOutcomeView.cs` — two challenge methods.
- Modify `src/Identity.Web.Server/LoginHandler.cs`, `LogoutHandler.cs` — `DeferredCompletion`; `?challenge=`.
- Create `src/Identity.Web.Server/{Op}Command.cs` + `{Op}Handler.cs` for `TwoFactorSignIn`, `RecoveryCodeSignIn`, `ForgotPassword`, `ResetPassword`, `ConfirmEmail`, `ConfirmEmailChange`, `ResendEmailConfirmation`.
- Create `src/Identity.Web.Server/AccountLinks.cs` — absolute email-link builder.
- Modify `src/Identity.Web.Server/AuthenticationService.cs` — seven methods.
- Modify `src/Identity.Web.Server/ServiceCollectionExtensions.cs` — `AddHttpContextAccessor()` (if not already present through Midgard) and `AddDataProtection()` are expected from the host; verify, no new registration unless a test proves one is missing.
- Delete `src/Identity.Web.Server/Components/Pages/ForgotPassword.razor`, `ResetPassword.razor`, `LoginWith2fa.razor`, `LoginWithRecoveryCode.razor`, `ConfirmEmail.razor`, `ConfirmEmailChange.razor`, `ResendEmailConfirmation.razor`, `RegisterConfirmation.razor`, `InvalidUser.razor`.
- Modify `src/Identity.Web.Server/IdentityNoOpEmailSender.cs` — doc comment no longer names `RegisterConfirmation.razor` and no longer claims to log.
- Create `src/Identity.Web.Server/DevelopmentEmailSender.cs` — logs links and codes; resolved only under `Environments.Development`.
- Create `tests/Identity.Web.Server.Tests/CapturingEmailSender.cs` — in-memory `IEmailSender<NorseUser>` for the real-Postgres facts.
- Modify `tests/Identity.Web.Server.Tests/OutcomeSurfaceEnforcementTests.cs` — bans `GenerateNewTwoFactorRecoveryCodesAsync` by name.
- Modify `tests/Identity.Web.Server.Tests/NorseSignInManagerTests.cs`, `LoginHandlerTests.cs`, `SignInManagerOutcomeViewTests.cs`.
- Create `tests/Identity.Web.Server.Tests/DeferredCompletionTests.cs`, `TwoFactorChallengeTests.cs`, `{Op}HandlerTests.cs` (seven), `NorseUserManagerRecoveryCodeTests.cs`.
- Modify `tests/Identity.Outcomes.Tests/UserManagerOutcomeViewTests.cs`.
- Modify `README.md`, `CLAUDE.md`.

**Bragi** (`Bragi/`)
- Modify `src/DesignSystem.Stories/Authentication/AuthenticationScenario.cs`, `FakeAuthenticationService.cs`, `ServiceCollectionExtensions.cs`, `ScenarioCatalog.md`, `Login.stories.razor`, `ForgotPassword.stories.razor`, `TwoFactor.stories.razor`, `ResetPassword.stories.razor`.
- Create `src/DesignSystem.Stories/Authentication/RecoveryCodeSignIn.stories.razor`, `ConfirmEmail.stories.razor`, `ConfirmEmailChange.stories.razor`, `ResendEmailConfirmation.stories.razor`.
- Modify `tests/DesignSystem.Stories.Tests/Authentication/FakeAuthenticationServiceTests.cs`, `DrivenStoryNavigationTests.cs`, `ServiceCollectionExtensionsTests.cs`, `CLAUDE.md`, `README.md`.

**Yggdrasil** (`Yggdrasil/`)
- Modify `Directory.Packages.props` — `AsgardVersion`, `MidgardVersion`, `HeimdallVersion`, `HiminbjorgVersion`, `BragiVersion`.
- Modify `src/Hosting.Web.Client/Program.cs` — `.AddNorsePolicies()`.
- Modify `tests/Hosting.Web.Client.Tests/CompositionTests.cs` — policy fact.
- Modify `CLAUDE.md` — identity page-tree row.

---

## Phase 1 — Asgard

### Task 1.1: `NorseRoles.Anonymous`

**Files:**
- Create: `Asgard/src/Abstractions.Components/Authorization/NorseRoles.cs`
- Test: `Asgard/tests/Abstractions.Components.Tests/Authorization/NorsePolicyDeclarationTests.cs`

**Interfaces:**
- Produces: `Norse.Abstractions.Components.Authorization.NorseRoles.Anonymous` (`public const string`, value `"anonymous"`). Midgard (Task 2.1) and Heimdall (Task 3.2) consume it.

- [ ] **Step 1: Create the Asgard fork** `feature/anonymous-role-name` from `master`.
- [ ] **Step 2: Write the failing test** (add to `NorsePolicyDeclarationTests`):

```csharp
[Fact]
void The_anonymous_role_name_is_the_one_the_browser_lane_mints() =>
	NorseRoles.Anonymous.ShouldBe("anonymous");
```

- [ ] **Step 3: Run it** — `dotnet test Asgard/tests/Abstractions.Components.Tests -- --filter-class "*.NorsePolicyDeclarationTests"`. Expected: build error, `NorseRoles` not found.
- [ ] **Step 4: Implement** `NorseRoles` as a `public static class` in `Authorization/NorseRoles.cs`, one constant, XML doc stating that Midgard's `NorseAnonymousHandler` mints exactly this role and policies exclude it by name (§4.4).
- [ ] **Step 5: Run the test** — expected PASS. Run `dotnet test Asgard.slnx` — green.
- [ ] **Step 6: Stage** `git -C Asgard add src/Abstractions.Components/Authorization/NorseRoles.cs tests/Abstractions.Components.Tests/Authorization/NorsePolicyDeclarationTests.cs` and stop. The human commits, opens the PR, tags, publishes.

---

## Phase 2 — Midgard

### Task 2.1: Alias the anonymous role

**Files:**
- Modify: `Midgard/src/Infrastructure.Web.Server/Authentication/NorseAnonymousOptions.cs:12`
- Test: `Midgard/tests/Infrastructure.Web.Server.Tests/Authentication/NorseAnonymousHandlerTests.cs`

**Interfaces:**
- Consumes: `NorseRoles.Anonymous` (Task 1.1), via the published Asgard package (`Directory.Packages.props` bump of `AsgardVersion` in Midgard first, or `UseProjectReferences=true` locally).

- [ ] **Step 1: Create the Midgard fork** `feature/client-policy-registration` from `master`.
- [ ] **Step 2: Write the failing test** in `NorseAnonymousHandlerTests`, beside the existing `IsInRole(NorseAnonymousOptions.AnonymousRole)` fact, driving the real handler the way that fact does:

```csharp
[Fact]
async Task The_minted_principal_holds_the_platform_anonymous_role()
{
	var result = await AuthenticateAsync(/* the existing harness: a fresh request, no cookie */);
	result.Principal!.IsInRole(NorseRoles.Anonymous).ShouldBeTrue();
	result.Principal.Claims.Count(c => c.Type == ClaimTypes.Role).ShouldBe(1);
}
```

A reference-equality or string-equality check between the two constants cannot prove the alias: identical literals are interned and `const` values are substituted at compile time, so it would pass before the change. The behavioral fact is the enforcement; the alias itself is a source-level convention reviewed by eye (one line, one `using`).

- [ ] **Step 3: Run it** — expected: build error until Midgard references the Asgard version that carries `NorseRoles` (bump `AsgardVersion` in Midgard's `Directory.Packages.props` or run on project references), then PASS once the reference resolves — the value is already `"anonymous"`. The fact pins the contract going forward; it is not expected to be red on behavior.
- [ ] **Step 4: Implement** `public const string AnonymousRole = NorseRoles.Anonymous;` with `using Norse.Abstractions.Components.Authorization;`. Doc comment: "aliases Asgard's constant so Heimdall can name the role without referencing Midgard (NORSE071)".
- [ ] **Step 5: Run** `dotnet test Midgard/tests/Infrastructure.Web.Server.Tests -- --filter-class "*.NorseAnonymousHandlerTests"` — PASS.
- [ ] **Step 6: Stage** the two files.

### Task 2.2: Client-side `AddNorsePolicies()` generator

**Files:**
- Modify: `Midgard/gen/Infrastructure.Web.Client.Generator/Infrastructure.Web.Client.Generator.csproj`
- Create: `Midgard/gen/Infrastructure.Web.Client.Generator/Policies/PolicyRegistrationGenerator.cs`
- Create: `Midgard/gen/Infrastructure.Web.Client.Generator/Policies/Diagnostics.cs` (only if the server's `Policies/Diagnostics.cs` cannot be linked cleanly; prefer linking)
- Test: `Midgard/tests/Infrastructure.Web.Client.Generator.Tests/Policies/PolicyRegistrationGeneratorTests.cs`

**Interfaces:**
- Consumes: `Norse.Infrastructure.Web.Server.Generator.Policies.PolicyDeclarationDiscovery.Discover(Compilation)` → `PolicyDiscoveryResult { Valid, Invalid }`; `PolicyRegistrationEmitter.Emit(string rootNamespace, ImmutableArray<PolicyDeclaration>)`; `RootNamespaceResolution.Resolve(compilation, options)`. All three arrive as linked `<Compile Include="../Infrastructure.Web.Server.Generator/Policies/PolicyDeclarationDiscovery.cs" Link="Policies/PolicyDeclarationDiscovery.cs" />` entries (same pattern as the existing `Shared/` links). The linked files keep their server namespace; the client generator `using`s it.
- Produces: in any compilation referencing `Infrastructure.Web.Client` as a generator, `static class NorsePolicyRegistration { public static IServiceCollection AddNorsePolicies(this IServiceCollection services) }` in the compilation's root namespace — byte-identical output to the server generator for the same inputs. Yggdrasil's WASM host (Task 6.2) calls it.

- [ ] **Step 1: Write the failing tests** (new file, mirror the server test harness: `ReferenceAssemblies.Net110`, `SharedFrameworkReferences`, `MetadataReference` to `NorsePolicyAttribute`'s assembly and to `Microsoft.AspNetCore.Authorization`'s; the client test project already references `Microsoft.AspNetCore.Components.Web`, which carries `AuthorizationPolicyBuilder` transitively via `Microsoft.AspNetCore.Authorization` — add an explicit `PackageReference Include="Microsoft.AspNetCore.Authorization" Version="11.*-*"` to the test csproj if resolution fails):

```csharp
[Fact]
void Direct_declaration_in_a_referenced_assembly_is_registered()
{
	var (_, output) = Run("Norse.Hosting.Web.Client", EmptySource, _imageA);
	Generated(output).ShouldContain("""AddPolicy("AuthN.Public", global::Norse.AuthN.Services.AuthNPolicies.Public)""");
}

[Fact]
void The_output_is_byte_identical_to_the_server_generators_output_for_the_same_inputs()
{
	var client = Generated(Run("Norse.Hosting.Web.Client", EmptySource, _imageA).Output);
	var server = Generated(RunServer("Norse.Hosting.Web.Client", EmptySource, _imageA).Output);
	client.ShouldBe(server);
}

[Fact]
void An_empty_reference_set_still_emits_a_compilable_AddNorsePolicies()
{
	var (_, output) = Run("Norse.Hosting.Web.Client", EmptySource);
	Generated(output).ShouldContain("AddNorsePolicies");
	output.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
}

[Fact]
void Duplicate_policy_name_across_two_packages_fires_NORSE014_and_registers_neither()
{
	var (diagnostics, output) = Run("Norse.Hosting.Web.Client", EmptySource, _imageA, _imageOther);
	diagnostics.ShouldContain(d => d.Id == "NORSE014" && d.Severity == DiagnosticSeverity.Error);
	Generated(output).ShouldNotContain("AuthN.Public");
}
```

`Run` drives `Norse.Infrastructure.Web.Client.Generator.Policies.PolicyRegistrationGenerator`; `RunServer` drives the server one (the client test project adds a `ProjectReference` to `../../gen/Infrastructure.Web.Server.Generator/Infrastructure.Web.Server.Generator.csproj` for this one fact). `Generated(output)` returns the text of the syntax tree whose path ends in `NorsePolicyRegistration.g.cs`. `AuthNPolicySource`, `OtherPolicySameNameSource`, `_imageA`, `_imageOther`, `Compile(...)` are copied from the server test.

- [ ] **Step 2: Run** `dotnet test Midgard/tests/Infrastructure.Web.Client.Generator.Tests -- --filter-class "*.PolicyRegistrationGeneratorTests"` — expected: build error, no client `PolicyRegistrationGenerator`.
- [ ] **Step 3: Link the shared files** in the client generator csproj (`PolicyDeclarationDiscovery.cs`, `PolicyRegistrationEmitter.cs`, and `Policies/Diagnostics.cs` if it has no server-only dependency; otherwise create a client copy with the same two descriptors `NORSE014` / `NORSE015`). Keep the `ItemGroup` sorted.
- [ ] **Step 4: Implement** `[Generator(LanguageNames.CSharp)] public sealed class PolicyRegistrationGenerator : IIncrementalGenerator` in `Policies/PolicyRegistrationGenerator.cs`, namespace `Norse.Infrastructure.Web.Client.Generator.Policies`. Body is the server generator's `Initialize` / `Discover` verbatim over the linked types (`CompilationProvider.Combine(AnalyzerConfigOptionsProvider).Select(Discover)`, NORSE015 per invalid, NORSE014 per duplicate, duplicates dropped from emission, `AddSource("NorsePolicyRegistration.g.cs", SourceText.From(PolicyRegistrationEmitter.Emit(...), Utf8NoBom.Encoding))`). Update the csproj `<Description>` to name `AddNorsePolicies()`.
- [ ] **Step 5: Run the tests** — PASS. Run `dotnet test Midgard.slnx` — green.
- [ ] **Step 6: Update** `Midgard/README.md` and `Midgard/CLAUDE.md` where the client generator's emitted surface is listed (grep `AddNorseClientComponents` to find the rows) to add `AddNorsePolicies()`.
- [ ] **Step 7: Stage** the generator, csproj, tests, README and CLAUDE.md. Stop. Ship gate: Midgard PR, tag, publish.

---

## Phase 3 — Heimdall

### Task 3.1: Seven wire records and the contract

**Files:**
- Create: `Heimdall/src/AuthN.Services/TwoFactorSignInRequest.cs`, `RecoveryCodeSignInRequest.cs`, `ForgotPasswordRequest.cs`, `ResetPasswordRequest.cs`, `ConfirmEmailRequest.cs`, `ConfirmEmailChangeRequest.cs`, `ResendEmailConfirmationRequest.cs`
- Modify: `Heimdall/src/AuthN.Services/IAuthenticationService.cs`
- Test: `Heimdall/tests/AuthN.Services.Tests/RequestContractTests.cs`

**Interfaces:**
- Produces (every record `[DataContract] public sealed record`, mutable `set` properties because they are `EditForm` binding targets, exactly like `LoginRequest`):
  - `TwoFactorSignInRequest { [1] string Challenge = ""; [2] string Code = ""; [3] bool RememberMachine }`
  - `RecoveryCodeSignInRequest { [1] string Challenge = ""; [2] string RecoveryCode = "" }`
  - `ForgotPasswordRequest { [1] Result<EmailAddress> Email; required string EmailInput (buffer, not serialized) }`
  - `ResetPasswordRequest { [1] Result<EmailAddress> Email; EmailInput buffer; [2] string Code = ""; [3] string Password = "" }`
  - `ConfirmEmailRequest { [1] string UserId = ""; [2] string Code = "" }`
  - `ConfirmEmailChangeRequest { [1] string UserId = ""; [2] Result<EmailAddress> Email; EmailInput buffer; [3] string Code = "" }`
  - `ResendEmailConfirmationRequest { [1] Result<EmailAddress> Email; EmailInput buffer }`
  - `IAuthenticationService` gains, each `[OperationContract]` with trailing `CancellationToken cancellationToken = default`: `Task<Outcome<NavigationResult>> TwoFactorSignIn(TwoFactorSignInRequest)`, `RecoveryCodeSignIn(RecoveryCodeSignInRequest)`, `ForgotPassword(ForgotPasswordRequest)`, `ResetPassword(ResetPasswordRequest)`, `ConfirmEmailChange(ConfirmEmailChangeRequest)`; `Task<Outcome<BoolResponse>> ConfirmEmail(ConfirmEmailRequest)`, `ResendEmailConfirmation(ResendEmailConfirmationRequest)`.
- Himinbjörg (Phase 4) and Bragi (Phase 5) implement the contract.

- [ ] **Step 1: Create the Heimdall fork** `feature/account-lifecycle-anonymous-tier` from `master`.
- [ ] **Step 2: Write the failing tests** — extend `Wire_records_carry_no_Authorize_attribute`'s array with the seven new types, and add:

```csharp
[Fact]
void The_seven_anonymous_lifecycle_operations_are_declared_with_the_ruled_return_types()
{
	Type[] navigation = [typeof(TwoFactorSignInRequest), typeof(RecoveryCodeSignInRequest), typeof(ForgotPasswordRequest), typeof(ResetPasswordRequest), typeof(ConfirmEmailChangeRequest)];
	Type[] acknowledgment = [typeof(ConfirmEmailRequest), typeof(ResendEmailConfirmationRequest)];
	foreach (var request in navigation)
		Operation(request).ReturnType.ShouldBe(typeof(Task<Outcome<NavigationResult>>));
	foreach (var request in acknowledgment)
		Operation(request).ReturnType.ShouldBe(typeof(Task<Outcome<BoolResponse>>));
}

[Fact]
void Every_wire_record_is_a_data_contract_with_ordered_members()
{
	foreach (var wireType in NewRecords)
	{
		wireType.GetCustomAttribute<DataContractAttribute>().ShouldNotBeNull(wireType.Name);
		wireType.GetProperties().Where(p => p.Name != "EmailInput")
			.Select(p => p.GetCustomAttribute<DataMemberAttribute>()?.Order)
			.ShouldAllBe(order => order is not null, wireType.Name);
	}
}

[Fact]
void Email_input_buffers_are_never_serialized()
{
	foreach (var wireType in new[] { typeof(ForgotPasswordRequest), typeof(ResetPasswordRequest), typeof(ConfirmEmailChangeRequest), typeof(ResendEmailConfirmationRequest) })
		wireType.GetProperty("EmailInput")!.GetCustomAttribute<DataMemberAttribute>().ShouldBeNull(wireType.Name);
}
```

`Operation(Type request)` = the single `IAuthenticationService` method whose first parameter type is `request`. `NewRecords` = the seven types. The existing `Every_service_method_ends_with_a_trailing_cancellation_token` and `Every_service_operation_carries_the_operation_contract_attribute` cover the new methods automatically.

- [ ] **Step 3: Run** `dotnet test Heimdall/tests/AuthN.Services.Tests` — expected: build errors for the missing types.
- [ ] **Step 4: Implement** the seven records and the seven interface members. Each XML doc names the scaffold page it replaces (`ForgotPassword.razor`, …) and, for `ForgotPassword` / `ResetPassword`, the anti-enumeration contract from §4.2. Update the `AuthN.Services.csproj` `<Description>` list of records.
- [ ] **Step 5: Run the tests** — PASS.
- [ ] **Step 6: Stage** and stop (no commit).

### Task 3.2: `AuthNPolicies.Account` and a hardened `IdentityPolicies.Self`

**Files:**
- Modify: `Heimdall/src/AuthN.Services/AuthNPolicies.cs`, `IdentityPolicies.cs`
- Test: `Heimdall/tests/AuthN.Services.Tests/PolicyContributorTests.cs`

**Interfaces:**
- Consumes: `NorseRoles.Anonymous` (Task 1.1; bump Heimdall's Asgard package version first if not on project references).
- Produces: `AuthNPolicies.Account = "AuthN.Account"`; `[NorsePolicy(Account)] public static void ConfigureAccount(AuthorizationPolicyBuilder policy)`. Himinbjörg's Train 2 commands and Heimdall's `Manage/_Imports.razor` (Train 2) name it; Yggdrasil's generated registrations pick it up with no edit.

- [ ] **Step 1: Write the failing tests** in `PolicyDeclarationTests`:

```csharp
static AuthorizationResult Evaluate(string policyName, Type owner, ClaimsPrincipal principal)
{
	var policy = Build(owner, policyName);
	// DefaultAuthorizationService over the built policy; AuthorizationHandlerContext with the principal; evaluate the assertion requirements.
}

static ClaimsPrincipal Principal(string role) =>
	new(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Test"));

[Fact]
void The_account_policy_is_declared_in_metadata() =>
	Should.NotThrow(() => Declaration(typeof(AuthNPolicies), AuthNPolicies.Account));

[Fact]
void The_account_policy_rejects_the_anonymous_role_principal() =>
	Evaluate(AuthNPolicies.Account, typeof(AuthNPolicies), Principal(NorseRoles.Anonymous)).Succeeded.ShouldBeFalse();

[Fact]
void The_account_policy_admits_an_authenticated_non_anonymous_principal() =>
	Evaluate(AuthNPolicies.Account, typeof(AuthNPolicies), Principal("user")).Succeeded.ShouldBeTrue();

[Fact]
void The_account_policy_rejects_an_unauthenticated_principal() =>
	Evaluate(AuthNPolicies.Account, typeof(AuthNPolicies), new ClaimsPrincipal(new ClaimsIdentity())).Succeeded.ShouldBeFalse();

[Fact]
void Self_now_rejects_the_anonymous_role_principal() =>
	Evaluate(IdentityPolicies.Self, typeof(IdentityPolicies), Principal(NorseRoles.Anonymous)).Succeeded.ShouldBeFalse();
```

`Evaluate` builds a `ServiceCollection().AddAuthorization(o => o.AddPolicy(name, policy)).AddLogging()` provider and calls `IAuthorizationService.AuthorizeAsync(principal, null, name)` — the real evaluator, so the assertion lambda is actually executed.

- [ ] **Step 2: Run** — expected: `AuthNPolicies.Account` missing; `Self` fact fails (admits anonymous today).
- [ ] **Step 3: Implement** `Account` constant, `ConfigureAccount` → `policy.RequireAuthenticatedUser().RequireAssertion(static context => !context.User.IsInRole(NorseRoles.Anonymous))`; `ConfigureSelf` becomes the same body. XML docs cite §4.4 (the anonymous principal is authenticated).
- [ ] **Step 4: Run** — PASS. Run `dotnet test Heimdall.slnx` — green (the `Masked_disclosure_still_requires_the_system_role` and signature facts stay green).
- [ ] **Step 5: Stage** and stop.

### Task 3.3: Seven validators

**Files:**
- Create: `Heimdall/src/AuthN.Components/TwoFactorSignInRequestValidator.cs`, `RecoveryCodeSignInRequestValidator.cs`, `ForgotPasswordRequestValidator.cs`, `ResetPasswordRequestValidator.cs`, `ConfirmEmailRequestValidator.cs`, `ConfirmEmailChangeRequestValidator.cs`, `ResendEmailConfirmationRequestValidator.cs`
- Test: `Heimdall/tests/AuthN.Components.Tests/{Record}ValidatorTests.cs` (seven files)

**Interfaces:**
- Produces: `public sealed class {Record}Validator : AbstractValidator<{Record}>` ×7. Midgard's generated `CommandRequestValidator<,,>` adapter picks each up server-side; `AddNorseClientComponents()` registers them client-side; Bragi registers them explicitly (Task 5.1).

Rules (§6.7):
- Email chain on `Email` (`Cascade(Stop)`, empty → "Enter your email address.", `.WithName(nameof(X.EmailInput))`, success-gate → "Enter a valid email address (local@domain.tld).") copied from `LoginRequestValidator` for `ForgotPassword`, `ResetPassword`, `ConfirmEmailChange`, `ResendEmailConfirmation`.
- `TwoFactorSignIn`: `Challenge` `NotEmpty()`; `Code` `NotEmpty().Length(6, 7)` (raw, the scaffold's `StringLength(7, MinimumLength = 6)`; `"123 456"` and `"123-456"` are 7 characters and pass).
- `RecoveryCodeSignIn`: `Challenge` `NotEmpty()`; `RecoveryCode` `NotEmpty()`.
- `ResetPassword`: `Code` `NotEmpty()`; `Password` `NotEmpty().MinimumLength(8)` (the platform's login floor, not the scaffold's 6).
- `ConfirmEmail`: `UserId` `NotEmpty()`; `Code` `NotEmpty()`.
- `ConfirmEmailChange`: `UserId` `NotEmpty()`; `Code` `NotEmpty()`; email chain.

- [ ] **Step 1: Write the failing tests**, one file per validator, in the `LoginRequestValidatorTests` shape (`readonly {Record}Validator _validator = new();`, `Rejects_*` / `Accepts_a_well_formed_request`). Minimum facts per validator: every rule rejected once; a well-formed request accepted. `TwoFactorSignInRequestValidatorTests` additionally:

```csharp
[Theory]
[InlineData("123456")]
[InlineData("123 456")]
[InlineData("123-456")]
void Accepts_six_digits_with_or_without_a_separator(string code) =>
	_validator.Validate(new TwoFactorSignInRequest { Challenge = "c", Code = code }).IsValid.ShouldBeTrue();

[Theory]
[InlineData("12345")]
[InlineData("12345678")]
void Rejects_codes_outside_six_to_seven_characters(string code) =>
	_validator.Validate(new TwoFactorSignInRequest { Challenge = "c", Code = code }).IsValid.ShouldBeFalse();
```

- [ ] **Step 2: Run** `dotnet test Heimdall/tests/AuthN.Components.Tests` — build errors.
- [ ] **Step 3: Implement** the seven validators. XML doc on each: "one declaration, two consumers" (the `LoginRequestValidator` wording).
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Stage** and stop.

### Task 3.4: `ForgotPassword` and `ResendEmailConfirmation` forms

**Files:**
- Create: `Heimdall/src/AuthN.Components.FluentUI/ForgotPassword.razor`, `ResendEmailConfirmation.razor`
- Test: `Heimdall/tests/AuthN.Components.FluentUI.Tests/ForgotPasswordTests.cs`, `ResendEmailConfirmationTests.cs`

**Interfaces:**
- Consumes: `IAuthenticationService.ForgotPassword` / `.ResendEmailConfirmation` (Task 3.1); validators (Task 3.3); `OutcomeFormComponentBase.EditContextFor` / `SubmitAsync`; `ISessionTransition` is **not** used here (no principal change, §6.3).
- Produces: routable pages `@page "/Account/ForgotPassword"` and `@page "/Account/ResendEmailConfirmation"`, both `@layout GateLayout`, `@inherits OutcomeFormComponentBase`, `<EditForm EditContext="EditContextFor(_request)" OnSubmit=…>` with `<FormValidator/>`, `<ModelValidationSummary/>`, one `FluentTextInput` bound to `EmailInput`, a primary submit button disabled on `IsSubmitting`.
  - `ForgotPassword`: on success `Navigation.NavigateTo(result.NextUrl)` (soft, §6.3). Cross-link back to `Account/Login`.
  - `ResendEmailConfirmation`: on success set `_sent = true` and render `<p role="alert">Verification email sent. Please check your email.</p>` in place (the scaffold's wording), form hidden.

- [ ] **Step 1: Write the failing tests** (both classes `: BunitContext`, constructor `Services.AddFluentUIComponents(); JSInterop.Mode = JSRuntimeMode.Loose;` + validator registration `Services.AddScoped<IValidator<ForgotPasswordRequest>, ForgotPasswordRequestValidator>()`):

```csharp
[Fact]
async Task An_empty_submit_never_reaches_the_service()
{
	var page = Render<ForgotPassword>();
	await page.Find("form").SubmitAsync();
	await _service.DidNotReceiveWithAnyArgs().ForgotPassword(default!, default);
}

[Fact]
async Task Success_navigates_softly_to_the_server_authored_url()
{
	_service.ForgotPassword(Arg.Any<ForgotPasswordRequest>(), Arg.Any<CancellationToken>())
		.Returns(Outcome<NavigationResult>.Ok(new() { NextUrl = "/Account/ForgotPasswordConfirmation" }));
	var page = Render<ForgotPassword>();
	await page.FindAll("fluent-text-input")[0].ChangeAsync("user@example.com");
	await page.Find("form").SubmitAsync();
	var entry = Navigation.History.ShouldHaveSingleItem();
	entry.Uri.ShouldBe("/Account/ForgotPasswordConfirmation");
	entry.Options.ForceLoad.ShouldBeFalse();
}

[Fact]
async Task A_fault_renders_in_the_model_summary_and_does_not_navigate()
{
	_service.ForgotPassword(...).Returns(Outcome<NavigationResult>.Err(ErrorCategory.Fault, correlationId: Guid.Empty));
	… submit valid email …
	page.Find(".norse-model-errors").ShouldNotBeNull();
	Navigation.History.ShouldBeEmpty();
}
```

`ResendEmailConfirmationTests`: the same empty-submit lock; `Success_renders_the_sent_message_in_place` asserts `page.Find("[role=alert]").TextContent.ShouldContain("Verification email sent")` and `Navigation.History.ShouldBeEmpty()`.

- [ ] **Step 2: Run** — build errors (components missing).
- [ ] **Step 3: Implement** both pages from the `Login.razor` template (same `@layout`, `@inherits`, `EditForm` shape, `gate-subtitle` copy: "Enter your email and we'll send a reset link." / "Enter your email and we'll send the confirmation link again.").
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Stage** and stop.

### Task 3.5: `TwoFactorForm` + `LoginWith2fa` shim

**Files:**
- Create: `Heimdall/src/AuthN.Components.FluentUI/TwoFactorForm.razor`, `LoginWith2fa.razor`
- Test: `Heimdall/tests/AuthN.Components.FluentUI.Tests/TwoFactorFormTests.cs`, `LoginWith2faTests.cs`

**Interfaces:**
- Consumes: `IAuthenticationService.TwoFactorSignIn`; `TwoFactorSignInRequestValidator`; `ISessionTransition.Begin(NavigationResult)`.
- Produces:
  - `TwoFactorForm` (`@inherits OutcomeFormComponentBase`): `[Parameter] public string? Challenge { get; set; }`. `OnParametersSet` copies `Challenge ?? ""` into `_request.Challenge` (§6.1). When `string.IsNullOrEmpty(Challenge)` renders `<p role="alert" class="norse-challenge-expired">This sign-in attempt has expired. <a href="Account/Login">Start again</a>.</p>` and no form. Otherwise: `FluentTextInput` bound to `_request.Code` (`AutoComplete="one-time-code"`, label "Authenticator code"), `FluentCheckbox` bound to `_request.RememberMachine` ("Remember this machine"), submit; success → `SessionTransition.Begin(result)`; a sibling gate link `<a href="Account/LoginWithRecoveryCode?challenge=@Uri.EscapeDataString(Challenge)">log in with a recovery code</a>`.
  - **Credential-failure recast rule (all three credential forms, Tasks 3.5–3.7).** The same failure reaches a form in two shapes: in-process on a Blazor Server circuit as the handler's `Failed(InvalidCredentials)` carrying its model-error text, and over gRPC as a trailerless `Unauthenticated` that Midgard's `RpcExceptionExtensions.DecodeProblem` turns into `Failed(Unauthorized)` with no errors (the silence ruling). A form therefore recasts **whenever** `problem.Category is ErrorCategory.Unauthorized or ErrorCategory.InvalidCredentials`, regardless of `Errors`, to its one locally authored message: `TwoFactorForm` "Invalid authenticator code.", `RecoveryCodeForm` "Invalid recovery code entered.", `ResetPasswordForm` "The reset link is invalid or has expired." `LockedOut` and `NotAllowed` render their server text in the summary unchanged. (`Login.razor`'s narrower `Errors.Count == 0` condition is left as the spec rules it unchanged; it happens to work because the handler's in-process text equals its local text.)
  - `LoginWith2fa` (`@page "/Account/LoginWith2fa"`, `@layout GateLayout`): `[SupplyParameterFromQuery(Name = "challenge")] string? Challenge`. The shim distinguishes the intentional query removal from a new token arriving on the same component instance: in `OnParametersSet`, `if (!string.IsNullOrEmpty(Challenge) && Challenge != _challenge) { _challenge = Challenge; _stripPending = true; }` — a null or empty query (the stripped URL) leaves the field alone; a different non-empty value replaces it. It renders `<TwoFactorForm Challenge="@_challenge" />` and nothing else, and in `OnAfterRender` with `_stripPending` set soft-navigates to `Account/LoginWith2fa` (no query) with `ReplaceHistoryEntry = true` and clears the flag. That is how "the shim replaces history state after reading the token" (§6.5) survives the same-component navigation and still honors a second challenge.

- [ ] **Step 1: Write the failing tests.** `TwoFactorFormTests` (validator registered; `_sessionTransition` substitute registered):

```csharp
[Fact]
void A_missing_challenge_renders_the_expired_state_without_calling_the_service()
{
	var form = Render<TwoFactorForm>();
	form.Find(".norse-challenge-expired").TextContent.ShouldContain("expired");
	form.FindAll("form").ShouldBeEmpty();
	_service.DidNotReceiveWithAnyArgs().TwoFactorSignIn(default!, default);
}

[Fact]
async Task Success_begins_the_session_transition_with_the_server_url_and_no_history()
{
	_service.TwoFactorSignIn(Arg.Is<TwoFactorSignInRequest>(r => r.Challenge == "tok" && r.Code == "123456" && r.RememberMachine), Arg.Any<CancellationToken>())
		.Returns(Outcome<NavigationResult>.Ok(new() { NextUrl = "/" }));
	var form = Render<TwoFactorForm>(p => p.Add(f => f.Challenge, "tok"));
	await form.FindAll("fluent-text-input")[0].ChangeAsync("123456");
	await form.Find("fluent-checkbox").ChangeAsync(true);
	await form.Find("form").SubmitAsync();
	_sessionTransition.Received(1).Begin(new() { NextUrl = "/" });
	Navigation.History.ShouldBeEmpty();
}

[Theory]
[InlineData(ErrorCategory.InvalidCredentials, "Invalid email or password.")] // the handler-shaped, in-process outcome: SignInRefusal's shared text
[InlineData(ErrorCategory.Unauthorized, null)]                                // the gRPC-decoded outcome: trailerless Unauthenticated, no errors
async Task A_credential_failure_in_either_wire_shape_renders_the_local_message(ErrorCategory category, string? serverText)
{
	var failed = serverText is null ?
		new Failed(new Problem { Category = category }) :
		new Failed(Problem.ModelError(category, serverText));
	_service.TwoFactorSignIn(Arg.Any<TwoFactorSignInRequest>(), Arg.Any<CancellationToken>()).Returns(new Outcome<NavigationResult>(failed));
	var form = Render<TwoFactorForm>(p => p.Add(f => f.Challenge, "tok"));
	await form.FindAll("fluent-text-input")[0].ChangeAsync("123456");
	await form.Find("form").SubmitAsync();
	form.Markup.ShouldContain("Invalid authenticator code.");
	form.Markup.ShouldNotContain("Invalid email or password.");
}

[Fact]
async Task An_invalid_code_can_be_corrected_and_resubmitted() // reject → edit → resubmit, Received(2)

[Fact]
void The_recovery_code_link_carries_the_same_challenge() =>
	Render<TwoFactorForm>(p => p.Add(f => f.Challenge, "a b")).Find("a[href*='LoginWithRecoveryCode']")
		.GetAttribute("href").ShouldBe("Account/LoginWithRecoveryCode?challenge=a%20b");

[Fact]
async Task Locked_out_renders_the_server_text_in_the_summary()
```

`LoginWith2faTests`: render with `Navigation.NavigateTo("Account/LoginWith2fa?challenge=A")` before `Render<LoginWith2fa>()`; assert `page.FindComponent<TwoFactorForm>().Instance.Challenge.ShouldBe("A")` and the history's latest entry is `Account/LoginWith2fa` with `ReplaceHistoryEntry` true; then `A_stripped_url_keeps_the_token` (`page.Render()` after the strip still passes `"A"`); then `A_second_challenge_replaces_the_first_and_is_stripped_too`: `Navigation.NavigateTo("Account/LoginWith2fa?challenge=B")` on the same rendered instance (bUnit re-renders the component on `LocationChanged` for `[SupplyParameterFromQuery]`), assert the form's `Challenge` is `"B"`, the latest history entry is again the bare route with `ReplaceHistoryEntry` true, and a submit sends `Challenge == "B"` to the service.

- [ ] **Step 2: Run** — build errors.
- [ ] **Step 3: Implement** the two components as specified in Interfaces.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Stage** and stop.

### Task 3.6: `RecoveryCodeForm` + `LoginWithRecoveryCode` shim

**Files:**
- Create: `Heimdall/src/AuthN.Components.FluentUI/RecoveryCodeForm.razor`, `LoginWithRecoveryCode.razor`
- Test: `Heimdall/tests/AuthN.Components.FluentUI.Tests/RecoveryCodeFormTests.cs` (shim covered by one fact inside it, the `LoginWith2faTests` shape)

**Interfaces:**
- Consumes: `IAuthenticationService.RecoveryCodeSignIn`; `RecoveryCodeSignInRequestValidator`; `ISessionTransition`.
- Produces: `RecoveryCodeForm` with `[Parameter] string? Challenge`, one text input bound to `_request.RecoveryCode` (label "Recovery code"), no remember-machine checkbox, the same expired state, the same recast rule (both categories, any `Errors`, local text "Invalid recovery code entered."), and the same replace-and-strip shim logic as Task 3.5 (a new non-empty token replaces the field; the stripped URL keeps it), sibling link back to `Account/LoginWith2fa?challenge=…`. `LoginWithRecoveryCode` is `@page "/Account/LoginWithRecoveryCode"`.

- [ ] **Step 1: Write the failing tests** — the Task 3.5 set minus the checkbox: the both-shapes recast theory, and the shim facts including `A_second_challenge_replaces_the_first_and_is_stripped_too`.
- [ ] **Step 2: Run** — build errors.
- [ ] **Step 3: Implement.** Share nothing by inheritance; the two forms are small and the duplication is the explicit ruling of the shim+unit shape.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Stage** and stop.

### Task 3.7: `ResetPasswordForm` + `ResetPassword` shim

**Files:**
- Create: `Heimdall/src/AuthN.Components.FluentUI/ResetPasswordForm.razor`, `ResetPassword.razor`
- Test: `Heimdall/tests/AuthN.Components.FluentUI.Tests/ResetPasswordFormTests.cs`, `ResetPasswordTests.cs`

**Interfaces:**
- Consumes: `IAuthenticationService.ResetPassword`; `ResetPasswordRequestValidator`.
- Produces: `ResetPasswordForm` with `[Parameter, EditorRequired] string Code` copied into `_request.Code` in `OnParametersSet`; inputs: email (`EmailInput`), password (`TextInputType.Password`, single field, no confirm — ruling 11); success → soft `Navigation.NavigateTo(result.NextUrl)`; field-keyed `Validation` failures land on `Password` through the base's server-error bridge; `InvalidCredentials` or `Unauthorized`, with or without errors, recast to "The reset link is invalid or has expired." (the Task 3.5 recast rule). `ResetPassword` shim: `@page "/Account/ResetPassword"`, `[SupplyParameterFromQuery(Name = "code")] string? Code`; when null or empty → `Navigation.NavigateTo("Account/InvalidPasswordReset", new NavigationOptions { ReplaceHistoryEntry = true })` in `OnParametersSet` and renders nothing; otherwise `<ResetPasswordForm Code="@Code" />`. The code is **not** stripped from history (it is single-use and the page needs it on reload).

- [ ] **Step 1: Write the failing tests.** Form: empty-submit lock; `Success_navigates_softly_to_the_confirmation_url`; `A_password_rule_failure_lands_on_the_password_field` (service returns `Err(Validation, { ["Password"] = ["Passwords must have at least one digit ('0'-'9')."] })`; assert the message renders and no navigation); `A_credential_failure_in_either_wire_shape_renders_the_local_message` (the Task 3.5 theory shape: `InvalidCredentials` with the handler's text, and `Unauthorized` with no errors, both render "The reset link is invalid or has expired." and never the server text). Shim: `A_missing_code_redirects_softly_to_the_invalid_reset_page` (`entry.Options.ReplaceHistoryEntry.ShouldBeTrue()`, uri ends with `Account/InvalidPasswordReset`); `A_present_code_is_handed_to_the_form`.
- [ ] **Step 2: Run** — build errors.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Stage** and stop.

### Task 3.8: `ConfirmEmailPanel` + `ConfirmEmailChangePanel` and their shims

**Files:**
- Create: `Heimdall/src/AuthN.Components.FluentUI/ConfirmEmailPanel.razor`, `ConfirmEmail.razor`, `ConfirmEmailChangePanel.razor`, `ConfirmEmailChange.razor`
- Test: `Heimdall/tests/AuthN.Components.FluentUI.Tests/ConfirmEmailPanelTests.cs`, `ConfirmEmailChangePanelTests.cs`

**Interfaces:**
- Consumes: `IAuthenticationService.ConfirmEmail` (`BoolResponse`), `.ConfirmEmailChange` (`NavigationResult`); `ISessionTransition`; `OutcomeComponentBase.DispatchAsync`.
- Produces:
  - `ConfirmEmailPanel` (`@inherits OutcomeComponentBase`): `[Parameter] string? UserId`, `[Parameter] string? Code`. Click-to-confirm (ruling 12): renders "Confirm your email address" with a `FluentButton` "Confirm"; a bare render dispatches nothing; click → `DispatchAsync(ct => Service.ConfirmEmail(new() { UserId = UserId!, Code = Code! }, ct), _ => _confirmed = true)`; confirmed → `<p role="alert">Thank you for confirming your email.</p>` with a login link; `Problem` → `<div class="alert alert-danger">Error confirming your email.</div>` (+ a NotFound-specific "We couldn't find that account."). Missing parameters → the expired state (same class as Task 3.5's, text "This confirmation link is invalid.").
  - `ConfirmEmailChangePanel`: `[Parameter] string? UserId`, `[Parameter] string? Email`, `[Parameter] string? Code`; click "Confirm email change"; success → `SessionTransition.Begin(result)`; failure → `alert-danger` "Error changing email.".
  - Shims: `@page "/Account/ConfirmEmail"` with `[SupplyParameterFromQuery] string? UserId, Code` (query names `userId`, `code`); `@page "/Account/ConfirmEmailChange"` with `userId`, `email`, `code`. Both `@layout GateLayout`.

- [ ] **Step 1: Write the failing tests** (`LogoutTests` shape):

```csharp
[Fact]
void A_bare_render_confirms_nothing()
{
	Render<ConfirmEmailPanel>(p => p.Add(x => x.UserId, "u").Add(x => x.Code, "c"));
	_service.DidNotReceiveWithAnyArgs().ConfirmEmail(default!, default);
}

[Fact]
void The_confirm_click_dispatches_and_renders_the_thanks()
{
	_service.ConfirmEmail(Arg.Is<ConfirmEmailRequest>(r => r.UserId == "u" && r.Code == "c"), Arg.Any<CancellationToken>())
		.Returns(Outcome<BoolResponse>.Ok(new() { Value = true }));
	var panel = Render<ConfirmEmailPanel>(…);
	panel.Find("fluent-button").Click();
	panel.Find("[role=alert]").TextContent.ShouldContain("Thank you for confirming");
}

[Fact]
void A_failure_renders_the_error_and_keeps_the_button()

[Fact]
void Missing_parameters_render_the_invalid_link_state_without_a_button()
```

`ConfirmEmailChangePanelTests`: bare render performs nothing; click → `_sessionTransition.Received(1).Begin(new() { NextUrl = "/Account/Login" })` with empty history; failure renders the error and never transitions.

- [ ] **Step 2: Run** — build errors.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS. Run `dotnet test Heimdall.slnx` — green.
- [ ] **Step 5: Update** `Heimdall/README.md` and `Heimdall/CLAUDE.md` project tables: list the seven operations on `AuthN.Services`, the seven validators on `AuthN.Components`, the new pages/units on `AuthN.Components.FluentUI`; `AuthN.Components.FluentUI.csproj` `<Description>` likewise.
- [ ] **Step 6: Stage** and stop. Ship gate: Heimdall PR, tag, publish (`HeimdallVersion` → next).

---

## Phase 4 — Himinbjörg

### Task 4.1: `UserManagerOutcomeView` gains the Train 1 read/write methods

**Files:**
- Modify: `Himinbjorg/src/Identity.Outcomes/UserManagerOutcomeView.cs`
- Test: `Himinbjorg/tests/Identity.Outcomes.Tests/UserManagerOutcomeViewTests.cs`

**Interfaces:**
- Produces (all `ValueTask<Outcome<…>>`, trailing `CancellationToken`, `GuardAsync` + `ToOutcome`):
  - `GetUserAsync(ClaimsPrincipal principal)` → `Outcome<NorseUser>`: null → `NotFound`; `KeyDestroyedException` → `Erased`.
  - `FindByIdAsync(string userId)` → `Outcome<NorseUser>`: same folds.
  - `ResetPasswordAsync(NorseUser user, string token, string newPassword)` → `Outcome<Unit>`.
  - `ConfirmEmailAsync(NorseUser user, string token)` → `Outcome<Unit>`.
  - `ChangeEmailAsync(NorseUser user, string newEmail, string token)` → `Outcome<Unit>`.

- [ ] **Step 1: Create the Himinbjörg fork** `feature/account-lifecycle-anonymous-tier` from `master`.
- [ ] **Step 2: Write the failing tests**, one fact per method in the existing file's shape (`_userManager` substitute; `Category(...)` helper):

```csharp
[Fact]
async Task GetUserAsync_answers_the_user_NotFound_or_Erased()   // principal with NameIdentifier; null → NotFound; throws KeyDestroyedException → Erased with receipt

[Fact]
async Task FindByIdAsync_answers_the_user_NotFound_or_Erased()

[Fact]
async Task ResetPasswordAsync_maps_InvalidToken_to_InvalidCredentials_and_password_rules_to_Validation()

[Fact]
async Task ConfirmEmailAsync_maps_InvalidToken_to_InvalidCredentials()

[Fact]
async Task ChangeEmailAsync_maps_DuplicateEmail_to_Conflict_and_an_indeterminate_write_to_Indeterminate()
```

- [ ] **Step 3: Run** `dotnet test Himinbjorg/tests/Identity.Outcomes.Tests` — build errors.
- [ ] **Step 4: Implement** the five methods (the `FindByEmailAsync` / `CreateAsync` bodies are the template).
- [ ] **Step 5: Run** — PASS.
- [ ] **Step 6: Stage** and stop.

### Task 4.2: `IRecoveryCodeReplacement` seam and `ReplaceRecoveryCodesAsync`

**Files:**
- Create: `Himinbjorg/src/Identity.Outcomes/IRecoveryCodeReplacement.cs`
- Modify: `Himinbjorg/src/Identity.Outcomes/UserManagerOutcomeView.cs`, `Himinbjorg/src/Identity.Web.Server/NorseUserManager.cs`
- Test: `Himinbjorg/tests/Identity.Outcomes.Tests/UserManagerOutcomeViewTests.cs`, `Himinbjorg/tests/Identity.Web.Server.Tests/NorseUserManagerRecoveryCodeTests.cs`, `OutcomeSurfaceEnforcementTests.cs`

**Interfaces:**
- Produces: `public interface IRecoveryCodeReplacement { Task<(IdentityResult Result, string[] Codes)> ReplaceRecoveryCodesAsync(NorseUser user, int number); }` in `Identity.Outcomes`; `NorseUserManager : UserManager<NorseUser>, IRecoveryCodeReplacement`; `UserManagerOutcomeView.ReplaceRecoveryCodesAsync(NorseUser user, int number, CancellationToken)` → `ValueTask<Outcome<string[]>>` (`userManager is IRecoveryCodeReplacement lossless ? … : throw new InvalidOperationException("… requires NorseUserManager")`); `NorseUserManager.GenerateNewTwoFactorRecoveryCodesAsync` overridden to delegate and return `Codes` only when `Result.Succeeded`, else `null` (framework contract) — but every Norse caller goes through the view, and the IL scan bans the name. Train 3 handlers consume the view method; nothing in Train 1 calls it at runtime, the seam ships now so Train 3's handlers are a pure addition (§5.2).

- [ ] **Step 1: Write the failing tests.**
  - `UserManagerOutcomeViewTests`: `ReplaceRecoveryCodesAsync_throws_loudly_on_a_stock_manager` (`Should.ThrowAsync<InvalidOperationException>`); `ReplaceRecoveryCodesAsync_answers_the_codes_and_keeps_an_indeterminate_result` — substitute `UserManager<NorseUser>` that also implements `IRecoveryCodeReplacement` (`Substitute.For<UserManager<NorseUser>, IRecoveryCodeReplacement>(…)`), returns `(IdentityResult.Failed(new IdentityError { Code = NorseIdentityErrorCodes.IndeterminateOutcome }), codes)` → category `Indeterminate`, never `Fault`; and `(IdentityResult.Success, codes)` → `Success<string[]>` same array.
  - `NorseUserManagerRecoveryCodeTests` (Web.Server tests; build a real `NorseUserManager` over a substituted `IUserStore<NorseUser>` that also implements `IUserTwoFactorRecoveryCodeStore<NorseUser>`, `IUserSecurityStampStore<NorseUser>`): `ReplaceRecoveryCodesAsync_returns_the_update_result_untouched_with_the_generated_codes` (store `UpdateAsync` returns the indeterminate failure → tuple carries it, `Codes.Length == 10`, `ReplaceCodesAsync` received once with the same codes); `GenerateNewTwoFactorRecoveryCodesAsync_delegates_to_the_lossless_path` (returns null on the indeterminate result; returns the codes on success).
  - `OutcomeSurfaceEnforcementTests`: add `"GenerateNewTwoFactorRecoveryCodesAsync"` to a new `_bannedByName` set checked in `Describe` for `_identityManagers` declaring types regardless of return type; a scan-level fact is already the single `[Fact]` — the ban is proven by the scan staying green after the override exists only on the exempt `NorseUserManager`.
- [ ] **Step 2: Run** both test projects — build errors.
- [ ] **Step 3: Implement.** `NorseUserManager.ReplaceRecoveryCodesAsync`: `ThrowIfDisposed`; `GetRecoveryCodeStore()` (private in the base → re-resolve as `Store as IUserTwoFactorRecoveryCodeStore<NorseUser> ?? throw NotSupportedException`); generate `number` codes with the protected `CreateTwoFactorRecoveryCode()` into a `HashSet<string>` until `Count == number` (the base's loop shape); `await store.ReplaceCodesAsync(user, codes, CancellationToken)`; `var result = await UpdateUserAsync(user)` (inside `SubjectCryptoScope.Begin(user.Id)` — `UpdateUserAsync` already opens it); return `(result, [.. codes])`. Override `GenerateNewTwoFactorRecoveryCodesAsync(user, number)` → `var (result, codes) = await ReplaceRecoveryCodesAsync(...); return result.Succeeded ? codes : null;`.
- [ ] **Step 4: Run** — PASS. Run `dotnet test Himinbjorg/tests/Identity.Web.Server.Tests -- --filter-class "*.OutcomeSurfaceEnforcementTests"` — PASS.
- [ ] **Step 5: Stage** and stop.

### Task 4.3: `DeferredCompletion`

**Files:**
- Create: `Himinbjorg/src/Identity.Web.Server/DeferredCompletion.cs`
- Modify: `Himinbjorg/src/Identity.Web.Server/LoginHandler.cs`, `LogoutHandler.cs`
- Test: `Himinbjorg/tests/Identity.Web.Server.Tests/DeferredCompletionTests.cs`, `LoginHandlerTests.cs`, `LogoutHandlerTests.cs`

**Interfaces:**
- Consumes: `IDeferredSignIn.BuildCompletionUrl(key, returnUrl)`; `NorseSignInManager.DeferredSignInKeyItemName`; a second items key produced by Task 4.4: `NorseSignInManager.DeferredRememberClientKeyItemName = "Norse.DeferredRememberClientKey"`.
- Produces: `sealed class DeferredCompletion(IDeferredSignIn deferredSignIn, IHttpContextAccessor httpContextAccessor)`, registered explicitly in `ServiceCollectionExtensions` (`.AddScoped<DeferredCompletion>()`; the generated handler registration covers handlers only). One method: `string? TryBuildUrl(string returnUrl)`:
  - no key on `HttpContext.Items` → `null`;
  - identity key only → `BuildCompletionUrl(identityKey, returnUrl)`;
  - remember key only → `BuildCompletionUrl(rememberKey, returnUrl)`;
  - both → `BuildCompletionUrl(identityKey, BuildCompletionUrl(rememberKey, returnUrl))` (outer completes the identity cookie, inner the remember cookie, §5.4).
  `LoginHandler` and `LogoutHandler` drop their private `TryGetDeferredCompletionUrl` copies and take `DeferredCompletion` by constructor.

- [ ] **Step 1: Write the failing tests** — `DeferredCompletionTests` with a substituted `IDeferredSignIn` whose `BuildCompletionUrl` returns `$"/_auth/complete?key={key}&returnUrl={Uri.EscapeDataString(returnUrl)}"`:

```csharp
[Fact] void No_stash_answers_null()
[Fact] void An_identity_stash_alone_wraps_the_return_url_once()
[Fact] void A_remember_stash_alone_wraps_the_return_url_once()
[Fact] void Both_stashes_chain_identity_outside_remember_inside()
// expected: "/_auth/complete?key=id&returnUrl=%2F_auth%2Fcomplete%3Fkey%3Drem%26returnUrl%3D%2F"
```

Update `LoginHandlerTests.CreateHandler` to construct `new LoginHandler(signInManager, new DeferredCompletion(deferredSignIn, accessor), accessor)` and keep every existing fact green; same for `LogoutHandlerTests`.

- [ ] **Step 2: Run** — build errors.
- [ ] **Step 3: Implement** `DeferredCompletion`; rewire both handlers; register it.
- [ ] **Step 4: Run** `dotnet test Himinbjorg/tests/Identity.Web.Server.Tests -- --filter-class "*.DeferredCompletionTests"` and the two handler classes — PASS.
- [ ] **Step 5: Stage** and stop.

### Task 4.4: `NorseSignInManager` — challenge token, redemption, remember-machine deferral

**Files:**
- Create: `Himinbjorg/src/Identity.Web.Server/TwoFactorChallenge.cs`
- Modify: `Himinbjorg/src/Identity.Web.Server/NorseSignInManager.cs`
- Test: `Himinbjorg/tests/Identity.Web.Server.Tests/TwoFactorChallengeTests.cs`, `NorseSignInManagerTests.cs`

**Prelude (not a step, a gate):** run the decompile ritual from Global Constraints and paste the relevant method bodies into the task's handoff note. Correct the reproduction below where RC3 differs.

**Interfaces:**
- Consumes: `IDataProtectionProvider` (new constructor parameter, last, after `deferredSignIn`); `IDeferredSignIn`; `IdentityConstants.TwoFactorRememberMeScheme`.
- Produces:
  - `sealed record TwoFactorChallenge(string UserId, string SecurityStamp, bool IsPersistent, string? LoginProvider)` with `const string Purpose = "Norse.Identity.TwoFactorChallenge.v1"`, `static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5)`, `string Protect(ITimeLimitedDataProtector protector)` → `protector.Protect($"v1|{UserId}|{SecurityStamp}|{(IsPersistent ? "1" : "0")}|{LoginProvider}", Lifetime)`, and `static bool TryUnprotect(ITimeLimitedDataProtector protector, string token, out TwoFactorChallenge challenge)` (`CryptographicException` → false; wrong version or field count → false; empty provider → null).
  - `NorseSignInManager`:
    - `public const string TwoFactorChallengeItemName = "Norse.TwoFactorChallenge"` — the minted token parked on `HttpContext.Items` for `LoginHandler`.
    - `public const string DeferredRememberClientKeyItemName = "Norse.DeferredRememberClientKey"`.
    - `SignInOrTwoFactorAsync` override: when `!bypassTwoFactor && IsTwoFactorEnabled && !IsTwoFactorClientRemembered` → read `UserManager.GetUserIdAsync` + `GetSecurityStampAsync`, mint, park, return `SignInResult.TwoFactorRequired`; never call the base on that branch; otherwise delegate to the base. `StoreTwoFactorInfo` deleted; the `schemes` retention and its `#pragma warning disable CS9107` go with it.
    - `public async Task<SignInResult> TwoFactorChallengeSignInAsync(string token, string code, bool rememberClient)`: unprotect (fail → `Failed`) → `UserManager.FindByIdAsync` (null → `Failed`; `KeyDestroyedException` propagates to the view, which classifies it) → `GetSecurityStampAsync` equals token stamp else `Failed` → `PreSignInCheck` (non-null → return it) → strip spaces/hyphens → `UserManager.VerifyTwoFactorTokenAsync(user, Options.Tokens.AuthenticatorTokenProvider, code)`; on success → `CompleteTwoFactorSignInAsync(user, challenge, isPersistent: challenge.IsPersistent, rememberClient)`; on failure → if `SupportsUserLockout` → `AccessFailedAsync`; `IsLockedOutAsync` → `LockedOut` else `Failed`.
    - `public async Task<SignInResult> RecoveryCodeChallengeSignInAsync(string token, string code)`: same prelude through `PreSignInCheck`; strip spaces; `UserManager.RedeemTwoFactorRecoveryCodeAsync(user, code)` (raw `IdentityResult` is allowed inside the exempt manager); succeeded → `CompleteTwoFactorSignInAsync(user, challenge, isPersistent: false, rememberClient: false)`; otherwise `Failed`, no lockout increment (parity).
    - `private async Task<SignInResult> CompleteTwoFactorSignInAsync(NorseUser user, TwoFactorChallenge challenge, bool isPersistent, bool rememberClient)` — the reproduction of the base's private `DoTwoFactorSignInAsync` (decompiled RC3, 2026-10-26), shared by both paths: **first** `if (UserManager.SupportsUserLockout && !(await UserManager.ResetAccessFailedCountAsync(user) ?? IdentityResult.Success).Succeeded) return SignInResult.Failed;` — the base gates both the authenticator and the recovery-code completion on a successful lockout reset and refuses the sign-in otherwise; on this store that write can answer `IndeterminateOutcome` with no exception, and a sign-in must not be issued over it. The base's protected `ResetLockout` throws a framework-private `IdentityResultException` on failure and is wrapped by a private `ResetLockoutWithResult`, neither of which an override can use, so the override calls `ResetAccessFailedCountAsync` directly (raw `IdentityResult` inside the exempt manager). Then claims `[new Claim("amr", "mfa")]` plus `new Claim(ClaimTypes.AuthenticationMethod, provider)` when the token's provider is non-null; if `rememberClient` → `RememberTwoFactorClientAsync(user)`; `SignInWithClaimsAsync(user, isPersistent, claims)`; `Success`. The base also signs out `IdentityConstants.ExternalScheme` and `TwoFactorUserIdScheme` when those schemes are registered: the two-factor-user-id cookie is never written on this platform any more, so that sign-out is dropped; the external-scheme sign-out is performed when `!Context.Response.HasStarted` and skipped on a circuit (an external cookie left behind on the Blazor Server path expires on its own; external login bypasses two-factor today, so the branch is unreachable until Train 3 revisits it — recorded as a known gap in the class doc).
    - `public override Task RememberTwoFactorClientAsync(NorseUser user)`: `!Response.HasStarted` → base; else build **the complete principal the base's `StoreRememberClient` builds** (decompiled RC3): `ClaimsIdentity(IdentityConstants.TwoFactorRememberMeScheme)` with `ClaimTypes.Name = userId` **and, when `UserManager.SupportsUserSecurityStamp`, `Options.ClaimsIdentity.SecurityStampClaimType = await UserManager.GetSecurityStampAsync(user)`** — the remember-me cookie's security-stamp validator reads that claim once the ticket is older than `SecurityStampValidatorOptions.ValidationInterval`, so a principal without it is a cookie the validator rejects on its first run after the interval; `StashSignIn(TwoFactorRememberMeScheme, principal, new AuthenticationProperties { IsPersistent = true })`, park the key under `DeferredRememberClientKeyItemName`. The principal is built by one private `BuildRememberClientPrincipalAsync(user)` so the direct and deferred paths cannot drift (the direct path still delegates to the base, and a parity test holds the two shapes together). Where the stamp claim is checked (decompiled RC3): `IsTwoFactorClientRememberedAsync` only authenticates the remember-me scheme and compares `ClaimTypes.Name` to the user id; the stamp is validated by the cookie scheme's `OnValidatePrincipal` (`SecurityStampValidator.ValidatePrincipalAsync` → `TwoFactorSecurityStampValidator`), and only once the ticket is older than `SecurityStampValidatorOptions.ValidationInterval` (30 minutes by default). Inside that interval a freshly issued cookie is accepted on its name claim alone, so a rotated stamp shows up as a rejection only on a request made after the interval has elapsed.
    - `public override Task ForgetTwoFactorClientAsync()`: `!HasStarted` → base; else `StashSignOut(TwoFactorRememberMeScheme)` parked under `DeferredRememberClientKeyItemName`.

- [ ] **Step 1: Write the failing tests.** `TwoFactorChallengeTests` (an `EphemeralDataProtectionProvider`):

```csharp
[Fact] void A_token_round_trips_every_field()
[Fact] void A_null_provider_round_trips_as_null()
[Fact] void A_garbage_token_fails_to_unprotect_without_throwing()
[Fact] void A_token_protected_under_another_purpose_is_refused()
[Fact] void The_lifetime_is_five_minutes() => TwoFactorChallenge.Lifetime.ShouldBe(TimeSpan.FromMinutes(5));
[Fact] void An_expired_token_is_refused() // protect with an ITimeLimitedDataProtector substitute whose Unprotect throws CryptographicException("expired") → TryUnprotect false; the real expiry clock is the framework's, not ours to re-test
```

`NorseSignInManagerTests` — extend the TestServer harness (`RunAsync` / `RunTwoFactorAsync`) with the data-protection provider and an in-memory user whose store answers two-factor enabled, authenticator token verification, and a security stamp. Replace `NorseSignInManager_defers_the_two_factor_cookie_once_the_response_has_already_started` and `…writes_the_two_factor_cookie_directly…` with:

```csharp
[Theory] [InlineData(true)] [InlineData(false)]
async Task A_two_factor_required_sign_in_parks_a_token_and_writes_no_cookie(bool responseAlreadyStarted)
// Result == TwoFactorRequired; Items[TwoFactorChallengeItemName] is a string; SetCookieHeaderPresent false; deferredSignIn received nothing

[Fact] async Task A_parked_token_unprotects_to_the_user_id_stamp_persistence_and_provider()
[Fact] async Task A_correct_authenticator_code_completes_the_sign_in_with_amr_mfa()
[Fact] async Task A_correct_authenticator_code_resets_the_failed_count_before_signing_in() // ResetAccessFailedCountAsync Received(1), ordered before SignInWithClaimsAsync
[Fact] async Task A_correct_recovery_code_resets_the_failed_count_before_signing_in()
[Theory] [InlineData("Failed")] [InlineData("IndeterminateOutcome")]
async Task A_failed_or_indeterminate_lockout_reset_refuses_the_authenticator_sign_in_and_issues_nothing(string resultKind)
// ResetAccessFailedCountAsync answers IdentityResult.Failed(DefaultError) or Failed(Code = NorseIdentityErrorCodes.IndeterminateOutcome):
// Result == Failed; no Set-Cookie; deferredSignIn received no StashSignIn; RememberTwoFactorClientAsync never ran (rememberClient: true in the call)
[Theory] [InlineData("Failed")] [InlineData("IndeterminateOutcome")]
async Task A_failed_or_indeterminate_lockout_reset_refuses_the_recovery_code_sign_in_and_issues_nothing(string resultKind)
[Fact] async Task A_wrong_code_counts_toward_lockout_and_answers_LockedOut_when_it_trips()
[Fact] async Task A_challenge_issued_before_a_stamp_rotation_is_refused_on_the_authenticator_path()
[Fact] async Task A_challenge_issued_before_a_stamp_rotation_is_refused_on_the_recovery_code_path()
[Fact] async Task A_recovery_code_completes_a_non_persistent_sign_in_and_never_counts_toward_lockout()
[Fact] async Task A_garbage_token_is_refused_without_throwing()
[Fact] async Task A_challenge_for_an_erased_user_lets_the_destroyed_key_surface() // FindByIdAsync throws KeyDestroyedException → Should.ThrowAsync<KeyDestroyedException>; the view folds it to Erased (Task 4.5) and the handler collapses Erased onto bad credentials (Task 4.6)
[Fact] async Task Spaces_and_hyphens_are_stripped_before_verification()
[Theory] [InlineData(true)] [InlineData(false)]
async Task Remember_client_writes_directly_or_stashes_under_the_remember_me_scheme(bool responseAlreadyStarted)
[Fact] async Task The_deferred_remember_client_principal_carries_the_same_claims_as_the_direct_cookie()
// direct path: decrypt the Set-Cookie value through the TestServer's registered TwoFactorRememberMeScheme cookie handler's ticket data format (resolve IOptionsMonitor<CookieAuthenticationOptions>.Get(scheme).TicketDataFormat) → claims;
// deferred path: the ClaimsPrincipal handed to StashSignIn → claims; assert both carry ClaimTypes.Name = userId and Options.ClaimsIdentity.SecurityStampClaimType = the user's stamp, and nothing else; identity AuthenticationType == TwoFactorRememberMeScheme on both
[Fact] async Task A_stashed_remember_client_cookie_is_honored_until_the_stamp_rotates_and_the_validation_interval_elapses()
// harness: AddIdentityCookies() on the TestServer so the real TwoFactorRememberMeScheme cookie and its SecurityStampValidator (TwoFactorSecurityStampValidator) are registered; a FakeTimeProvider
// (Microsoft.Extensions.TimeProvider.Testing, Version="*" as Midgard's Infrastructure.Web.Server.Tests references it) assigned to both SecurityStampValidatorOptions.TimeProvider and the cookie
// options' TimeProvider; the substituted UserManager answers GetSecurityStampAsync from a mutable field.
// 1. complete the stash through Midgard's real MapDeferredSignIn endpoint, carry the Set-Cookie into request 2: IsTwoFactorClientRememberedAsync(user) → true.
// 2. rotate the stamp (the field changes) and make request 3 at the same clock: → true — inside ValidationInterval the validator does not run; the cookie is accepted on its name claim.
// 3. advance the clock past SecurityStampValidatorOptions.ValidationInterval (default 30 min; the harness sets it explicitly to 30 min and advances 31) and make request 4 through the real cookie
//    validator with the same cookie: → false, and the response carries a Set-Cookie that expires the remember-me cookie (the validator rejects the principal).
// 4. control: the same four requests with the stamp never rotated → true on request 4 as well, so the rejection is the stamp, not the clock.
[Fact] async Task Forget_client_stashes_a_sign_out_once_the_response_has_started()
[Fact] async Task Refresh_sign_in_reaches_the_identity_stash_once_the_response_has_started() // RefreshSignInAsync on a started response → StashSignIn(ApplicationScheme, …)
```

- [ ] **Step 2: Run** `dotnet test Himinbjorg/tests/Identity.Web.Server.Tests -- --filter-class "*.NorseSignInManagerTests"` and `"*.TwoFactorChallengeTests"` — build errors.
- [ ] **Step 3: Implement** `TwoFactorChallenge` and the manager changes as specified. Update the class XML doc: the "every seam" list now reads `SignInWithClaimsAsync` ×2, `SignOutAsync`, `SignInOrTwoFactorAsync` (token, no cookie), `RememberTwoFactorClientAsync`, `ForgetTwoFactorClientAsync`; record the RC3 decompile date.
- [ ] **Step 4: Run** — PASS. Run the whole `Identity.Web.Server.Tests` project — green (the `MockSignInManager` helper gains the `IDataProtectionProvider` argument where it constructs a `NorseSignInManager`; the stock-manager substitute is unchanged).
- [ ] **Step 5: Stage** and stop.

### Task 4.5: `SignInManagerOutcomeView` challenge methods and `LoginHandler`'s `?challenge=`

**Files:**
- Modify: `Himinbjorg/src/Identity.Web.Server/SignInManagerOutcomeView.cs`, `LoginHandler.cs`
- Test: `Himinbjorg/tests/Identity.Web.Server.Tests/SignInManagerOutcomeViewTests.cs`, `LoginHandlerTests.cs`

**Interfaces:**
- Produces: `ValueTask<Outcome<SignInCompletion>> TwoFactorChallengeSignInAsync(string token, string code, bool rememberClient, CancellationToken)` and `RecoveryCodeChallengeSignInAsync(string token, string code, CancellationToken)` on the view — `signInManager is NorseSignInManager norse ? IdentityOutcomeMap.ToOutcome(await norse.…) : throw new InvalidOperationException("… requires NorseSignInManager")`, `TryClassify` catch as `PasswordSignInAsync` has. `LoginHandler`: on `RequiresTwoFactor` reads `httpContextAccessor.HttpContext!.Items[NorseSignInManager.TwoFactorChallengeItemName] as string ?? throw new InvalidOperationException("RequiresTwoFactor without a parked challenge")`, returns `NextUrl = UriHelper.BuildRelative(PathBase, "/Account/LoginWith2fa", QueryString.Create("challenge", token))` — **no** deferred-completion detour on this branch (nothing was stashed) and no `RememberMe` query.

- [ ] **Step 1: Write the failing tests.** View: `Challenge_sign_in_states_map_like_password_sign_in` (substitute `NorseSignInManager`? — it is sealed; construct a real one over the Task 4.4 harness pieces with a substituted user manager and an `EphemeralDataProtectionProvider`, drive `Success`/`Failed`/`LockedOut`), `A_stock_manager_fails_loud_on_the_challenge_methods` (`Should.ThrowAsync<InvalidOperationException>` against `MockSignInManager.Create()`), `An_erased_user_answers_Erased` (user manager `FindByIdAsync` throws `KeyDestroyedException` → category `Erased`). Handler: replace `Returns_the_two_factor_challenge_url_on_the_success_side…` to park `"tok"` under `TwoFactorChallengeItemName` on the `DefaultHttpContext` and assert `NextUrl == "/Account/LoginWith2fa?challenge=tok"`; add `A_two_factor_required_result_without_a_parked_token_throws`; add `The_two_factor_branch_never_routes_through_deferred_completion` (`deferredSignIn.DidNotReceiveWithAnyArgs().BuildCompletionUrl(default!, default!)` even when an identity key is present on Items — it is not, on that branch, but the assertion pins the absence of the detour).
- [ ] **Step 2: Run** — build errors / failures.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Stage** and stop.

### Task 4.6: `TwoFactorSignIn`, `RecoveryCodeSignIn`, `ForgotPassword`, `ResetPassword` handlers

**Files:**
- Create: `Himinbjorg/src/Identity.Web.Server/TwoFactorSignInCommand.cs`, `TwoFactorSignInHandler.cs`, `RecoveryCodeSignInCommand.cs`, `RecoveryCodeSignInHandler.cs`, `ForgotPasswordCommand.cs`, `ForgotPasswordHandler.cs`, `ResetPasswordCommand.cs`, `ResetPasswordHandler.cs`, `AccountLinks.cs`
- Modify: `Himinbjorg/src/Identity.Web.Server/AuthenticationService.cs`
- Test: `Himinbjorg/tests/Identity.Web.Server.Tests/TwoFactorSignInHandlerTests.cs`, `RecoveryCodeSignInHandlerTests.cs`, `ForgotPasswordHandlerTests.cs`, `ResetPasswordHandlerTests.cs`, `AccountLinksTests.cs`

**Interfaces:**
- Consumes: Task 4.1 view methods, Task 4.3 `DeferredCompletion`, Task 4.5 view methods, Heimdall records (bump `HeimdallVersion` in Himinbjörg's package pins first, or project references).
- Produces (every command `[Authorize(Policy = AuthNPolicies.Public)] sealed record {Op}Command({Record} Request) : CommandRequest<{Record}, {Response}>(Request)`, the `LoginCommand` doc shape; every handler `sealed class {Op}Handler(…) : IRequestHandler<{Op}Command, {Response}>`):
  - `internal static class SignInRefusal`: `static Outcome<NavigationResult> Collapse(Failed failed, Failed fallback)` — the hoist of `LoginHandler.Refused`'s three arms: `LockedOut` → its text, `NotAllowed` → its text, everything else (wrong credentials, stamp mismatch, `Indeterminate`, `Erased`) → the caller's one static `fallback` instance. `LoginHandler` keeps its own `_invalidCredentials` ("Invalid email or password.") and passes it; each two-factor handler holds its own single static instance (`TwoFactorSignInHandler`: `Problem.ModelError(InvalidCredentials, "Invalid authenticator code.")`; `RecoveryCodeSignInHandler`: `"Invalid recovery code entered."`), so the reference-identity collapse is per handler and the in-process text matches the form's own wording. Over gRPC the text is stripped anyway and the form recasts (Task 3.5's rule), so no host sees different words.
  - `TwoFactorSignInHandler(SignInManager<NorseUser> signInManager, DeferredCompletion completion, IHttpContextAccessor accessor)`: `signInManager.Outcome.TwoFactorChallengeSignInAsync(Challenge, Code, RememberMachine)`; `Failed` → `SignInRefusal.Collapse(failed, _invalidCode)`; `Success` → `NextUrl = completion.TryBuildUrl(appRoot) ?? appRoot`.
  - `RecoveryCodeSignInHandler`: same over `RecoveryCodeChallengeSignInAsync` with its own instance.
  - `ForgotPasswordHandler(UserManager<NorseUser> userManager, IEmailSender<NorseUser> emailSender, IHttpContextAccessor accessor)`: tripwire on the email stamp; `userManager.Outcome.FindByEmailAsync`; `NotFound`/`Erased`/any failure → the confirmation `NextUrl`; found and `!IsEmailConfirmedAsync` → the same; else `GeneratePasswordResetTokenAsync` → `WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token))` → `AccountLinks.ResetPassword(request, code)` → `SendPasswordResetLinkAsync(user, email, HtmlEncoder.Default.Encode(link))` → the same `NextUrl`. `NextUrl = UriHelper.BuildRelative(PathBase, "/Account/ForgotPasswordConfirmation")` in every branch; exactly one static `NavigationResult`-producing path so the wire shape cannot differ.
  - `ResetPasswordHandler(UserManager<NorseUser> userManager, IHttpContextAccessor accessor)`: tripwire; `FindByEmailAsync`; `NotFound`/`Erased` → success `NextUrl = /Account/ResetPasswordConfirmation`; found → decode `Code` (`Base64UrlDecode`; a `FormatException` → `InvalidCredentials` with the static "The reset link is invalid or has expired." model error) → `userManager.Outcome.ResetPasswordAsync(user, token, Password)`; `InvalidCredentials` → the same static model error; `Validation` → errors regrouped through `RegisterHandler.FieldFor` (hoist `FieldFor` to `internal static class IdentityErrorFields` so both handlers share it) onto `nameof(ResetPasswordRequest.Password)`; other failures pass through; success → the confirmation URL.
  - `AccountLinks` (`static class`): `string ResetPassword(HttpRequest request, string code)` → `UriHelper.BuildAbsolute(request.Scheme, request.Host, request.PathBase, "/Account/ResetPassword", QueryString.Create("code", code))`; `string ConfirmEmail(HttpRequest request, string userId, string code)` → `/Account/ConfirmEmail?userId=…&code=…` (used by Task 4.7).
  - `AuthenticationService` gains the four methods with the mirrored `[Authorize(Policy = AuthNPolicies.Public)]`.

- [ ] **Step 1: Write the failing tests** (handler facts with `MockUserManager`/`MockSignInManager`, `DefaultHttpContext` with `Request.Scheme = "https"`, `Host = new HostString("example.test")`):
  - `TwoFactorSignInHandlerTests`: `Success_answers_the_app_root`; `Success_on_a_circuit_answers_the_deferred_completion_url` (Items carries an identity key; `deferredSignIn.BuildCompletionUrl` substituted); `Both_stashes_produce_the_chained_url`; `Every_refusal_collapses_onto_the_handlers_one_static_instance` (driven once with `Failed`, once with a stamp mismatch, once with an `Erased` fold: all three `Problem`s are `ShouldBeSameAs` each other, carry `InvalidCredentials` and the text "Invalid authenticator code."); `LockedOut_carries_its_text`; `LoginHandlerTests.Wrong_user_and_wrong_password_produce_the_same_problem_instance` stays green over `SignInRefusal.Collapse`. The handler takes `SignInManager<NorseUser>` as `LoginHandler` does, and the view type-tests for `NorseSignInManager`, so these facts drive a real `NorseSignInManager` over substituted stores through the Task 4.4 harness rather than a substituted manager.
  - `ForgotPasswordHandlerTests`: `An_unknown_email_answers_the_confirmation_url_and_sends_nothing`; `An_erased_subject_answers_the_confirmation_url_and_sends_nothing`; `An_unconfirmed_email_answers_the_confirmation_url_and_sends_nothing`; `A_confirmed_email_sends_an_absolute_reset_link_with_the_encoded_token_and_answers_the_same_url` (assert the link starts with `https://example.test/Account/ResetPassword?code=` and the code Base64Url-decodes to the generated token); `Every_branch_answers_the_identical_NavigationResult` (structural equality across all four).
  - `ResetPasswordHandlerTests`: `An_unknown_or_erased_email_answers_the_same_invalid_link_problem_instance` (ruled in execution: unknown/erased folds onto the handler's one static `InvalidCredentials` instance, `ShouldBeSameAs` the invalid-token answer); `An_invalid_token_answers_InvalidCredentials_with_the_local_message`; `A_malformed_code_answers_the_same_InvalidCredentials`; `Password_rule_failures_regroup_onto_the_Password_field`; `An_indeterminate_write_passes_through`.
  - `AccountLinksTests`: the two builders produce absolute URLs with PathBase preserved.
- [ ] **Step 2: Run** — build errors.
- [ ] **Step 3: Implement** the four command/handler pairs, `AccountLinks`, `SignInRefusal`, `IdentityErrorFields`, the four service methods.
- [ ] **Step 4: Run** `dotnet test Himinbjorg/tests/Identity.Web.Server.Tests` — green including `OutcomeSurfaceEnforcementTests` (no raw `IdentityResult`/`SignInResult` call in any handler).
- [ ] **Step 5: Stage** and stop.

### Task 4.7: `ConfirmEmail`, `ConfirmEmailChange`, `ResendEmailConfirmation` handlers

**Files:**
- Create: `Himinbjorg/src/Identity.Web.Server/ConfirmEmailCommand.cs`, `ConfirmEmailHandler.cs`, `ConfirmEmailChangeCommand.cs`, `ConfirmEmailChangeHandler.cs`, `ResendEmailConfirmationCommand.cs`, `ResendEmailConfirmationHandler.cs`
- Modify: `Himinbjorg/src/Identity.Web.Server/AuthenticationService.cs`
- Test: `Himinbjorg/tests/Identity.Web.Server.Tests/ConfirmEmailHandlerTests.cs`, `ConfirmEmailChangeHandlerTests.cs`, `ResendEmailConfirmationHandlerTests.cs`, plus one `PostgresTestGroup` fact

**Interfaces:**
- Produces:
  - `ConfirmEmailHandler(UserManager<NorseUser> userManager)`: `userManager.Outcome.FindByIdAsync(UserId)` (`NotFound`/`Erased` pass through) → decode `Code` (`FormatException` → `InvalidCredentials`) → `userManager.Outcome.ConfirmEmailAsync(user, token)` → `BoolResponse { Value = true }`; failures pass through unchanged (§4.2).
  - `ConfirmEmailChangeHandler(UserManager<NorseUser> userManager, SignInManager<NorseUser> signInManager, IPrincipalAccessor principalAccessor, DeferredCompletion completion, IHttpContextAccessor accessor)`: tripwire on the email stamp; `FindByIdAsync(UserId)`; decode; `userManager.Outcome.ChangeEmailAsync(user, email.WireValue, token)` (`NorseUserManager` sets email and username together); on success: `var principal = await principalAccessor.GetPrincipalAsync(ct)`; if `userManager.GetUserId(principal) == user.Id.ToString()` → `await signInManager.RefreshSignInAsync(user)` (raw `Task`, allowed) → `NextUrl = completion.TryBuildUrl(appRoot) ?? appRoot`; else `NextUrl = /Account/Login` (PathBase-qualified), no refresh.
  - `ResendEmailConfirmationHandler(UserManager<NorseUser> userManager, IEmailSender<NorseUser> emailSender, IHttpContextAccessor accessor)`: tripwire; `FindByEmailAsync`; any failure → `BoolResponse { Value = true }`; found → `GenerateEmailConfirmationTokenAsync` → Base64Url → `AccountLinks.ConfirmEmail(request, userId, code)` → `SendConfirmationLinkAsync` → `Value = true`. Always true (§4.2).
  - `AuthenticationService` gains the three methods.

- [ ] **Step 1: Write the failing tests.**
  - `ConfirmEmailHandlerTests`: `Success_answers_true`; `An_unknown_user_answers_NotFound`; `An_invalid_token_answers_InvalidCredentials`; `A_malformed_code_answers_InvalidCredentials`; `An_erased_subject_answers_Erased`.
  - `ConfirmEmailChangeHandlerTests`: `A_confirmed_change_by_the_same_user_refreshes_the_sign_in_and_answers_the_root` (`signInManager.Received(1).RefreshSignInAsync(user)`); `A_confirmed_change_for_another_user_does_not_refresh_the_callers_session` (`FakePrincipal.For(otherId)`; `DidNotReceive().RefreshSignInAsync`; `NextUrl == "/Account/Login"`); `An_anonymous_caller_is_treated_as_another_user` (`FakePrincipal.Empty()`); `A_duplicate_email_answers_Conflict`; `An_invalid_token_answers_InvalidCredentials`; `A_refresh_on_a_started_response_answers_the_completion_url`.
  - `ResendEmailConfirmationHandlerTests`: `An_unknown_email_answers_true_and_sends_nothing`; `A_known_email_sends_an_absolute_confirmation_link_and_answers_true`.
  - `PostgresTestGroup` (existing fixture; find the file that hosts the `Failed_password_writes_no_users_history` fact and add beside it): `ConfirmEmailChange_writes_one_users_version_with_email_and_username_changed_together` — create a user, generate a change-email token through the real manager, dispatch the handler, then assert `users_history` grew by exactly one row and the live row's `email` and `user_name` (or their lookup hashes, per the protected-column shape) both reflect the new address.
- [ ] **Step 2: Run** — build errors.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the three handler classes and the Postgres fact — PASS.
- [ ] **Step 5: Stage** and stop.

### Task 4.8: Retire the nine scaffold pages; docs

**Files:**
- Delete: `Himinbjorg/src/Identity.Web.Server/Components/Pages/ForgotPassword.razor`, `ResetPassword.razor`, `LoginWith2fa.razor`, `LoginWithRecoveryCode.razor`, `ConfirmEmail.razor`, `ConfirmEmailChange.razor`, `ResendEmailConfirmation.razor`, `RegisterConfirmation.razor`, `InvalidUser.razor`
- Modify: `Himinbjorg/src/Identity.Web.Server/IdentityNoOpEmailSender.cs` (doc comment only), `ServiceCollectionExtensions.cs:57` (the sender registration), `README.md`, `CLAUDE.md`, `Identity.Web.Server.csproj` `<Description>` if it enumerates pages
- Create: `Himinbjorg/src/Identity.Web.Server/DevelopmentEmailSender.cs`, `Himinbjorg/tests/Identity.Web.Server.Tests/CapturingEmailSender.cs`
- Test: `Himinbjorg/tests/Identity.Web.Server.Tests/ScaffoldRetirementTests.cs` (new), `DevelopmentEmailSenderTests.cs` (new), `IdentityNoOpEmailSenderTests.cs` (exists: three completes-without-throwing facts; extend), `ServiceCollectionExtensionsTests.cs` (exists: asserts the last `IEmailSender<NorseUser>` descriptor's `ImplementationType` is `IdentityNoOpEmailSender`; rewrite that fact for the factory)

**Interfaces:**
- Produces: no `@page` in `Identity.Web.Server` for any of `/Account/ForgotPassword`, `/Account/ResetPassword`, `/Account/LoginWith2fa`, `/Account/LoginWithRecoveryCode`, `/Account/ConfirmEmail`, `/Account/ConfirmEmailChange`, `/Account/ResendEmailConfirmation`, `/Account/RegisterConfirmation`, `/Account/InvalidUser` — Yggdrasil's router would otherwise throw on the duplicate templates Heimdall now owns (§8).
- Produces: a development-only link capture, with the production sender unchanged in behavior. `IdentityNoOpEmailSender` stays a silent no-op (its doc comment claims "logs instead of sending", but the framework `NoOpEmailSender` it delegates to returns `Task.CompletedTask`; the comment is corrected, the delegation is kept). A reset link, a reset code and a confirmation link are credentials: they are never written to a log outside the `Development` environment, because the sender is registered unconditionally for every host and Midgard's service defaults export formatted logs through OpenTelemetry. Three pieces:
  - `sealed class DevelopmentEmailSender(ILogger<DevelopmentEmailSender> logger) : IEmailSender<NorseUser>` — three `[LoggerMessage(Level = LogLevel.Information)]` delegates (`ConfirmationLink(email, link)`, `PasswordResetLink(email, link)`, `PasswordResetCode(email, code)`), the value logged verbatim (the handlers in Tasks 4.6/4.7 pass the unencoded absolute URL; no HTML body is composed any more). Class doc: "development only; resolved by `AddNorseAuthenticationService` under `Environments.Development` and nowhere else; logs credentials by design."
  - `ServiceCollectionExtensions.AddNorseAuthenticationService` replaces `.AddSingleton<IEmailSender<NorseUser>, IdentityNoOpEmailSender>()` with a singleton factory: `sp => sp.GetRequiredService<IHostEnvironment>().IsDevelopment() ? ActivatorUtilities.CreateInstance<DevelopmentEmailSender>(sp) : new IdentityNoOpEmailSender()` (`IHostEnvironment` is always present on an `IHostApplicationBuilder`, which is what this method extends). The "a host wiring a real sender registers its own afterward and wins" comment stays true: a later registration still wins resolution.
  - `tests/Identity.Web.Server.Tests/CapturingEmailSender.cs` — `sealed class CapturingEmailSender : IEmailSender<NorseUser>` with `IReadOnlyList<(string Email, string Kind, string Value)> Sent` (`Kind` ∈ `confirmation`, `reset-link`, `reset-code`); the real-Postgres facts in Tasks 4.6/4.7 register it after `AddNorseAuthenticationService` to read the token out of a captured link instead of a log.

- [ ] **Step 0a: Write the failing sender tests** (`DevelopmentEmailSenderTests`, `Microsoft.Extensions.Diagnostics.Testing`'s `FakeLogger<DevelopmentEmailSender>`; add the package to `Identity.Web.Server.Tests.csproj` with `Version="*"` like its siblings): `A_password_reset_link_is_logged_at_information` (`SendPasswordResetLinkAsync(user, "u@example.test", "https://example.test/Account/ResetPassword?code=abc")` → one record at `Information` whose message contains the link verbatim); the same for the confirmation link and the reset code. Run → FAIL (type missing). Implement. Run → PASS.
- [ ] **Step 0b: Write the failing environment tests** (`ServiceCollectionExtensionsTests`): build `Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment })`, call `AddNorseAuthenticationService` the way the existing fact does, add a `FakeLoggerProvider` through `AddLogging`, build the provider, resolve `IEmailSender<NorseUser>` and send all three kinds with the token values `"https://example.test/reset?code=SECRET-LINK"`, `"https://example.test/confirm?code=SECRET-CONFIRM"`, `"SECRET-CODE"`:
  - `Under_Development_the_sender_logs_every_link_and_code` — `Environments.Development`; the resolved sender `ShouldBeOfType<DevelopmentEmailSender>()`; the collector holds three records containing the three values.
  - `Theory` `Outside_Development_nothing_token_bearing_is_logged(string environment)` with `Environments.Production` and `Environments.Staging` — the resolved sender `ShouldBeOfType<IdentityNoOpEmailSender>()`; the collector's records, joined, contain none of `SECRET-LINK`, `SECRET-CONFIRM`, `SECRET-CODE`, nor `u@example.test`.
  Run → FAIL (the existing registration resolves the no-op in every environment, so the Development fact fails). Implement the factory. Run → PASS, and the pre-existing `ImplementationType.ShouldBe(typeof(IdentityNoOpEmailSender))` fact is replaced by these two.
- [ ] **Step 1: Write the failing test:**

```csharp
[Fact]
void The_train_one_routes_no_longer_exist_in_this_assembly()
{
	string[] retired = ["/Account/ForgotPassword", "/Account/ResetPassword", "/Account/LoginWith2fa", "/Account/LoginWithRecoveryCode", "/Account/ConfirmEmail", "/Account/ConfirmEmailChange", "/Account/ResendEmailConfirmation", "/Account/RegisterConfirmation", "/Account/InvalidUser"];
	var routes = typeof(LoginHandler).Assembly.GetTypes()
		.SelectMany(t => t.GetCustomAttributes<RouteAttribute>())
		.Select(r => r.Template);
	routes.ShouldNotContain(template => retired.Contains(template, StringComparer.OrdinalIgnoreCase));
}
```

- [ ] **Step 2: Run** — FAIL (nine matches).
- [ ] **Step 3: Delete** the nine files. Grep `Identity.Web.Server` for `RegisterConfirmation`, `InvalidUser`, `RedirectToInvalidUser` and fix every remaining reference (`IdentityRedirectManager` keeps its generic methods; a `RedirectToInvalidUser` helper, if present, is deleted along with its callers in the Manage pages only if a Manage page calls it — otherwise leave the Manage tree untouched for Train 2).
- [ ] **Step 4: Run** `dotnet build Himinbjorg.slnx` and `dotnet test Himinbjorg.slnx` — green.
- [ ] **Step 5: Docs.** `README.md` line 63 and `CLAUDE.md` line 17/23: the scaffold is now "the `/Account/Manage` tree and the external-login callback", the anonymous tier is on gRPC; name `DeferredCompletion`, the challenge token and `IRecoveryCodeReplacement`. `IdentityNoOpEmailSender` doc comment drops the `RegisterConfirmation.razor` sentence and the "logs" claim: it sends nothing and logs nothing; `DevelopmentEmailSender` is the development-only logger; a host wiring a real sender registers its own afterward. `CLAUDE.md` records the rule: no token-bearing log line outside `Development`, enforced by `ServiceCollectionExtensionsTests`.
- [ ] **Step 6: Stage** and stop. Ship gate: Himinbjörg PR, tag, publish (`HiminbjorgVersion` → next). No migration changes in this train, so the local Postgres volumes need no wipe.

---

## Phase 5 — Bragi

### Task 5.1: Scenario members, fake arms, registrations

**Files:**
- Modify: `Bragi/src/DesignSystem.Stories/Authentication/AuthenticationScenario.cs`, `FakeAuthenticationService.cs`, `ServiceCollectionExtensions.cs`
- Test: `Bragi/tests/DesignSystem.Stories.Tests/Authentication/FakeAuthenticationServiceTests.cs`, `ServiceCollectionExtensionsTests.cs`

**Interfaces:**
- Consumes: Heimdall's seven operations and validators (bump Bragi's Heimdall package or project references).
- Produces: `AuthenticationScenario` gains `TwoFactorRequired = 8`, `InvalidTwoFactorCode = 9`, `InvalidRecoveryCode = 10`, `InvalidResetToken = 11`, `EmailConfirmationFailed = 12`. Fake arms (verbatim producer parity, §7):
  - `Login` under `TwoFactorRequired` → `Ok(NextUrl = "/Account/LoginWith2fa?challenge=story")`.
  - `TwoFactorSignIn`: `Success` → `Ok("/")`; `InvalidTwoFactorCode` → `InvalidCredentials` model error "Invalid email or password." (the handler's shared instance; the component recasts to its local text); `LockedOut` → the Login lockout text; `Fault`.
  - `RecoveryCodeSignIn`: `Success` → `Ok("/")`; `InvalidRecoveryCode` → `InvalidCredentials`; `LockedOut`; `Fault`.
  - `ForgotPassword`: `Success` → `Ok("/Account/ForgotPasswordConfirmation")`; `Fault`.
  - `ResetPassword`: `Success` → `Ok("/Account/ResetPasswordConfirmation")`; `InvalidResetToken` → `InvalidCredentials` model error "The reset link is invalid or has expired."; `RegistrationValidation` → the three-message `Password` dictionary (reused); `Fault`.
  - `ConfirmEmail`: `Success` → `Ok(true)`; `EmailConfirmationFailed` → `InvalidCredentials`; `Fault`.
  - `ConfirmEmailChange`: `Success` → `Ok("/Account/Login")`; `EmailConfirmationFailed` → `InvalidCredentials`; `Fault`.
  - `ResendEmailConfirmation`: `Success` → `Ok(true)`; `Fault`.
  - `AddNorseStoryFakes()` registers the seven validators.

- [ ] **Step 1: Write the failing tests** — one parity fact per arm in `FakeAuthenticationServiceTests` (the existing `Login_under_*` shape), a `Does_not_apply` fact per method for one foreign scenario, and in `ServiceCollectionExtensionsTests` `Registers_every_train_one_validator` resolving all seven `IValidator<T>`.
- [ ] **Step 2: Run** — build errors (interface not implemented).
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Stage** and stop.

### Task 5.2: Stories, driven-story locks, catalog docs

**Files:**
- Modify: `Bragi/src/DesignSystem.Stories/Authentication/Login.stories.razor`, `ForgotPassword.stories.razor`, `TwoFactor.stories.razor`, `ResetPassword.stories.razor`, `ScenarioCatalog.md`
- Create: `Bragi/src/DesignSystem.Stories/Authentication/RecoveryCodeSignIn.stories.razor`, `ConfirmEmail.stories.razor`, `ConfirmEmailChange.stories.razor`, `ResendEmailConfirmation.stories.razor`
- Test: `Bragi/tests/DesignSystem.Stories.Tests/DrivenStoryNavigationTests.cs`
- Modify: `Bragi/CLAUDE.md`, `Bragi/README.md`

**Interfaces:**
- Consumes: Heimdall units with parameters (`TwoFactorForm Challenge`, `RecoveryCodeForm Challenge`, `ResetPasswordForm Code`, `ConfirmEmailPanel UserId/Code`, `ConfirmEmailChangePanel UserId/Email/Code`), `StoryDriver` (`Email` slot carries the code for the 2FA/recovery forms; `FillAndSubmit` fills the first text input with `Email` and the first password input with `Password`, so the forms' first text input is the code — verify `storyDriver.js` selector order and adjust the story's `Email` value accordingly), `ScenarioScope`, `LayoutView(GateLayout)`.
- Produces stories:
  - `Login`: + "Two-Factor Required" (`TwoFactorRequired`, `FillAndSubmit`).
  - `ForgotPassword`: "Default" (form in `GateLayout`), "Validation Errors" (`SubmitOnly`), "Email Sent" (existing confirmation page), "Fault".
  - `TwoFactor` (retitle "Authentication/Two-Factor"): "Default" (`TwoFactorForm Challenge="story"`), "Validation Errors", "Invalid Code" (`InvalidTwoFactorCode`, `FillAndSubmit Email="000000"`), "Locked Out", "Expired Challenge" (`Challenge=null`), existing "Locked Out" page story renamed "Lockout Page".
  - `RecoveryCodeSignIn`: "Default", "Validation Errors", "Invalid Code", "Expired Challenge".
  - `ResetPassword`: "Default" (`ResetPasswordForm Code="story"`), "Validation Errors", "Invalid Link" (existing page), "Password Policy" (`RegistrationValidation`), "Invalid Token" (`InvalidResetToken`), "Password Reset" (existing).
  - `ConfirmEmail`: "Confirm" (bare), "Confirmed" (`ClickOnly`), "Failed" (`EmailConfirmationFailed`, `ClickOnly`), "Invalid Link".
  - `ConfirmEmailChange`: "Confirm", "Confirmed" (transition recorded), "Failed".
  - `ResendEmailConfirmation`: "Default", "Validation Errors", "Sent" (`FillAndSubmit`).

- [ ] **Step 1: Write the failing tests** in `DrivenStoryNavigationTests` (one fact per surface, §7 and §10):

```csharp
[Fact] async Task A_two_factor_success_records_one_transition_and_no_history()
[Theory] [InlineData(AuthenticationScenario.InvalidTwoFactorCode)] [InlineData(AuthenticationScenario.LockedOut)]
async Task A_pinned_two_factor_story_renders_its_state_without_navigating(AuthenticationScenario scenario)
[Fact] async Task A_recovery_code_success_records_one_transition()
[Fact] async Task A_forgot_password_success_records_one_soft_navigation()  // Navigation.History single, ForceLoad false, Transitions empty
[Fact] async Task A_reset_password_success_records_one_soft_navigation()
[Fact] async Task A_confirm_email_click_renders_in_place_with_no_navigation()
[Fact] async Task A_confirm_email_change_click_records_one_transition()
[Fact] async Task A_failed_story_records_nothing()  // ConfirmEmail under EmailConfirmationFailed
```

- [ ] **Step 2: Run** — build errors (components not yet referenced) or failures.
- [ ] **Step 3: Implement** the story files; update `ScenarioCatalog.md`'s authentication table with the five new rows and a "Train 1 surfaces" note on which stories drive which scenario; update `Bragi/CLAUDE.md` line 17 and `README.md` (new states listed).
- [ ] **Step 4: Run** `dotnet test Bragi.slnx` — green. Run the stories host (`dotnet run --project Yggdrasil/src/Hosting.Stories` on project references) and open each new story through the MCP `list_stories` / `render` tools; record any story that renders a validation error or an exception.
- [ ] **Step 5: Stage** and stop. Ship gate: Bragi PR, tag, publish.

---

## Phase 6 — Yggdrasil

### Task 6.1: Package bumps

**Files:**
- Modify: `Yggdrasil/Directory.Packages.props:5-13`

- [ ] **Step 1: Create the fork** `feature/account-lifecycle-train-1`.
- [ ] **Step 2: Bump** `AsgardVersion`, `MidgardVersion`, `HeimdallVersion`, `HiminbjorgVersion`, `BragiVersion` to the tags shipped in Phases 1–5.
- [ ] **Step 3: Run** `dotnet build Yggdrasil.slnx` — green (no duplicate-route exception at runtime is verified in Task 6.3).
- [ ] **Step 4: Stage** and stop.

### Task 6.2: Client-side `AddNorsePolicies()`

**Files:**
- Modify: `Yggdrasil/src/Hosting.Web.Client/Program.cs:32-38`
- Test: `Yggdrasil/tests/Hosting.Web.Client.Tests/CompositionTests.cs`

**Interfaces:**
- Consumes: Task 2.2's generated `Norse.Hosting.Web.Client.NorsePolicyRegistration.AddNorsePolicies`.

- [ ] **Step 1: Write the failing test** (fully qualified, for the reason the file's existing comment records):

```csharp
[Fact]
async Task AddNorsePolicies_registers_the_account_policy_on_the_client()
{
	ServiceCollection services = new();
	services.AddLogging().AddAuthorizationCore();
	Client.NorsePolicyRegistration.AddNorsePolicies(services);
	using var provider = services.BuildServiceProvider();
	var policy = await provider.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync(AuthNPolicies.Account);
	policy.ShouldNotBeNull();
	var evaluator = provider.GetRequiredService<IAuthorizationService>();
	ClaimsPrincipal anonymous = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, NorseRoles.Anonymous)], "Test"));
	(await evaluator.AuthorizeAsync(anonymous, null, AuthNPolicies.Account)).Succeeded.ShouldBeFalse();
}
```

- [ ] **Step 2: Run** — build error (`NorsePolicyRegistration` not generated until the chain includes the policy generator — confirm `Infrastructure.Web.Client` is referenced with `Generator="true"`, it is).
- [ ] **Step 3: Implement** `.AddNorsePolicies()` after `.AddAuthorizationCore()` in `Program.cs`, with a comment mirroring the server's ("every `[NorsePolicy]` declaration in the reference set, registered once — the WASM router evaluates `[Authorize(Policy = …)]` and throws on an unknown name").
- [ ] **Step 4: Run** `dotnet test Yggdrasil/tests/Hosting.Web.Client.Tests` — PASS.
- [ ] **Step 5: Stage** and stop.

### Task 6.3: End-to-end verification and docs

**Files:**
- Modify: `Yggdrasil/CLAUDE.md:43` (identity page-tree row: the anonymous lifecycle pages are Heimdall's; Himinbjörg keeps `/Account/Manage/*` and the external-login callback)
- Test: `Yggdrasil/tests/Hosting.Web.Server.Tests/Authentication/ChallengeAndForbidTests.cs` (one sibling fact)

- [ ] **Step 1: Write the failing test** — `A_browser_principal_reaches_the_forgot_password_page_from_heimdalls_assembly`: `WebServerHost.StartAsync()`, browser client GET `/Account/ForgotPassword` → 200, body contains `authn-forgot-password` (the Heimdall form's `FormName`), proving the route resolves from Heimdall and not a stale Himinbjörg page (which would have thrown on duplicate templates at startup).
- [ ] **Step 2: Run** — FAIL until the bump lands (or PASS immediately if Task 6.1 is already staged; either way keep it).
- [ ] **Step 3: Run the composition end to end.** From Bifröst: `dotnet run --project src/Orchestration.AppHost`. On the Server host and the WASM host:
  - register, enable 2FA through the (still-Himinbjörg) Manage page, log out, log in → lands on `/Account/LoginWith2fa?challenge=…`, history entry replaced, code accepted, "remember this machine" checked → two chained `/_auth/complete` hops on Server, direct cookies on WASM, lands on `/`;
  - log out, log in again → no 2FA prompt (machine remembered);
  - recovery-code path with a wrong then a right code;
  - resend confirmation for the registered account → "Verification email sent"; copy the confirmation link from the Aspire dashboard's structured log for `Norse.Identity.Web.Server.DevelopmentEmailSender` (the AppHost runs the web server under `Development`, so Task 4.8's factory resolves the logging sender) → open it → click-to-confirm → thanks; the account is now `EmailConfirmed`;
  - forgot password with an unknown email, and with a second registered-but-unconfirmed account → identical confirmation page and no link logged for either;
  - forgot password with the confirmed account → the same confirmation page, and a reset link logged; open it → set a new password → confirmation page → login with the new password; a second use of the same link → "The reset link is invalid or has expired";
  - log in with the old password → "Invalid email or password" (the stamp rotated, the old 2FA challenge if any is refused).
  Record the outcome of each in the task handoff. A failure in the chained hop is the §5.4 re-plan trigger.
- [ ] **Step 4: Update** `Yggdrasil/CLAUDE.md` line 43 and `README.md` if it narrates the page tree.
- [ ] **Step 5: Run** `dotnet test Yggdrasil.slnx` — green.
- [ ] **Step 6: Stage** and stop. Ship gate: Yggdrasil PR, tag. Train 1 is done; Train 2's plan starts from the shipped state.

---

## Self-review record

**Spec coverage (Train 1 scope):** §4.1 conventions → Tasks 3.1/3.3; §4.2 seven operations and the `Login` change → 3.1, 4.5, 4.6, 4.7; §4.4 policy → 1.1, 2.1, 2.2, 3.2, 6.2; §5.1 handler shape → 4.6/4.7; §5.2 view methods and the `IRecoveryCodeReplacement` seam (shipped now, consumed in Train 3) → 4.1/4.2; §5.3 token + stamp → 4.4; §5.4 remember/forget + `DeferredCompletion` → 4.3/4.4; §5.8 `InvalidUser`/`RegisterConfirmation` → 4.8; §6.1 shim+unit → 3.5–3.8; §6.3 continuation rule → every component task; §6.5 hand-off → 3.5/3.6; §6.7 validators → 3.3; §7 Train 1 stories and scenarios → 5.1/5.2; §8 client policies + ordering hazard → 6.1–6.3; §10 testing shapes → every task. Out of Train 1 by design: §4.3, §5.5, §5.6, §5.7, §5.9, §6.2, §6.4 Manage tree, §6.6, the `AccountScenario`/`FakeAuthenticationStateProvider` fakes.

**Type consistency:** `TwoFactorChallengeItemName` / `DeferredRememberClientKeyItemName` (4.4) are read by 4.3 and 4.5; `DeferredCompletion.TryBuildUrl(string)` (4.3) is used in 4.6/4.7; `SignInRefusal` and `IdentityErrorFields` (4.6) are the hoists of `LoginHandler.Refused`/`_invalidCredentials` and `RegisterHandler.FieldFor`; `AccountLinks.ResetPassword(HttpRequest, string)` / `ConfirmEmail(HttpRequest, string, string)` (4.6) are used in 4.6/4.7; `NorseRoles.Anonymous` (1.1) in 2.1, 3.2, 6.2; the unit parameter names (`Challenge`, `Code`, `UserId`, `Email`) in 3.5–3.8 match 5.2's story usage.

**Proportion:** 6 phases, 17 tasks, against a 279-line spec of which Train 1 is roughly a third. Code bodies appear only where the signature and tests do not determine them (the token format, the redemption algorithm, the chained URL).

## Plan review record (r1 → r2, 2026-10-26)

Six findings on r1, each checked before the revision; PL1 and PL2 against an `ilspycmd` 11.1 decompile of `Microsoft.AspNetCore.Identity.dll` from `Microsoft.AspNetCore.App 11.0.0-rc.3.26522.148`.

| # | Finding | Verified | Change |
|---|---|---|---|
| PL1 | The deferred remember-machine principal omitted the security-stamp claim | Yes: `StoreRememberClient` adds `Options.ClaimsIdentity.SecurityStampClaimType` when `SupportsUserSecurityStamp` | Task 4.4: one `BuildRememberClientPrincipalAsync` with both claims; parity test direct vs deferred; a stashed cookie is honored until the stamp rotates **and** the validation interval elapses (r3) |
| PL2 | Two-factor completion did not gate on a successful lockout reset, and the recovery path skipped it | Yes: `DoTwoFactorSignInAsync` returns `Failed` when `ResetLockoutWithResult` fails, on both paths; `ResetLockout` throws a private `IdentityResultException` | Task 4.4: shared `CompleteTwoFactorSignInAsync` calls `ResetAccessFailedCountAsync` first and refuses on any non-success; theories for `Failed` and `IndeterminateOutcome` on both paths issue nothing; recovery success resets the count |
| PL3 | Forms recast only an empty `InvalidCredentials`; gRPC delivers `Unauthorized` with no errors and in-process delivers text | Yes: `RpcExceptionExtensions.FromStatusAlone` maps trailerless `Unauthenticated` to `Unauthorized`; `LoginHandler` sends text in-process | Tasks 3.5–3.7: recast on either category regardless of `Errors`, one local message per form, a both-shapes theory per form; Task 4.6: per-handler static refusal instances with form-matching text through `SignInRefusal.Collapse` |
| PL4 | The shim kept the first token forever (`??=`) and stripped only on first render | Yes | Tasks 3.5/3.6: a new non-empty query value replaces the field and re-arms the strip; the stripped URL keeps it; A → strip → B → strip test |
| PL5 | The end-to-end steps read links from a sender that logs nothing, and the forgot-password account was unconfirmed | Accepted: the framework `NoOpEmailSender` returns `Task.CompletedTask` (reviewer's check of the installed implementation; matches the aspnetcore source) | Task 4.8 Step 0: a `Development`-only `DevelopmentEmailSender` logs every link through `[LoggerMessage]`, chosen by a factory on `IHostEnvironment`; `CapturingEmailSender` for the real-Postgres facts; Task 6.3: confirm the account through the logged link first, then exercise the confirmed forgot-password branch and the unconfirmed one (r3) |
| PL6 | `ShouldBeSameAs` on two `const` strings cannot distinguish an alias from a copy | Yes: interned literals, compile-time substitution | Task 2.1: behavioral fact on the minted principal's role against `NorseRoles.Anonymous`; the alias is a reviewed one-liner |

**r2 → r3 (same day), two follow-ups on the r2 answers to PL5 and PL1:**

| # | Finding | Verified | Change |
|---|---|---|---|
| PL5-b (P1) | The r2 answer logged reset links, reset codes, confirmation links and email addresses at Information from a sender registered unconditionally, so every host exports credentials through OpenTelemetry | Yes: `ServiceCollectionExtensions.cs:57` registers the singleton for every environment; Yggdrasil's `Program.cs` calls `AddNorseAuthenticationService` with no environment branch | Task 4.8: `IdentityNoOpEmailSender` stays silent; `DevelopmentEmailSender` is resolved by a factory only when `IHostEnvironment.IsDevelopment()`; `CapturingEmailSender` serves the real-Postgres facts; `ServiceCollectionExtensionsTests` proves the no-op resolves and nothing token-bearing is logged under `Production` and `Staging` |
| PL1-b (P2) | The r2 test expected `IsTwoFactorClientRememberedAsync` to turn false the moment the stamp rotated | Yes: the method authenticates the scheme and compares the name claim only; the stamp is checked by the cookie validator after `ValidationInterval` | Task 4.4: the test advances a `FakeTimeProvider` past the interval and makes a fresh request through the real cookie validator before asserting rejection, with an inside-the-interval step and a never-rotated control; the prose no longer says "on every later login" |

Also recorded while verifying PL2: `ExternalLogin.razor` calls `ExternalLoginSignInAsync(..., bypassTwoFactor: true)`, so no Train 1 change is needed on the external-login page for the token path; the base's external-scheme sign-out inside two-factor completion is kept on an unstarted response and skipped on a circuit, noted in Task 4.4.
