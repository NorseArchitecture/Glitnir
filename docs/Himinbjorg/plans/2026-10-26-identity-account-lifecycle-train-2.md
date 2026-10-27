# Identity Account Lifecycle over gRPC — Train 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Pairs with superpowers:test-driven-development on every coding task, per standing law (Glitnir CLAUDE.md §2.8).

**Goal:** Ship the account core of Himinbjörg#48 sub-project A: the eight self-service operations (account status, phone, change/set password, change email, external-login list/remove, erasure) run as gRPC operations on a new `IAccountService` behind Heimdall's `Manage/` components, the six Himinbjörg scaffold pages they replace are deleted, the external-login link start becomes an antiforgery-free GET pair, and the last-sign-in-method invariant is enforced in the store.

**Architecture:** Heimdall's `AuthN.Services` gains `IAccountService` (`grpc.account.v1.AccountService`, policy `AuthNPolicies.Account`) with ten `[DataContract]` records and six validators; `AuthN.Components.FluentUI` gains a `Manage/` folder whose `_Imports.razor` stamps every page with `@layout ManageLayout` and `[Authorize(Policy = AuthNPolicies.Account)]`, pages are loader-over-form (`OutcomeComponentBase` page, `OutcomeFormComponentBase` form), and `ManageLayout` renders the server-authored `?status=` key through `ManageStatus`. Himinbjörg wraps each operation in an `Account/{Op}Command` + `{Op}Handler` pair behind an `AccountService` hydrate-and-send mirror, every handler starting from `userManager.Outcome.GetUserAsync(principal)`; `UserManagerOutcomeView` gains five write views; `FlushExecutor`'s `RemoveLogin` arm counts under a row lock and refuses the last sign-in method with `NorseIdentityErrorCodes.LastSignInMethod → Conflict`. Bragi pins every state through `AccountScenario`, `IdentityScenario`, `SessionScenario`; Yggdrasil bumps and proves the gate on both lanes.

**Tech Stack:** .NET 11 RC3 (`DotNetVersion` `11.0.0-rc.3.26522.148` in Yggdrasil CPM), C# 15, ASP.NET Core Identity 11 RC3, protobuf-net.Grpc 1.3.x code-first, Blazor (Server + WASM + bUnit 2.x), FluentUI Blazor 5.x, FluentValidation 12 via Blazilla, EF Core 11 RC2 (Npgsql + SqlClient), Testcontainers (`postgres:19rc1`, `mssql/server:2025-latest`), xUnit v3 on MTP, Shouldly, NSubstitute, BlazingStory (Bragi).

**Spec:** `../specs/2026-10-26-identity-account-lifecycle-over-grpc-design.md` (r3, approved 2026-10-26). Every §-reference below is to that spec unless marked otherwise. Train 1 shipped 2026-10-26 (`../plans/2026-10-26-identity-account-lifecycle-train-1.md`: Asgard 0.0.34, Midgard 0.0.37, Heimdall 0.0.21, Himinbjörg 0.0.25, Bragi 0.0.14, Yggdrasil `master`). Train 3 (§9) gets its own plan after this one ships.

**Plan revision:** r2, 2026-10-26. r1 → r2 (static review, three findings, all accepted): `ManageLayout`/`ManageNavMenu` moved out of the stamped `Manage/` folder to the project root beside `GateLayout` (a layout inside its own `@layout` folder is Blazor's documented infinite layout loop) with a `RouteView` chain fact added to Task 1.3; failure regrouping in Tasks 2.4 and 2.7 became `failed.Problem with { Errors = … }` so a mid-write erasure receipt survives, with an erased-during-write fact on both password handlers; Task 2.3 now creates the full eight-method `AccountService` and all eight command records in one buildable step, with Tasks 2.4–2.7 adding only handlers.

## Global Constraints

- **No automatic git commits — platform law overrides the skill template's commit steps.** Every task ends by staging the diff (`git add`) in the realm it touched and stopping. Never `git commit`, never push. The human commits.
- **Branch law (Bifröst CLAUDE.md §7):** never branch Bifröst. One feature fork per realm, created from `master` at the start of the realm's phase (every realm is on `master` with nothing in flight as of 2026-10-26): Heimdall `feature/account-lifecycle-account-core`, Himinbjörg `feature/account-lifecycle-account-core`, Bragi `feature/account-core-stories`, Yggdrasil `feature/account-lifecycle-train-2`. Glitnir work lands on Glitnir `master`.
- **Ship gates (§9):** each realm phase ends with PR merged, CI green, tag, publish. The next realm's phase develops against `UseProjectReferences=true` (local upstream) and only needs the upstream package at its own PR. Yggdrasil's bump must consume a Himinbjörg release that already deletes the six Manage pages (duplicate-route hazard, §8).
- **House rules:** `../../house-rules.md`, read in full at plan time: tabs; BOM-free UTF-8; LF; target-typed `new()`; collection expressions; primary constructors; expression bodies with the arrow on the declaration line; `is null`/`is not null`; dot-leading fluent chains; `ConfigureAwait(false)` in src, never in tests; `CancellationToken cancellationToken = default` last on every async method; `LoggerMessage` delegates; every class `sealed`/`abstract`/`static`; modifiers omitted at the default; XML docs on every src member; records for request/response types; `Outcome<T>` over throwing; no null collections (absence is `[]`); tests `public sealed`, bare `void`/`async Task` methods, sentence-shaped underscore names; warnings are errors.
- **Wire records** are `[DataContract] sealed record` with `[DataMember(Order = n)]`; email is `Result<EmailAddress>` plus the un-serialized `required string …Input` buffer whose setter parses (the `LoginRequest` shape); phone is `Result<PhoneNumber>` plus `PhoneInput` the same way (`Norse.Primitives.Pii.PhoneNumber`, E.164, format hint `+15551234567`; Midgard already registers its `Result<PhoneNumber>` serializer); tokens, codes and provider keys are opaque `string`s (§4.1). No `[Authorize]` and no mediator marker on any wire type (`RequestContractTests` locks it). Collections on responses are arrays initialized to `[]`, never `required` (protobuf-net leaves an empty collection untouched on deserialize).
- **Return-type rule (§4.1):** sign-in refresh / sign-out → `Outcome<NavigationResult>`; acknowledgment → `Outcome<BoolResponse>`; data → a response record.
- **Identity access law:** handlers and endpoints reach `UserManager` / `SignInManager` only through `.Outcome` views for anything returning `IdentityResult` / `SignInResult` (`OutcomeSurfaceEnforcementTests` IL scan; `Components/` stays exempt until Train 3). Read-only calls (`HasPasswordAsync`, `CheckPasswordAsync`, `IsEmailConfirmedAsync`, `GetTwoFactorEnabledAsync`, `GetAuthenticatorKeyAsync`, `CountRecoveryCodesAsync`, `GetLoginsAsync`, `GetPasskeysAsync`, `GetEmailAsync`, `Generate*TokenAsync`, `IsTwoFactorClientRememberedAsync`, `GetExternalAuthenticationSchemesAsync`, `GetExternalLoginInfoAsync`, `RefreshSignInAsync`, `SignOutAsync`) stay raw (§5.2).
- **Every account handler** starts `principalAccessor.GetPrincipalAsync` → `userManager.Outcome.GetUserAsync(principal)` and passes a `Failed` (`NotFound` / `Erased`) through unchanged; never `HttpContext.User` (§5.1). Every account command and every `AccountService` method carries `[Authorize(Policy = AuthNPolicies.Account)]`.
- **Continuation rule (§6.3):** `SetPhoneNumber`, `ChangePassword`, `SetPassword`, `RemoveExternalLogin`, `DeletePersonalData` → `ISessionTransition.Begin(result)`; `ChangeEmail` and the resend button → in-place text. The server is the only author of URLs: `NextUrl` is root-relative, PathBase-qualified via `UriHelper.BuildRelative`, and carries `?status=<key>` for the refresh family; `Heimdall/src/AuthN.Components/Shared/ManageStatus.cs` maps key → text.
- **Status keys (§6.3, extended here):** `password-changed`, `password-set`, `phone-updated`, `email-changed`, `external-login-removed`, `external-login-added`, `external-login-not-added`, `authenticator-reset` (text only; Train 3 emits it). The two `external-login-*added` keys are this plan's addition: the HTTP link callback (§5.7) has no gRPC answer and redirects with a key instead.
- **Routes** keep the scaffold's `/Account/Manage/*` templates (Heimdall CLAUDE.md). A Heimdall page and a Himinbjörg page with the same template cannot coexist in one Yggdrasil host; the Himinbjörg deletions (Task 2.8) ship before the Yggdrasil bump (Task 4.1).
- **No token-bearing log line outside `Development`** (Himinbjörg CLAUDE.md): the change-email link goes to `IEmailSender<NorseUser>` as a raw absolute URL, never to a logger.
- **Test commands:** `dotnet test <realm>.slnx` or `dotnet test tests/<Project> -- --filter-class "*.<ClassName>"`. VSTest `--filter` does not work on MTP. Container facts need Docker (`PostgresTestGroup` / `SqlServerTestGroup`).
- Realm paths are workspace-relative from the Bifröst root.

## Review Focus

The five input classes the spec implies but no task's tests would otherwise exercise, each pinned to the owning task:

1. **A passwordless account with exactly one external login asks to remove it** through a direct gRPC request (the page hides the button, the wire does not): the store refuses under the row lock, the wire answers `Conflict`, the row stays. → Task 2.2 (`A_passwordless_account_cannot_remove_its_only_login`, both providers), Task 2.6 (`A_last_sign_in_method_refusal_answers_Conflict_and_refreshes_nothing`).
2. **`SetPassword` for an account that already has one** (a stale page, or a hand-made request): `UserAlreadyHasPassword` answers `Conflict`, never 500, and the form renders it in `ModelValidationSummary`. → Task 2.4 (`An_account_that_already_has_a_password_answers_Conflict`), Task 1.6 (`A_conflict_renders_in_the_summary_without_a_transition`).
3. **`ChangeEmail` with the current address, differing only in case**: `Validation` on `NewEmail` ("Your email is unchanged."), no token generated, nothing sent. → Task 2.5 (`The_current_address_in_any_case_answers_Validation_and_sends_nothing`).
4. **`DeletePersonalData` from a passwordless account with an empty password on the wire** proceeds to the shred (the field is always on the wire, §5.6); **a wrong password on a password account** answers the one static `InvalidCredentials` instance and shreds nothing. → Task 2.7 (`A_passwordless_account_is_shredded_without_a_password_check`, `A_wrong_password_answers_the_static_instance_and_shreds_nothing`).
5. **An anonymous-cookie visitor opens `/Account/Manage`** (the Train 1 carried gap: the anonymous principal is authenticated, so it used to bounce through `/Account/Logout`): with `[Authorize(Policy = AuthNPolicies.Account)]` on every Heimdall Manage page, `AuthorizeRouteView` renders `RedirectToLogin` and the response is a redirect to `/Account/Login?returnUrl=…`. → Task 1.3 (`Every_manage_route_carries_the_account_policy_and_the_manage_layout`), Task 4.2 (`An_anonymous_browser_principal_is_redirected_from_the_manage_tree_to_login`).

## Plan boundary and gates

| Phase | Realm | Gate before it starts |
|---|---|---|
| 1 | Heimdall | Train 1 shipped (done 2026-10-26) |
| 2 | Himinbjörg | Phase 1 shipped (`HeimdallVersion` bump) |
| 3 | Bragi | Phase 1 shipped |
| 4 | Yggdrasil | Phases 2 and 3 shipped |

**Deviation from §10 recorded here:** the explicit Playwright phase for the Manage shell under WASM is **not** added in this train. The .NET 11 RC3 Mono WASM runtime does not compile on Chromium 156 (`mono_llvm_cpp_catch_exception`: "module uses a mix of legacy and new exception handling instructions"), so every WASM browser check in this environment fails before Blazor code runs; Train 1's own WASM leg was never browser-verified for the same reason. The phase lands in Train 3's plan, or earlier if the runtime pin or the browser changes. The Server-host end-to-end check in Task 4.3 stands.

**Re-plan triggers:** `[SupplyParameterFromQuery]` does not bind on `ManageLayout` under bUnit or under the Yggdrasil router (fallback: each Manage page reads `status` and passes it to the layout through a cascading value — a Task 1.3 change only); the minted identity cookie in Task 4.2 is rejected by the browser lane because the lane requires the anonymous cookie beside it (fallback: seed the anonymous cookie with an initial `GET /` and add the identity cookie to the same container); `RuntimeTypeModel` refuses `Result<PhoneNumber>` on the wire (not expected; `ResultSerializers.RegisterPiiScalar<PhoneNumber>` exists in Midgard).

## File Structure

**Heimdall** (`Heimdall/`)
- Create `src/AuthN.Services/IAccountService.cs`, `AccountStatusResponse.cs`, `SetPhoneNumberRequest.cs`, `ChangePasswordRequest.cs`, `SetPasswordRequest.cs`, `ChangeEmailRequest.cs`, `ExternalLoginsResponse.cs`, `ExternalLoginEntry.cs`, `ExternalProviderEntry.cs`, `RemoveExternalLoginRequest.cs`, `DeletePersonalDataRequest.cs`.
- Modify `src/AuthN.Services/AuthN.Services.csproj` — `<Description>` lists the new contract and records.
- Create `src/AuthN.Components/SetPhoneNumberRequestValidator.cs`, `ChangeEmailRequestValidator.cs`, `ChangePasswordRequestValidator.cs`, `SetPasswordRequestValidator.cs`, `RemoveExternalLoginRequestValidator.cs`, `DeletePersonalDataRequestValidator.cs`, `Shared/ManageStatus.cs`.
- Create `src/AuthN.Components.FluentUI/ManageLayout.razor`, `ManageNavMenu.razor` (beside `GateLayout.razor`, outside the stamped folder), `src/AuthN.Components.FluentUI/Manage/_Imports.razor`, `Index.razor`, `ProfileForm.razor`, `Email.razor`, `ChangeEmailForm.razor`, `ChangePassword.razor`, `ChangePasswordForm.razor`, `SetPassword.razor`, `SetPasswordForm.razor`, `ExternalLogins.razor`, `DeletePersonalData.razor`, `DeletePersonalDataForm.razor`.
- Move `src/AuthN.Components.FluentUI/PersonalData.razor` → `src/AuthN.Components.FluentUI/Manage/PersonalData.razor` (`git mv`).
- Modify `src/AuthN.Components.FluentUI/_Imports.razor` — `@using Norse.AuthN.Components.FluentUI.Manage`; `AuthN.Components.FluentUI.csproj` `<Description>`.
- Modify `tests/AuthN.Services.Tests/RequestContractTests.cs`.
- Create `tests/AuthN.Components.Tests/{Record}ValidatorTests.cs` (six), `tests/AuthN.Components.Tests/Shared/ManageStatusTests.cs`.
- Create `tests/AuthN.Components.FluentUI.Tests/Manage/TestShellLayout.razor`, `TestAppShell.cs`, `ManagePolicyLockTests.cs`, `ManageLayoutTests.cs`, `ManageNavMenuTests.cs`, `IndexTests.cs`, `ProfileFormTests.cs`, `EmailTests.cs`, `ChangeEmailFormTests.cs`, `ChangePasswordTests.cs`, `ChangePasswordFormTests.cs`, `SetPasswordTests.cs`, `SetPasswordFormTests.cs`, `ExternalLoginsTests.cs`, `DeletePersonalDataTests.cs`, `DeletePersonalDataFormTests.cs`.
- Move `tests/AuthN.Components.FluentUI.Tests/PersonalDataTests.cs` → `tests/AuthN.Components.FluentUI.Tests/Manage/PersonalDataTests.cs` (namespace follows the folder).
- Modify `README.md`, `CLAUDE.md` — the three project rows list the account core.

**Himinbjörg** (`Himinbjorg/`)
- Modify `src/Identity.Outcomes/NorseIdentityErrorCodes.cs` — `LastSignInMethod`; `IdentityOutcomeMap.cs` — one row; `UserManagerOutcomeView.cs` — `SetPhoneNumberAsync`, `ChangePasswordAsync`, `AddPasswordAsync`, `AddLoginAsync`, `RemoveLoginAsync`.
- Modify `src/Identity.Web.Server/Store/FlushExecutor.cs` — `RemoveLoginAsync` arm with the guard; `Store/ProviderStatements.cs` — `HasPasswordForUpdateAsync` and two `Dialect` lock spellings; `NorseUserStore.cs` — a `LastSignInMethodErrorCode` alias beside its siblings.
- Create `src/Identity.Web.Server/Account/AccountService.cs` and, in the same folder, `{Op}Command.cs` + `{Op}Handler.cs` for `GetAccountStatus`, `SetPhoneNumber`, `ChangePassword`, `SetPassword`, `ChangeEmail`, `GetExternalLogins`, `RemoveExternalLogin`, `DeletePersonalData` (namespace `Norse.Identity.Web.Server.Account`).
- Modify `src/Identity.Web.Server/IdentityErrorFields.cs` — `Regroup(errors, fieldFor)` overload and `ForPasswordChange`; `AccountLinks.cs` — `ConfirmEmailChange`; `ConfirmEmailChangeHandler.cs` — same-user `NextUrl` becomes `/Account/Manage/Email?status=email-changed`; `ServiceCollectionExtensions.cs` — `AddScoped<IAccountService, AccountService>()`; `IdentityComponentsEndpointRouteBuilderExtensions.cs` — manage group policy, `GET /LinkExternalLogin`, `GET /LinkExternalLoginCallback`.
- Delete `src/Identity.Web.Server/Components/Pages/Manage/Index.razor`, `Email.razor`, `ChangePassword.razor`, `SetPassword.razor`, `ExternalLogins.razor`, `DeletePersonalData.razor`. `Manage/_Imports.razor`, the seven Train 3 pages, `Shared/ManageLayout.razor`, `Shared/ManageNavMenu.razor`, `Pages/ExternalLogin.razor` and the passkey pieces stay.
- Modify `tests/Identity.Outcomes.Tests/IdentityOutcomeMapTests.cs`, `UserManagerOutcomeViewTests.cs`.
- Create `tests/Identity.Web.Server.Tests/LastSignInMethodSuite.cs`, `PostgresLastSignInMethodTests.cs`, `SqlServerLastSignInMethodTests.cs`, `Account/AccountServiceTests.cs`, `Account/{Op}HandlerTests.cs` (seven), `Account/DeletePersonalDataHandlerSuite.cs` + its Postgres/SQL Server pair, `IdentityErrorFieldsTests.cs`, `LinkExternalLoginEndpointTests.cs`.
- Modify `tests/Identity.Web.Server.Tests/ConfirmEmailChangeHandlerTests.cs`, `AccountLinksTests.cs`, `ScaffoldRetirementTests.cs`, `ScaffoldWritePathSuite.cs` (one seed), `ServiceCollectionExtensionsTests.cs`.
- Modify `README.md`, `CLAUDE.md`, `src/Identity.Web.Server/README.md`.

**Bragi** (`Bragi/`)
- Create `src/DesignSystem.Stories/Account/AccountScenario.cs`, `FakeAccountService.cs`, `Profile.stories.razor`, `Email.stories.razor`, `ChangePassword.stories.razor`, `SetPassword.stories.razor`, `ExternalLogins.stories.razor`, `DeletePersonalData.stories.razor`; `Identity/IdentityScenario.cs`, `FakeIdentityService.cs`, `PersonalData.stories.razor`; `Session/SessionScenario.cs`, `FakeAuthenticationStateProvider.cs`; `Shell/StoryShell.razor`, `StoryShellLayout.cs`.
- Modify `src/DesignSystem.Stories/ServiceCollectionExtensions.cs`, `_Imports.razor`, `Scenarios/StoryDriver.razor`, `wwwroot/storyDriver.js`, `ScenarioCatalog.md`.
- Create `tests/DesignSystem.Stories.Tests/Account/FakeAccountServiceTests.cs`, `Identity/FakeIdentityServiceTests.cs`, `Session/FakeAuthenticationStateProviderTests.cs`.
- Modify `tests/DesignSystem.Stories.Tests/ServiceCollectionExtensionsTests.cs`, `DrivenStoryNavigationTests.cs`, `Scenarios/StoryDriverTests.cs`, `Scenarios/storyDriver.test.mjs`, `CLAUDE.md`, `README.md`.

**Yggdrasil** (`Yggdrasil/`)
- Modify `Directory.Packages.props` — `HeimdallVersion`, `HiminbjorgVersion`, `BragiVersion`.
- Modify `tests/Hosting.Web.Server.Tests/Authentication/WebServerHost.cs` — `CreateIdentityClient()`; `ChallengeAndForbidTests.cs` — three facts; `tests/Hosting.Web.Client.Tests/CompositionTests.cs` — one fact.
- Modify `CLAUDE.md` — identity page-tree row.

---

## Phase 1 — Heimdall

### Task 1.1: `IAccountService` and its ten wire records

**Files:**
- Create: the eleven `src/AuthN.Services/` files listed in File Structure
- Modify: `Heimdall/src/AuthN.Services/AuthN.Services.csproj` (`<Description>`)
- Test: `Heimdall/tests/AuthN.Services.Tests/RequestContractTests.cs`

**Interfaces:**
- Produces `Norse.AuthN.Services.IAccountService`, `[ServiceContract(Name = "grpc.account.v1.AccountService")]`, every member `[OperationContract]` with a trailing `CancellationToken cancellationToken = default` and no `Async` suffix (the `IAuthenticationService` shape):
  - `Task<Outcome<AccountStatusResponse>> GetAccountStatus(CancellationToken cancellationToken = default)`
  - `Task<Outcome<NavigationResult>> SetPhoneNumber(SetPhoneNumberRequest request, …)`
  - `Task<Outcome<NavigationResult>> ChangePassword(ChangePasswordRequest request, …)`
  - `Task<Outcome<NavigationResult>> SetPassword(SetPasswordRequest request, …)`
  - `Task<Outcome<BoolResponse>> ChangeEmail(ChangeEmailRequest request, …)`
  - `Task<Outcome<ExternalLoginsResponse>> GetExternalLogins(CancellationToken cancellationToken = default)`
  - `Task<Outcome<NavigationResult>> RemoveExternalLogin(RemoveExternalLoginRequest request, …)`
  - `Task<Outcome<NavigationResult>> DeletePersonalData(DeletePersonalDataRequest request, …)`
- Produces the records (`[DataContract] public sealed record`; `[DataMember(Order = n)]` in the order listed; scalars `{ get; set; } = string.Empty` on requests, `required … { get; init; }` on responses):
  - `AccountStatusResponse { bool HasPassword, bool EmailConfirmed, bool TwoFactorEnabled, bool HasAuthenticator, int RecoveryCodesLeft, bool IsMachineRemembered, int PasskeyCount, int ExternalLoginCount }` (orders 1–8).
  - `SetPhoneNumberRequest { Result<PhoneNumber> Phone (1) }` + un-serialized `required string PhoneInput` whose setter does `field = value; Phone = PhoneNumber.Parse(value);`, initialized `= string.Empty`. An empty buffer parses to `Failure(ParseFailure.Empty)`, which is the "clear" signal (§4.3).
  - `ChangePasswordRequest { string OldPassword (1), string NewPassword (2) }`.
  - `SetPasswordRequest { string NewPassword (1) }`.
  - `ChangeEmailRequest { Result<EmailAddress> NewEmail (1) }` + `required string NewEmailInput` (the `LoginRequest` setter shape; the buffer-suffix echo maps `NewEmailInput` → `NewEmail`).
  - `ExternalLoginsResponse { ExternalLoginEntry[] Logins (1) = [], ExternalProviderEntry[] AvailableProviders (2) = [], required bool CanRemove (3) }`.
  - `ExternalLoginEntry { required string LoginProvider (1), required string ProviderKey (2), required string DisplayName (3) }`.
  - `ExternalProviderEntry { required string Name (1), required string DisplayName (2) }`.
  - `RemoveExternalLoginRequest { string LoginProvider (1), string ProviderKey (2) }`.
  - `DeletePersonalDataRequest { string Password (1) }`.

- [ ] **Step 1: Create the Heimdall fork** `feature/account-lifecycle-account-core` from `master`.
- [ ] **Step 2: Write the failing tests** in `RequestContractTests`:
  - Extend `NewRecords` with the ten new types and `Wire_records_carry_no_Authorize_attribute`'s array likewise; `contracts` becomes `[typeof(IAuthenticationService), typeof(IIdentityService), typeof(IAccountService)]` so the trailing-token and `[OperationContract]` facts cover it.
  - `Every_wire_record_is_a_data_contract_with_ordered_members`: the buffer exclusion changes from `Name == "EmailInput"` to `Name.EndsWith("Input", StringComparison.Ordinal)` so `PhoneInput` and `NewEmailInput` are exempt and everything else needs `[DataMember]`.
  - Replace `Operation(Type request)` with `Operation(Type contract, Type request)`; existing callers pass `typeof(IAuthenticationService)`.
  - New facts:

```csharp
[Fact]
void The_account_contract_is_declared_with_the_ruled_name_and_return_types()
{
	typeof(IAccountService).GetCustomAttribute<ServiceContractAttribute>()!.Name.ShouldBe("grpc.account.v1.AccountService");
	Type[] navigation = [typeof(SetPhoneNumberRequest), typeof(ChangePasswordRequest), typeof(SetPasswordRequest), typeof(RemoveExternalLoginRequest), typeof(DeletePersonalDataRequest)];
	foreach (var request in navigation)
		Operation(typeof(IAccountService), request).ReturnType.ShouldBe(typeof(Task<Outcome<NavigationResult>>));
	Operation(typeof(IAccountService), typeof(ChangeEmailRequest)).ReturnType.ShouldBe(typeof(Task<Outcome<BoolResponse>>));
	typeof(IAccountService).GetMethod(nameof(IAccountService.GetAccountStatus))!.ReturnType.ShouldBe(typeof(Task<Outcome<AccountStatusResponse>>));
	typeof(IAccountService).GetMethod(nameof(IAccountService.GetExternalLogins))!.ReturnType.ShouldBe(typeof(Task<Outcome<ExternalLoginsResponse>>));
}

[Fact]
void The_request_less_account_operations_take_only_a_cancellation_token()
{
	foreach (var name in new[] { nameof(IAccountService.GetAccountStatus), nameof(IAccountService.GetExternalLogins) })
		typeof(IAccountService).GetMethod(name)!.GetParameters().ShouldHaveSingleItem().ParameterType.ShouldBe(typeof(CancellationToken));
}

[Fact]
void Phone_and_new_email_buffers_are_never_serialized()
{
	typeof(SetPhoneNumberRequest).GetProperty(nameof(SetPhoneNumberRequest.PhoneInput))!.GetCustomAttribute<DataMemberAttribute>().ShouldBeNull();
	typeof(ChangeEmailRequest).GetProperty(nameof(ChangeEmailRequest.NewEmailInput))!.GetCustomAttribute<DataMemberAttribute>().ShouldBeNull();
}

[Fact]
void The_phone_buffer_restamps_on_every_assignment()
{
	SetPhoneNumberRequest request = new() { PhoneInput = "+1 (555) 123-4567" };
	request.Phone.TryGetValue(out Success<PhoneNumber> parsed).ShouldBeTrue();
	parsed.Value.WireValue.ShouldBe("+15551234567");
	request.PhoneInput = "";
	request.Phone.TryGetValue(out Failure empty).ShouldBeTrue();
	empty.Reason.ShouldBe(ParseFailure.Empty);
}

[Fact]
void Response_collections_default_to_empty_not_null()
{
	ExternalLoginsResponse response = new() { CanRemove = false };
	response.Logins.ShouldBeEmpty();
	response.AvailableProviders.ShouldBeEmpty();
}
```

- [ ] **Step 3: Run** `dotnet test Heimdall/tests/AuthN.Services.Tests` — build errors for the missing types.
- [ ] **Step 4: Implement** the contract and records. XML docs name the scaffold page each operation replaces (`Manage/Index.razor`, `Manage/Email.razor`, …), cite §4.3, and for `RemoveExternalLogin` the §5.9 refusal; `ExternalLoginsResponse.CanRemove` doc says "presentation, not the guard". Update the csproj `<Description>` (add `IAccountService` and the ten records to the list).
- [ ] **Step 5: Run** — PASS.
- [ ] **Step 6: Stage** and stop.

### Task 1.2: Six validators

**Files:**
- Create: `Heimdall/src/AuthN.Components/SetPhoneNumberRequestValidator.cs`, `ChangeEmailRequestValidator.cs`, `ChangePasswordRequestValidator.cs`, `SetPasswordRequestValidator.cs`, `RemoveExternalLoginRequestValidator.cs`, `DeletePersonalDataRequestValidator.cs`
- Test: `Heimdall/tests/AuthN.Components.Tests/{Record}ValidatorTests.cs` (six)

**Interfaces:**
- Produces `public sealed class {Record}Validator : AbstractValidator<{Record}>`, parameterless, namespace `Norse.AuthN.Components`, each with the "one declaration, two consumers" doc (the `LoginRequestValidator` wording). Rules (§6.7):
  - `SetPhoneNumber`: `RuleFor(x => x.Phone).Must(phone => phone.TryGetValue(out Success<PhoneNumber> _) || (phone.TryGetValue(out Failure failure) && failure.Reason == ParseFailure.Empty)).WithMessage("Enter a valid phone number (+15551234567).").WithName(nameof(SetPhoneNumberRequest.PhoneInput))` — empty allowed, malformed rejected.
  - `ChangeEmail`: the `ResetPasswordRequestValidator` email chain on `NewEmail` with `WithName(nameof(ChangeEmailRequest.NewEmailInput))`; messages "Enter your new email address." / "Enter a valid email address (local@domain.tld).".
  - `ChangePassword`: `OldPassword` `NotEmpty()`; `NewPassword` `NotEmpty().MinimumLength(8).NotEqual(x => x.OldPassword).WithMessage("Your new password must differ from your current password.")`.
  - `SetPassword`: `NewPassword` `NotEmpty().MinimumLength(8)`.
  - `RemoveExternalLogin`: `LoginProvider` `NotEmpty()`; `ProviderKey` `NotEmpty()`.
  - `DeletePersonalData`: `Password` `MaximumLength(256)` only (requiredness is the server's, §6.7).

- [ ] **Step 1: Write the failing tests**, one file per validator in the `LoginRequestValidatorTests` shape (`readonly {Record}Validator _validator = new();`). Minimum: every rule rejected once with its message asserted; a well-formed request accepted. Pinned facts: `SetPhoneNumberRequestValidatorTests.An_empty_phone_is_accepted` (`PhoneInput = ""` → valid), `A_phone_without_the_plus_prefix_is_rejected` (`"5551234567"` → the format message keyed on `Phone`, displayed as `PhoneInput`); `ChangePasswordRequestValidatorTests.A_new_password_equal_to_the_old_one_is_rejected`; `DeletePersonalDataRequestValidatorTests.An_empty_password_is_accepted` and `A_257_character_password_is_rejected`.
- [ ] **Step 2: Run** `dotnet test Heimdall/tests/AuthN.Components.Tests` — build errors.
- [ ] **Step 3: Implement** the six validators.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Stage** and stop.

### Task 1.3: `ManageStatus`, `ManageLayout`, `ManageNavMenu`, `Manage/_Imports`, the `PersonalData` move, the reflection lock

**Files:**
- Create: `Heimdall/src/AuthN.Components/Shared/ManageStatus.cs`; `Heimdall/src/AuthN.Components.FluentUI/ManageLayout.razor`, `ManageNavMenu.razor` (project root, beside `GateLayout.razor` — never inside `Manage/`, because the folder `_Imports` would stamp `@layout ManageLayout` onto the layout itself, which Blazor documents as an infinite layout loop; Himinbjörg's own copy sits in `Components/Shared/` for the same reason); `Heimdall/src/AuthN.Components.FluentUI/Manage/_Imports.razor`
- Move: `Heimdall/src/AuthN.Components.FluentUI/PersonalData.razor` → `Manage/PersonalData.razor`; `tests/AuthN.Components.FluentUI.Tests/PersonalDataTests.cs` → `Manage/PersonalDataTests.cs`
- Modify: `Heimdall/src/AuthN.Components.FluentUI/_Imports.razor`
- Test: `Heimdall/tests/AuthN.Components.Tests/Shared/ManageStatusTests.cs`; `Heimdall/tests/AuthN.Components.FluentUI.Tests/Manage/TestShellLayout.razor`, `TestAppShell.cs`, `ManagePolicyLockTests.cs`, `ManageLayoutTests.cs`, `ManageNavMenuTests.cs`

**Interfaces:**
- Produces `public static class ManageStatus` (namespace `Norse.AuthN.Components.Shared`) with `public static string? Text(string? key)`: `password-changed` → "Your password has been changed."; `password-set` → "Your password has been set."; `phone-updated` → "Your profile has been updated."; `email-changed` → "Your email has been changed."; `external-login-removed` → "The external login was removed."; `external-login-added` → "The external login was added."; `external-login-not-added` → "Error: The external login was not added. External logins can only be associated with one account."; `authenticator-reset` → "Your authenticator app key has been reset, you will need to configure your authenticator app using the new key."; null, empty or unknown → `null`. Keys are `const string` members (`PasswordChanged`, …) so handlers and tests name them; a `switch` expression, ordinal.
- Produces `Manage/_Imports.razor`:

```razor
@using Microsoft.AspNetCore.Authorization
@using Norse.Abstractions.Components.Primitives
@layout ManageLayout
@attribute [Authorize(Policy = AuthNPolicies.Account)]
```

- Produces `ManageLayout.razor` (namespace `Norse.AuthN.Components.FluentUI`, resolvable from the nested `Manage` namespace without a `@using`): `@inherits LayoutComponentBase`, `@inject IAppShellLayout AppShell`, `[SupplyParameterFromQuery(Name = "status")] public string? Status { get; set; }`; renders `<LayoutView Layout="@AppShell.LayoutType">` → `<h1>Manage your account</h1>`, `<h2>Change your account settings</h2>`, `<StatusMessage Message="@ManageStatus.Text(Status)"/>`, then the Himinbjörg `FluentGrid` (`ManageNavMenu` at `sm="3"`, `@Body` at `sm="6"`).
- Produces `ManageNavMenu.razor`: static markup, `FluentNavMenu` with `FluentNavLink`s for `Account/Manage` (Profile, `Match="NavLinkMatch.All"`), `Account/Manage/Email`, `Account/Manage/ChangePassword`, `Account/Manage/ExternalLogins`, `Account/Manage/TwoFactorAuthentication`, `Account/Manage/Passkeys`, `Account/Manage/PersonalData`. No injection (§6.4: the external-logins link is always shown; the page itself says when nothing is linked).
- `PersonalData.razor` keeps its route, markup and code; its namespace becomes `Norse.AuthN.Components.FluentUI.Manage` by folder, so it inherits the layout and the policy.
- `AuthN.Components.FluentUI/_Imports.razor` gains `@using Norse.AuthN.Components.FluentUI.Manage`.
- Test helpers: `TestShellLayout.razor` (`@inherits LayoutComponentBase` rendering `<div data-test-shell>@Body</div>`) and `sealed class TestAppShell : IAppShellLayout { public Type LayoutType => typeof(TestShellLayout); }`.

- [ ] **Step 1: Write the failing tests.**
  - `ManageStatusTests`: `[Theory]` over every key → its text; `An_unknown_key_maps_to_null` (`"nope"`, `""`, `null`).
  - `ManagePolicyLockTests`:

```csharp
[Fact]
void Every_manage_route_carries_the_account_policy_and_the_manage_layout()
{
	var pages = typeof(ManageLayout).Assembly.GetTypes()
		.Where(t => t.GetCustomAttributes<RouteAttribute>().Any(r => r.Template.StartsWith("/Account/Manage", StringComparison.OrdinalIgnoreCase)))
		.ToList();
	pages.Count.ShouldBeGreaterThanOrEqualTo(7);
	foreach (var page in pages)
	{
		page.GetCustomAttributes<AuthorizeAttribute>().ShouldContain(a => a.Policy == AuthNPolicies.Account, page.FullName);
		page.GetCustomAttribute<LayoutAttribute>()!.LayoutType.ShouldBe(typeof(ManageLayout), page.FullName);
	}
}
```

  - `ManageLayoutTests` (`BunitContext`; `Services.AddFluentUIComponents(); JSInterop.Mode = JSRuntimeMode.Loose; Services.AddSingleton<IAppShellLayout, TestAppShell>();`): `Renders_the_app_shell_around_the_body` (`Render<ManageLayout>(p => p.Add(l => l.Body, b => b.AddMarkupContent(0, "<p>child</p>")))` → markup contains `data-test-shell` and `child` and "Manage your account"); `Renders_the_status_text_for_a_known_key` (`Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/Account/Manage?status=password-changed")` before render → markup contains "Your password has been changed."); `Renders_no_status_for_an_unknown_key`; and the router-chain fact that a self-stamped layout would fail by looping:

```csharp
[Fact]
void A_manage_page_renders_through_the_router_layout_chain_exactly_once()
{
	RouteData routeData = new(typeof(PersonalData), new Dictionary<string, object?>());
	var cut = Render<RouteView>(p => p.Add(r => r.RouteData, routeData));
	cut.FindAll("[data-test-shell]").Count.ShouldBe(1);
	cut.FindAll("h1").Count(h => h.TextContent == "Manage your account").ShouldBe(1);
	cut.Markup.ShouldContain("Personal Data");
}
```

  (`RouteView` reads the page's `[Layout]` attribute and nests through `LayoutView`, so it exercises the same resolution the Yggdrasil router does; a layout stamped with itself never returns from this render.)
  - `ManageNavMenuTests`: `Links_every_manage_page` asserts the seven `href`s.
  - `Manage/PersonalDataTests`: the moved file; the two existing facts unchanged apart from namespace.
- [ ] **Step 2: Run** `dotnet test Heimdall/tests/AuthN.Components.FluentUI.Tests` and `…/AuthN.Components.Tests` — build errors; the policy lock fails on the moved `PersonalData` until `_Imports` exists.
- [ ] **Step 3: Implement** as specified in Interfaces; `git mv` the two PersonalData files.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Stage** and stop.

### Task 1.4: Profile — `Index` + `ProfileForm`

**Files:**
- Create: `Heimdall/src/AuthN.Components.FluentUI/Manage/Index.razor`, `ProfileForm.razor`
- Test: `Heimdall/tests/AuthN.Components.FluentUI.Tests/Manage/IndexTests.cs`, `ProfileFormTests.cs`

**Interfaces:**
- Consumes `IIdentityService.GetMyPersonalDataAsync` (email, phone), `IAccountService.SetPhoneNumber`, `SetPhoneNumberRequestValidator`, `ISessionTransition`.
- Produces `Index.razor`: `@page "/Account/Manage"`, `@inherits OutcomeComponentBase`, `@inject IIdentityService IdentityService`; `OnInitializedAsync` → `DispatchAsync(ct => IdentityService.GetMyPersonalDataAsync(new(), ct), data => _data = data)`; renders `<Loader/>` while dispatching, the `Problem` in place (`<div class="alert alert-danger" role="alert">` with "Your profile could not be loaded."), else `<ProfileForm Email="@_data.Email" PhoneNumber="@_data.PhoneNumber"/>`.
- Produces `ProfileForm.razor`: `@inherits OutcomeFormComponentBase`, `@inject IAccountService AccountService`, `@inject ISessionTransition SessionTransition`; `[Parameter, EditorRequired] string Email`, `[Parameter] string PhoneNumber = ""`; `OnParametersSet` copies `PhoneNumber` into `_request.PhoneInput`; `EditForm` `FormName="authn-profile"` with `<FormValidator/>`, `<ModelValidationSummary/>`, a disabled `FluentTextInput` showing `Email` (label "Email"), a `FluentTextInput` bound to `_request.PhoneInput` with `ValidationFieldFor="@((Expression<Func<Result<PhoneNumber>>>)(() => _request.Phone))"` (label "Phone number", placeholder `+15551234567`), submit "Save"; `SubmitAsync(editContext, ct => AccountService.SetPhoneNumber(_request, ct), result => SessionTransition.Begin(result))`.

- [ ] **Step 1: Write the failing tests** (`BunitContext`; `AddFluentUIComponents`, loose JS, `Services.AddSingleton(_identity)` / `Services.AddSingleton(_account)` substitutes, `Services.AddScoped<IValidator<SetPhoneNumberRequest>, SetPhoneNumberRequestValidator>()`, `Services.AddSingleton(_sessionTransition)` substitute):
  - `IndexTests`: `Loads_the_profile_on_initialization_and_renders_the_form` (identity returns `Ok(new PersonalDataResponse { Email = "user@example.com", PhoneNumber = "+15551234567" })` → markup contains `authn-profile` and the phone value); `A_failed_load_renders_the_problem_and_no_form` (`Err(Fault)` → "could not be loaded", no `authn-profile`).
  - `ProfileFormTests`: `A_malformed_phone_never_reaches_the_service` (change input to `"5551234567"`, submit, `DidNotReceiveWithAnyArgs().SetPhoneNumber`); `An_empty_phone_is_sent_as_a_clear` (change to `""`, submit → `Received(1).SetPhoneNumber(Arg.Is<SetPhoneNumberRequest>(r => r.PhoneInput == ""), …)`); `Success_begins_one_transition_and_no_history` (account returns `Ok(NextUrl = "/Account/Manage?status=phone-updated")` → `_sessionTransition.Received(1).Begin(Arg.Is<NavigationResult>(r => r.NextUrl == "/Account/Manage?status=phone-updated"))`, `Navigation.History.ShouldBeEmpty()`); `A_failed_save_renders_in_the_summary_without_a_transition` (`Err(Fault)` → `DidNotReceive().Begin`).
- [ ] **Step 2: Run** — build errors.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Stage** and stop.

### Task 1.5: Email — `Email` + `ChangeEmailForm`

**Files:**
- Create: `Heimdall/src/AuthN.Components.FluentUI/Manage/Email.razor`, `ChangeEmailForm.razor`
- Test: `Heimdall/tests/AuthN.Components.FluentUI.Tests/Manage/EmailTests.cs`, `ChangeEmailFormTests.cs`

**Interfaces:**
- Consumes `IAccountService.GetAccountStatus` (`EmailConfirmed`), `IIdentityService.GetMyPersonalDataAsync` (`Email`), `IAccountService.ChangeEmail`, `IAuthenticationService.ResendEmailConfirmation`, `ChangeEmailRequestValidator`.
- Produces `Email.razor`: `@page "/Account/Manage/Email"`, `@inherits OutcomeComponentBase`; `OnInitializedAsync` dispatches the two reads in sequence (status first; a `Failed` on either renders the problem, "Your email settings could not be loaded."); renders `<ChangeEmailForm Email="@_email" EmailConfirmed="@_status.EmailConfirmed"/>`.
- Produces `ChangeEmailForm.razor`: `@inherits OutcomeFormComponentBase`, injects `IAccountService` and `IAuthenticationService`; `[Parameter, EditorRequired] string Email`, `[Parameter] bool EmailConfirmed`; markup: a disabled `FluentTextInput` showing `Email` with a "✓" suffix when confirmed; when not confirmed a `FluentButton` "Send verification email" (`OnClick="HandleSendVerificationAsync"`, disabled while `_sending`) that calls `AuthenticationService.ResendEmailConfirmation(new() { EmailInput = Email }, CancellationToken)` and sets `_message = "Verification email sent. Please check your email."` on `Success` (a `Failed` sets `_message = "Error: The verification email could not be sent."`); then the `EditForm` `FormName="authn-change-email"` with `<FormValidator/>`, `<ModelValidationSummary/>`, a `FluentTextInput` bound to `_request.NewEmailInput` with `ValidationFieldFor` on `_request.NewEmail` (label "New email"), submit "Change email"; `SubmitAsync(editContext, ct => AccountService.ChangeEmail(_request, ct), _ => _message = "Confirmation link to change email sent. Please check your email.")`; `<StatusMessage Message="@_message"/>` above the form. No transition, no navigation (§6.3 in-place row).

- [ ] **Step 1: Write the failing tests.**
  - `EmailTests`: `Loads_status_then_profile_and_renders_the_form`; `A_failed_status_load_renders_the_problem_and_no_form`.
  - `ChangeEmailFormTests`: `An_empty_submit_never_reaches_the_service`; `A_malformed_email_renders_the_format_message`; `Success_renders_the_sent_message_in_place` (`Ok(new BoolResponse { Value = true })` → markup contains "Confirmation link to change email sent."; `Navigation.History.ShouldBeEmpty()`); `An_unchanged_email_lands_on_the_new_email_field` (`Err(Validation, { ["NewEmail"] = ["Your email is unchanged."] })` → rendered, service received once); `The_verification_button_renders_only_when_unconfirmed` (`EmailConfirmed = true` → no "Send verification email"); `The_verification_button_calls_resend_with_the_current_email` (`_authentication.Received(1).ResendEmailConfirmation(Arg.Is<ResendEmailConfirmationRequest>(r => r.EmailInput == "user@example.com"), …)` and the sent text renders).
- [ ] **Step 2: Run** — build errors.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Stage** and stop.

### Task 1.6: Passwords — `ChangePassword`/`ChangePasswordForm`, `SetPassword`/`SetPasswordForm`

**Files:**
- Create: `Heimdall/src/AuthN.Components.FluentUI/Manage/ChangePassword.razor`, `ChangePasswordForm.razor`, `SetPassword.razor`, `SetPasswordForm.razor`
- Test: `Heimdall/tests/AuthN.Components.FluentUI.Tests/Manage/ChangePasswordTests.cs`, `ChangePasswordFormTests.cs`, `SetPasswordTests.cs`, `SetPasswordFormTests.cs`

**Interfaces:**
- Consumes `IAccountService.GetAccountStatus` (`HasPassword`), `ChangePassword`, `SetPassword`, the two validators, `ISessionTransition`.
- Produces `ChangePassword.razor` (`/Account/Manage/ChangePassword`) and `SetPassword.razor` (`/Account/Manage/SetPassword`): both `OutcomeComponentBase` loaders over `GetAccountStatus`; each renders `<ChangePasswordForm/>` when `HasPassword` and `<SetPasswordForm/>` otherwise (§6.2: the child is chosen from the status; both scaffold routes survive and never redirect). `SetPasswordForm` is preceded by `<p>You do not have a local username/password for this site. Add a local account so you can log in without an external login.</p>` on either page.
- Produces `ChangePasswordForm.razor`: `FormName="authn-change-password"`, two `FluentTextInput`s with `TextInputType.Password` bound to `_request.OldPassword` (label "Current password", `AutoComplete="current-password"`) and `_request.NewPassword` (label "New password", `AutoComplete="new-password"`), submit "Update password"; `SubmitAsync(…, ct => AccountService.ChangePassword(_request, ct), result => SessionTransition.Begin(result))`.
- Produces `SetPasswordForm.razor`: `FormName="authn-set-password"`, one password input bound to `_request.NewPassword`, submit "Set password"; `SubmitAsync(…, ct => AccountService.SetPassword(_request, ct), result => SessionTransition.Begin(result))`.

- [ ] **Step 1: Write the failing tests.**
  - `ChangePasswordTests` / `SetPasswordTests`: `Renders_the_change_form_when_a_password_exists` (status `HasPassword = true` → `authn-change-password`), `Renders_the_set_form_when_no_password_exists` (→ `authn-set-password` and the "no local username/password" copy), `A_failed_status_load_renders_the_problem`.
  - `ChangePasswordFormTests`: `An_empty_submit_never_reaches_the_service`; `A_new_password_equal_to_the_old_one_never_reaches_the_service`; `Success_begins_one_transition_and_no_history` (`Ok(NextUrl = "/Account/Manage/ChangePassword?status=password-changed")`); `A_wrong_current_password_lands_on_the_old_password_field` (`Err(InvalidCredentials, { ["OldPassword"] = ["Incorrect password."] })` → renders, no transition); `A_password_rule_failure_lands_on_the_new_password_field` (`Err(Validation, { ["NewPassword"] = ["Passwords must have at least one digit ('0'-'9')."] })`).
  - `SetPasswordFormTests`: `An_empty_submit_never_reaches_the_service`; `Success_begins_one_transition_and_no_history` (`…?status=password-set`); `A_conflict_renders_in_the_summary_without_a_transition` (`new Failed(Problem.ModelError(ErrorCategory.Conflict, "User already has a password set."))` → text renders, `DidNotReceive().Begin`); `A_password_rule_failure_lands_on_the_new_password_field`.
- [ ] **Step 2: Run** — build errors.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Stage** and stop.

### Task 1.7: `ExternalLogins`

**Files:**
- Create: `Heimdall/src/AuthN.Components.FluentUI/Manage/ExternalLogins.razor`
- Test: `Heimdall/tests/AuthN.Components.FluentUI.Tests/Manage/ExternalLoginsTests.cs`

**Interfaces:**
- Consumes `IAccountService.GetExternalLogins`, `RemoveExternalLogin`, `ISessionTransition`.
- Produces `ExternalLogins.razor` (`/Account/Manage/ExternalLogins`, `@inherits OutcomeComponentBase`): loads `GetExternalLogins` in `OnInitializedAsync`; renders `Problem` in place; "Registered logins" table — one row per `Logins` entry with `DisplayName` and, when `CanRemove`, a `FluentButton` "Remove" (`data-provider="@login.LoginProvider"`) whose click dispatches `RemoveExternalLogin(new() { LoginProvider = …, ProviderKey = … })` → `SessionTransition.Begin(result)`; a `Failed` on remove renders in the same in-place alert (the `Conflict` text from the server, e.g. "This is the only way to sign in to your account."); "Add another service to log in" — one `FluentAnchor` per `AvailableProviders` entry with `Href="Account/Manage/LinkExternalLogin?provider=@Uri.EscapeDataString(provider.Name)"` (§5.7: a plain GET link, no form, no antiforgery); when both lists are empty, `<p>No external logins are linked and none are available.</p>`.

- [ ] **Step 1: Write the failing tests** (`LogoutTests` shape for the click page): `Lists_registered_logins_and_available_providers` (one of each → `DisplayName`s render, the link `href` ends with `LinkExternalLogin?provider=Microsoft`); `Hides_the_remove_button_when_the_server_says_it_cannot_be_removed` (`CanRemove = false` → no "Remove"); `Removing_a_login_begins_one_transition` (click → `Received(1).RemoveExternalLogin(Arg.Is<RemoveExternalLoginRequest>(r => r.LoginProvider == "Google" && r.ProviderKey == "g-1"), …)`, `Begin` once, history empty); `A_refused_removal_renders_the_conflict_in_place` (`new Failed(Problem.ModelError(ErrorCategory.Conflict, "This is the only way to sign in to your account."))` → text renders, no transition); `A_bare_render_removes_nothing`.
- [ ] **Step 2: Run** — build errors.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Stage** and stop.

### Task 1.8: `DeletePersonalData` + `DeletePersonalDataForm`; docs; ship gate

**Files:**
- Create: `Heimdall/src/AuthN.Components.FluentUI/Manage/DeletePersonalData.razor`, `DeletePersonalDataForm.razor`
- Modify: `Heimdall/src/AuthN.Components.FluentUI/AuthN.Components.FluentUI.csproj`, `Heimdall/README.md`, `Heimdall/CLAUDE.md`
- Test: `Heimdall/tests/AuthN.Components.FluentUI.Tests/Manage/DeletePersonalDataTests.cs`, `DeletePersonalDataFormTests.cs`

**Interfaces:**
- Consumes `IAccountService.GetAccountStatus` (`HasPassword`), `DeletePersonalData`, `DeletePersonalDataRequestValidator`, `ISessionTransition`.
- Produces `DeletePersonalData.razor` (`/Account/Manage/DeletePersonalData`): loader over `GetAccountStatus` → `<DeletePersonalDataForm HasPassword="@_status.HasPassword"/>`.
- Produces `DeletePersonalDataForm.razor`: `[Parameter] bool HasPassword`; the scaffold's warning copy ("Deleting this data will permanently remove your account, and this cannot be recovered."); `FormName="authn-delete-personal-data"`; the password input (`AutoComplete="current-password"`, label "Password") rendered only when `HasPassword` (§5.6: the field is always on the wire, the client renders it only when a password exists); submit "Delete data and close my account" with `Appearance="ButtonAppearance.Primary"`; `SubmitAsync(…, ct => AccountService.DeletePersonalData(_request, ct), result => SessionTransition.Begin(result))`.

- [ ] **Step 1: Write the failing tests.**
  - `DeletePersonalDataTests`: `Renders_the_form_with_a_password_field_when_a_password_exists`; `Renders_the_form_without_a_password_field_otherwise`.
  - `DeletePersonalDataFormTests`: `A_passwordless_submit_reaches_the_service_with_an_empty_password` (`HasPassword = false`, submit → `Received(1).DeletePersonalData(Arg.Is<DeletePersonalDataRequest>(r => r.Password == ""), …)`); `Success_begins_one_transition_and_no_history` (`Ok(NextUrl = "/")`); `A_wrong_password_renders_in_the_summary_without_a_transition` (`new Failed(Problem.ModelError(ErrorCategory.InvalidCredentials, "Incorrect password."))`); `A_shred_failure_renders_the_administrator_wording` (`new Failed(Problem.ModelError(ErrorCategory.Fault, "Your account could not be fully deleted. The failure has been logged for an administrator to finish."))` → renders, no transition).
- [ ] **Step 2: Run** — build errors.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS. Run `dotnet test Heimdall.slnx` — green.
- [ ] **Step 5: Docs.** `Heimdall/README.md` rows 17–19 and `Heimdall/CLAUDE.md` rows 15–17: `AuthN.Services` lists `IAccountService` and its records; `AuthN.Components` lists the six validators and `ManageStatus`; `AuthN.Components.FluentUI` lists the `Manage/` tree (layout, nav, the loader-over-form pages, the moved `PersonalData`) and records the `_Imports` policy/layout stamp and its reflection lock. `AuthN.Components.FluentUI.csproj` `<Description>` likewise. Add a CLAUDE.md architecture fact: "`Manage/_Imports.razor` stamps every page with `@layout ManageLayout` and `[Authorize(Policy = AuthNPolicies.Account)]`; `ManagePolicyLockTests` enforces it; `ManageLayout` renders `?status=` through `ManageStatus` — the server is the only author of URLs."
- [ ] **Step 6: Stage** and stop. Ship gate: Heimdall PR, tag, publish (`HeimdallVersion` → next).

---

## Phase 2 — Himinbjörg

### Task 2.1: `UserManagerOutcomeView` write views, `LastSignInMethod`, the map row

**Files:**
- Modify: `Himinbjorg/src/Identity.Outcomes/NorseIdentityErrorCodes.cs`, `IdentityOutcomeMap.cs`, `UserManagerOutcomeView.cs`
- Test: `Himinbjorg/tests/Identity.Outcomes.Tests/IdentityOutcomeMapTests.cs`, `UserManagerOutcomeViewTests.cs`

**Interfaces:**
- Produces `NorseIdentityErrorCodes.LastSignInMethod = "LastSignInMethod"` (doc: §5.9, "a user has a password or at least one external login"); `IdentityOutcomeMap._codes[LastSignInMethod] = ErrorCategory.Conflict`.
- Produces on `UserManagerOutcomeView`, each `ThrowIfCancellationRequested` + `GuardAsync` + `ToOutcome(…, Unit.Value)` like `ChangeEmailAsync`:
  - `ValueTask<Outcome<Unit>> SetPhoneNumberAsync(NorseUser user, string? phoneNumber, CancellationToken cancellationToken = default)`
  - `ValueTask<Outcome<Unit>> ChangePasswordAsync(NorseUser user, string currentPassword, string newPassword, CancellationToken cancellationToken = default)`
  - `ValueTask<Outcome<Unit>> AddPasswordAsync(NorseUser user, string password, CancellationToken cancellationToken = default)`
  - `ValueTask<Outcome<Unit>> AddLoginAsync(NorseUser user, UserLoginInfo login, CancellationToken cancellationToken = default)`
  - `ValueTask<Outcome<Unit>> RemoveLoginAsync(NorseUser user, string loginProvider, string providerKey, CancellationToken cancellationToken = default)`

- [ ] **Step 1: Write the failing tests.** `IdentityOutcomeMapTests`: `Last_sign_in_method_maps_to_Conflict` (`TryCategorize(NorseIdentityErrorCodes.LastSignInMethod, out var c)` true, `Conflict`); the existing every-constant enumeration fact goes red until the row exists. `UserManagerOutcomeViewTests`, one fact per method in the `ChangeEmailAsync` shape (Success → `Success<Unit>`; a describer failure → its category: `PasswordMismatch` → `InvalidCredentials` for `ChangePasswordAsync`, `UserAlreadyHasPassword` → `Conflict` for `AddPasswordAsync`, `LoginAlreadyAssociated` → `Conflict` for `AddLoginAsync`, `LastSignInMethod` → `Conflict` for `RemoveLoginAsync`, `ConcurrencyFailure` → `Conflict` for `SetPhoneNumberAsync`; `IndeterminateOutcome` → `Indeterminate`; a `KeyDestroyedException` throw → `Erased` with the receipt).
- [ ] **Step 2: Run** `dotnet test Himinbjorg/tests/Identity.Outcomes.Tests` — build errors / the enumeration fact red.
- [ ] **Step 3: Implement** the constant, the row, the five methods.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Create the Himinbjörg fork** `feature/account-lifecycle-account-core` from `master` (if not already done at phase start). **Stage** and stop.

### Task 2.2: The last sign-in method guard in the store (§5.9)

**Files:**
- Modify: `Himinbjorg/src/Identity.Web.Server/Store/FlushExecutor.cs:148-150`, `Store/ProviderStatements.cs` (`Dialect`, new method), `NorseUserStore.cs` (constant alias)
- Create: `Himinbjorg/tests/Identity.Web.Server.Tests/LastSignInMethodSuite.cs`, `PostgresLastSignInMethodTests.cs`, `SqlServerLastSignInMethodTests.cs`

**Interfaces:**
- Produces `ProviderStatements.HasPasswordForUpdateAsync(NorseIdentityDbContext context, Guid userId, CancellationToken cancellationToken = default) → Task<bool>`: one `SELECT` of `CASE WHEN password_hash IS NULL THEN 0 ELSE 1 END AS "Value"` from the credentials table for `id = @p0`, locked — PostgreSQL appends `FOR UPDATE`; SQL Server puts `WITH (UPDLOCK, HOLDLOCK)` after the table name. Two new `Dialect` fields: `LockHint` (SQL Server `" WITH (UPDLOCK, HOLDLOCK)"`, PostgreSQL `""`) and `LockSuffix` (PostgreSQL `" FOR UPDATE"`, SQL Server `""`); `SqlQueryRaw<int>` + `ToListAsync`, `[var v] => v is 1`, no row → `InvalidOperationException` (the user has no credentials row).
- Produces `FlushExecutor.RemoveLoginAsync(Guid userId, StagedIntent.RemoveLogin intent, CancellationToken cancellationToken) → Task<IdentityError?>` replacing the inline arm: lock-and-read the password hash, `context.UserLogins.CountAsync(l => l.UserId == userId)`, and when `!hasPassword && count <= 1` return `new() { Code = NorseUserStore.LastSignInMethodErrorCode, Description = "This is the only way to sign in to your account." }` (the arm's caller rolls back); otherwise the existing `ExecuteDeleteAsync` and `null`. Doc: the stamp CAS on `user_credentials` (step 2 of the flush) already serializes two stale instances — the second's credentials statement blocks on the row and then matches zero rows (`ConcurrencyFailure`); the lock here is what makes a *fresh* instance's count authoritative, and the two together keep the invariant.
- `NorseUserStore.LastSignInMethodErrorCode = NorseIdentityErrorCodes.LastSignInMethod` beside `IndeterminateOutcomeErrorCode`.

- [ ] **Step 1: Write the failing suite** — `public abstract class LastSignInMethodSuite(IdentityFixture fixture)` in the `ScaffoldWritePathSuite` shape (`SkipUnlessAvailable()`, `[Fact(SkipUnless = nameof(IsTestable), Skip = UntestableReason)]`), helpers `SeedAsync(bool withPassword)`, `LinkAsync(userId, provider)`, `FreshAsync(userId)` (`_fixture.CreateUserManager()` + `GetUserAsync` on a principal, the `PageUserAsync` shape), and the invariant query `OrphansAsync()` = `context.Users.CountAsync(u => u.PasswordHash == null && !context.UserLogins.Any(l => l.UserId == u.Id))` on a fresh context:

```csharp
protected async Task A_passwordless_account_cannot_remove_its_only_login()
// seed without password; link "A"; fresh manager; RemoveLoginAsync("A") → Succeeded false, Errors single Code == NorseIdentityErrorCodes.LastSignInMethod; GetLoginsAsync count 1; OrphansAsync() == 0

protected async Task A_second_sequential_removal_on_a_fresh_instance_is_refused()
// seed without password; link "A" and "B"; fresh → remove "A" succeeds; fresh → remove "B" → LastSignInMethod; one login left

protected async Task A_password_account_can_remove_its_only_login()
// seed with password; link "A"; remove → Succeeded; zero logins; OrphansAsync() == 0

protected async Task Two_concurrent_removals_from_a_passwordless_account_leave_exactly_one()
// seed without password; link "A","B"; two fresh managers over two instances; Task.WhenAll(removeA, removeB);
// exactly one Succeeded; the other's single error Code is LastSignInMethod or ConcurrencyFailure; GetLoginsAsync on a fresh read has count 1; OrphansAsync() == 0
```

  Concrete classes: `[Collection(PostgresTestGroup.Name)] public sealed class PostgresLastSignInMethodTests(PostgresIdentityFixture fixture) : LastSignInMethodSuite(fixture);` and the SQL Server twin.
- [ ] **Step 2: Run** `dotnet test Himinbjorg/tests/Identity.Web.Server.Tests -- --filter-class "*LastSignInMethodTests"` — the first two facts FAIL (the delete succeeds today).
- [ ] **Step 3: Implement** the dialect fields, `HasPasswordForUpdateAsync`, the arm, the alias.
- [ ] **Step 4: Run** — PASS on both providers. `ScaffoldWritePathSuite.ExternalLoginsAsync` (`tests/Identity.Web.Server.Tests/ScaffoldWritePathSuite.cs:357`) seeds without a password, links one login and removes it, which the guard now refuses: change that seed to `SeedAsync(withPassword: true)` with a one-line comment naming §5.9, then run `"*ScaffoldWritePathTests"` and `"*.NorseUserStoreTests"` — green.
- [ ] **Step 5: Stage** and stop.

### Task 2.3: `AccountService` with all eight commands, `GetAccountStatus`, `SetPhoneNumber`; `IdentityErrorFields` overload

**Files:**
- Create: `Himinbjorg/src/Identity.Web.Server/Account/AccountService.cs`, all eight `{Op}Command.cs` records (`GetAccountStatus`, `SetPhoneNumber`, `ChangePassword`, `SetPassword`, `ChangeEmail`, `GetExternalLogins`, `RemoveExternalLogin`, `DeletePersonalData`), `GetAccountStatusHandler.cs`, `SetPhoneNumberHandler.cs`
- Modify: `Himinbjorg/src/Identity.Web.Server/IdentityErrorFields.cs`, `ServiceCollectionExtensions.cs`
- Test: `Himinbjorg/tests/Identity.Web.Server.Tests/Account/AccountServiceTests.cs`, `GetAccountStatusHandlerTests.cs`, `SetPhoneNumberHandlerTests.cs`, `IdentityErrorFieldsTests.cs`, `ServiceCollectionExtensionsTests.cs`

**Interfaces:**
- Produces `public sealed class AccountService(ISender sender) : IAccountService` (namespace `Norse.Identity.Web.Server.Account`), every method `[Authorize(Policy = AuthNPolicies.Account)]` and one line: `sender.Send(new {Op}Command(request), cancellationToken).AsTask()`; the two request-less ones each send their own command over `Unit.Value` — `GetAccountStatus` sends `new GetAccountStatusCommand(Unit.Value)`, `GetExternalLogins` sends `new GetExternalLoginsCommand(Unit.Value)`. Registered `.AddScoped<IAccountService, AccountService>()` after `IIdentityService`. **All eight methods and all eight command records land in this task** — a class implementing `IAccountService` does not compile with a subset, and Phase 1 already shipped the full interface. Only the two handlers named here land now; Tasks 2.4–2.7 add the remaining six handlers behind commands that already exist. Until a handler exists, the generated `AddNorseIdentityWebServerHandlers()` simply registers none for that command and `Sender` fails at request time, which no test in this task exercises.
- Produces the commands, each `[Authorize(Policy = AuthNPolicies.Account)] sealed record {Op}Command({Request} Request) : CommandRequest<{Request}, {Response}>(Request);` with: `GetAccountStatusCommand(Unit)` → `AccountStatusResponse`; `SetPhoneNumberCommand(SetPhoneNumberRequest)` → `NavigationResult`; `ChangePasswordCommand(ChangePasswordRequest)` → `NavigationResult`; `SetPasswordCommand(SetPasswordRequest)` → `NavigationResult`; `ChangeEmailCommand(ChangeEmailRequest)` → `BoolResponse`; `GetExternalLoginsCommand(Unit)` → `ExternalLoginsResponse`; `RemoveExternalLoginCommand(RemoveExternalLoginRequest)` → `NavigationResult`; `DeletePersonalDataCommand(DeletePersonalDataRequest)` → `NavigationResult`.
- Produces `GetAccountStatusHandler(UserManager<NorseUser> userManager, SignInManager<NorseUser> signInManager, IPrincipalAccessor principalAccessor)`: fold, then `HasPasswordAsync`, `IsEmailConfirmedAsync`, `GetTwoFactorEnabledAsync`, `GetAuthenticatorKeyAsync() is not null`, `CountRecoveryCodesAsync`, `signInManager.IsTwoFactorClientRememberedAsync(user)`, `(await GetPasskeysAsync(user)).Count`, `(await GetLoginsAsync(user)).Count` → `Ok(new AccountStatusResponse { … })`.
- Produces `SetPhoneNumberHandler(UserManager<NorseUser> userManager, SignInManager<NorseUser> signInManager, IPrincipalAccessor principalAccessor, DeferredCompletion deferredCompletion, IHttpContextAccessor httpContextAccessor)`: phone resolution — `Success<PhoneNumber>` → `WireValue`; `Failure { Reason: ParseFailure.Empty }` → `null`; any other `Failure` → `Err(Validation, { [nameof(SetPhoneNumberRequest.Phone)] = ["Enter a valid phone number (+15551234567)."] })` (tripwire); fold; `userManager.Outcome.SetPhoneNumberAsync(user, phone)` → `Failed` pass-through; `await signInManager.RefreshSignInAsync(user)`; `var url = UriHelper.BuildRelative(pathBase, "/Account/Manage", QueryString.Create("status", ManageStatus.PhoneUpdated))`; `NextUrl = deferredCompletion.TryBuildUrl(url) ?? url`. (`ManageStatus` is Heimdall's `AuthN.Components.Shared`, reachable transitively through `AuthN.Components.FluentUI`; the keys are constants there so the server and the page agree by name.)
- Produces `IdentityErrorFields.Regroup(IReadOnlyDictionary<string, string[]> errors, Func<string, string> fieldFor)`; the existing `Regroup(errors)` delegates with `For`; new `public static string ForPasswordChange(string identityErrorCode)`: `PasswordMismatch` → `"OldPassword"`; the six `Password*` rule codes → `"NewPassword"`; else `""`.

- [ ] **Step 1: Write the failing tests** (handler facts with `MockUserManager`/`MockSignInManager`, `FakePrincipal.For(userId)`, `DefaultHttpContext` with `PathBase = "/base"`, the `ConfirmEmailChangeHandlerTests` helper shape):
  - `AccountServiceTests`: `Every_method_carries_the_account_policy` (reflection over `typeof(AccountService).GetMethods()` declared on the type → each has `AuthorizeAttribute` with `Policy == AuthNPolicies.Account`); `Every_command_carries_the_account_policy` (the eight command types); `Every_method_sends_its_own_command` (a `[Theory]` over the eight methods against a substituted `ISender`: `GetAccountStatus` → `Received(1).Send(Arg.Any<GetAccountStatusCommand>(), …)`, and so on, with the request instance carried through for the six request-bearing ones).
  - `GetAccountStatusHandlerTests`: `Answers_every_status_field_from_the_managers` (stub all eight reads; assert each); `An_anonymous_caller_answers_NotFound` (`FakePrincipal.Empty()`, `GetUserAsync` returns null); `An_erased_subject_answers_Erased`.
  - `SetPhoneNumberHandlerTests`: `A_parsed_phone_is_stored_normalized_and_the_sign_in_refreshed` (`PhoneInput = "+1 (555) 123-4567"` → `Received(1).SetPhoneNumberAsync(user, "+15551234567")`, `Received(1).RefreshSignInAsync(user)`, `NextUrl == "/base/Account/Manage?status=phone-updated"`); `An_empty_phone_clears_it` (`SetPhoneNumberAsync(user, null)`); `A_malformed_phone_trips_the_wire_with_Validation_on_the_phone_field` (`DidNotReceiveWithAnyArgs().SetPhoneNumberAsync`); `A_store_failure_passes_through_and_refreshes_nothing` (`IdentityResult.Failed(describer.ConcurrencyFailure())` → `Conflict`, no refresh); `A_refresh_on_a_started_response_answers_the_completion_url` (the `TwoFactorHarness` shape from `ConfirmEmailChangeHandlerTests`).
  - `IdentityErrorFieldsTests`: `ForPasswordChange_keys_mismatch_to_the_old_field_and_rules_to_the_new_one`; `Regroup_with_a_selector_merges_codes_onto_their_field` (`{ PasswordTooShort: [a], PasswordRequiresDigit: [b], PasswordMismatch: [c] }` → `{ NewPassword: [a, b], OldPassword: [c] }`).
  - `ServiceCollectionExtensionsTests`: `Registers_the_account_service_as_scoped` (the last `IAccountService` descriptor's `ImplementationType` is `AccountService`, lifetime scoped).
- [ ] **Step 2: Run** — build errors.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the five classes and `"*.OutcomeSurfaceEnforcementTests"` — PASS.
- [ ] **Step 5: Stage** and stop.

### Task 2.4: `ChangePassword` and `SetPassword` handlers

**Files:**
- Create: `Himinbjorg/src/Identity.Web.Server/Account/ChangePasswordHandler.cs`, `SetPasswordHandler.cs` (the commands exist from Task 2.3)
- Test: `Himinbjorg/tests/Identity.Web.Server.Tests/Account/ChangePasswordHandlerTests.cs`, `SetPasswordHandlerTests.cs`

**Interfaces:**
- Consumes `ChangePasswordCommand`, `SetPasswordCommand` and `IdentityErrorFields.Regroup(errors, fieldFor)` / `ForPasswordChange` from Task 2.3.
- Produces `ChangePasswordHandler(UserManager<NorseUser>, SignInManager<NorseUser>, IPrincipalAccessor, DeferredCompletion, IHttpContextAccessor)`: fold; `userManager.Outcome.ChangePasswordAsync(user, wire.OldPassword, wire.NewPassword)`; `Failed failed` → `new Failed(failed.Problem with { Errors = IdentityErrorFields.Regroup(failed.Problem.Errors, IdentityErrorFields.ForPasswordChange) })` — a record copy that replaces only `Errors`, so `Category`, `CorrelationId` and the erasure `Receipt` the map attaches on a `KeyDestroyedException` thrown mid-write all survive; success → `RefreshSignInAsync`; `NextUrl` = completion-or-`/Account/Manage/ChangePassword?status=password-changed`.
- Produces `SetPasswordHandler(…same…)`: `userManager.Outcome.AddPasswordAsync(user, wire.NewPassword)`; failures copied the same way (`UserAlreadyHasPassword` keys to `""`, category `Conflict`); success → refresh; `NextUrl` = completion-or-`/Account/Manage/ChangePassword?status=password-set`.

- [ ] **Step 1: Write the failing tests.**
  - `ChangePasswordHandlerTests`: `Success_refreshes_the_sign_in_and_answers_the_status_url`; `A_wrong_current_password_lands_on_the_old_password_field_as_InvalidCredentials` (`IdentityResult.Failed(describer.PasswordMismatch())` → category `InvalidCredentials`, `Errors["OldPassword"]`, no refresh); `Password_rule_failures_regroup_onto_the_new_password_field` (`PasswordTooShort` + `PasswordRequiresDigit` → `Validation`, `Errors["NewPassword"]` has both); `An_indeterminate_commit_passes_through` (`IndeterminateOutcome` code → `Indeterminate`, no refresh); `An_erased_subject_answers_Erased` (erased at the initial lookup); `An_erasure_during_the_write_answers_Erased_with_its_receipt` (`GetUserAsync` returns the user, `ChangePasswordAsync` on the substitute throws the `KeyDestroyedException` the `UserManagerOutcomeViewTests` erased-write fact uses → `Erased`, `Problem.Receipt` is the same instance, `Errors` regrouped, no refresh).
  - `SetPasswordHandlerTests`: `Success_refreshes_the_sign_in_and_answers_the_status_url`; `An_account_that_already_has_a_password_answers_Conflict` (`describer.UserAlreadyHasPassword()` → `Conflict`, `Errors[""]`, no refresh); `Password_rule_failures_regroup_onto_the_new_password_field`; `An_erasure_during_the_write_answers_Erased_with_its_receipt` (same shape over `AddPasswordAsync`); `A_refresh_on_a_started_response_answers_the_completion_url`.
- [ ] **Step 2: Run** — build errors.
- [ ] **Step 3: Implement** the two handlers.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Stage** and stop.

### Task 2.5: `ChangeEmail` handler, `AccountLinks.ConfirmEmailChange`, the same-user `NextUrl`

**Files:**
- Create: `Himinbjorg/src/Identity.Web.Server/Account/ChangeEmailHandler.cs` (the command exists from Task 2.3)
- Modify: `Himinbjorg/src/Identity.Web.Server/AccountLinks.cs`, `ConfirmEmailChangeHandler.cs`
- Test: `Himinbjorg/tests/Identity.Web.Server.Tests/Account/ChangeEmailHandlerTests.cs`, `AccountLinksTests.cs`, `ConfirmEmailChangeHandlerTests.cs`

**Interfaces:**
- Produces `AccountLinks.ConfirmEmailChange(HttpRequest request, string userId, string email, string code)` → absolute `/Account/ConfirmEmailChange?userId=…&email=…&code=…` (the query names Heimdall's `ConfirmEmailChange.razor` shim reads), built like its two siblings.
- Produces `ChangeEmailHandler(UserManager<NorseUser> userManager, IEmailSender<NorseUser> emailSender, IPrincipalAccessor principalAccessor, IHttpContextAccessor httpContextAccessor)`: tripwire on `NewEmail` (the `ConfirmEmailChangeHandler` wording, keyed `NewEmail`); fold; `var current = await userManager.GetEmailAsync(user)`; `string.Equals(current, newEmail, StringComparison.OrdinalIgnoreCase)` → `Err(Validation, { [nameof(ChangeEmailRequest.NewEmail)] = ["Your email is unchanged."] })`; `GenerateChangeEmailTokenAsync(user, newEmail)` → `WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token))` → `AccountLinks.ConfirmEmailChange(request, user.Id.ToString(), newEmail, code)` → `emailSender.SendConfirmationLinkAsync(user, newEmail, link)` → `Ok(new BoolResponse { Value = true })`.
- `ConfirmEmailChangeHandler`: the same-user branch's URL becomes `UriHelper.BuildRelative(pathBase, "/Account/Manage/Email", QueryString.Create("status", ManageStatus.EmailChanged))` (completion-wrapped as today); the other-user branch is unchanged.

- [ ] **Step 1: Write the failing tests.**
  - `AccountLinksTests`: `ConfirmEmailChange_builds_the_absolute_link_with_user_email_and_code` (scheme `https`, host `example.test`, PathBase `/base` → `https://example.test/base/Account/ConfirmEmailChange?userId=u&email=new%40example.com&code=c`).
  - `ChangeEmailHandlerTests` (a substituted `IEmailSender<NorseUser>`): `A_new_address_generates_a_token_and_sends_an_absolute_link_and_answers_true` (`Received(1).SendConfirmationLinkAsync(user, "new@example.com", Arg.Is<string>(l => l.StartsWith("https://example.test/base/Account/ConfirmEmailChange?")))`); `The_current_address_in_any_case_answers_Validation_and_sends_nothing` (`GetEmailAsync` returns `"User@Example.com"`, request `"user@example.com"` → `Validation` on `NewEmail`; `DidNotReceiveWithAnyArgs().GenerateChangeEmailTokenAsync`; sender received nothing); `An_erased_subject_answers_Erased`.
  - `ConfirmEmailChangeHandlerTests`: `A_confirmed_change_by_the_same_user_refreshes_the_sign_in_and_answers_the_root` is renamed `…_and_answers_the_email_page_with_its_status` and asserts `NextUrl == "/base/Account/Manage/Email?status=email-changed"`.
- [ ] **Step 2: Run** — build errors / the renamed fact red.
- [ ] **Step 3: Implement** the handler, the link builder and the `NextUrl` change.
- [ ] **Step 4: Run** — PASS. Run `dotnet test Himinbjorg/tests/Identity.Web.Server.Tests` — green.
- [ ] **Step 5: Stage** and stop.

### Task 2.6: `GetExternalLogins`, `RemoveExternalLogin`, and the `LinkExternalLogin` GET pair

**Files:**
- Create: `Himinbjorg/src/Identity.Web.Server/Account/GetExternalLoginsHandler.cs`, `RemoveExternalLoginHandler.cs` (the commands exist from Task 2.3)
- Modify: `Himinbjorg/src/Identity.Web.Server/IdentityComponentsEndpointRouteBuilderExtensions.cs:106-125`
- Test: `Himinbjorg/tests/Identity.Web.Server.Tests/Account/GetExternalLoginsHandlerTests.cs`, `RemoveExternalLoginHandlerTests.cs`, `LinkExternalLoginEndpointTests.cs`

**Interfaces:**
- Produces `GetExternalLoginsHandler(UserManager<NorseUser>, SignInManager<NorseUser>, IPrincipalAccessor)`: fold; `var logins = await userManager.GetLoginsAsync(user)`; `var schemes = await signInManager.GetExternalAuthenticationSchemesAsync()`; `Logins = [.. logins.Select(l => new ExternalLoginEntry { LoginProvider = l.LoginProvider, ProviderKey = l.ProviderKey, DisplayName = l.ProviderDisplayName ?? l.LoginProvider })]`; `AvailableProviders = [.. schemes.Where(s => logins.All(l => l.LoginProvider != s.Name)).Select(s => new ExternalProviderEntry { Name = s.Name, DisplayName = s.DisplayName ?? s.Name })]`; `CanRemove = await userManager.HasPasswordAsync(user) || logins.Count > 1`.
- Produces `RemoveExternalLoginHandler(UserManager<NorseUser>, SignInManager<NorseUser>, IPrincipalAccessor, DeferredCompletion, IHttpContextAccessor)`: fold; `userManager.Outcome.RemoveLoginAsync(user, wire.LoginProvider, wire.ProviderKey)` → `Failed` pass-through (no count of its own, §5.9); success → `RefreshSignInAsync`; `NextUrl` = completion-or-`/Account/Manage/ExternalLogins?status=external-login-removed`.
- Produces the endpoint changes: `manageGroup = accountGroup.MapGroup("/Manage").RequireAuthorization(AuthNPolicies.Account)`; `MapPost("/LinkExternalLogin", …)` is replaced by `MapGet("/LinkExternalLogin", (HttpContext context, [FromServices] SignInManager<NorseUser> signInManager, [FromQuery] string provider) => …)` whose redirect URL is `UriHelper.BuildRelative(context.Request.PathBase, "/Account/Manage/LinkExternalLoginCallback")`, no `TemporaryFluentButtonFix` (a query value is not a FluentButton value); new `MapGet("/LinkExternalLoginCallback", async (HttpContext context, [FromServices] UserManager<NorseUser> userManager, [FromServices] SignInManager<NorseUser> signInManager, CancellationToken cancellationToken) => …)`: `userManager.Outcome.GetUserAsync(context.User, ct)` → `Failed` → redirect `not-added`; `signInManager.GetExternalLoginInfoAsync(user.Id.ToString())` null → `not-added`; `userManager.Outcome.AddLoginAsync(user, info, ct)` → `Failed` → `not-added`; `context.SignOutAsync(IdentityConstants.ExternalScheme)`; `TypedResults.LocalRedirect` to `~/Account/Manage/ExternalLogins?status=external-login-added`; the failure redirect is the same path with `external-login-not-added`. `ExternalLogins.LinkLoginCallbackAction` is no longer referenced from here.

- [ ] **Step 1: Write the failing tests.**
  - `GetExternalLoginsHandlerTests`: `Lists_linked_logins_and_the_providers_not_yet_linked` (logins `[Google]`, schemes `[Google, Microsoft]` → `Logins` one, `AvailableProviders` one named `Microsoft`); `Can_remove_when_a_password_exists_or_more_than_one_login` (theory: password/1 login → true; no password/2 → true; no password/1 → false).
  - `RemoveExternalLoginHandlerTests`: `Success_refreshes_the_sign_in_and_answers_the_status_url`; `A_last_sign_in_method_refusal_answers_Conflict_and_refreshes_nothing` (`IdentityResult.Failed(new IdentityError { Code = NorseIdentityErrorCodes.LastSignInMethod, Description = "…" })` → `Conflict`, `DidNotReceive().RefreshSignInAsync`); `An_erased_subject_answers_Erased`.
  - `LinkExternalLoginEndpointTests` — a `WebApplication.CreateSlimBuilder()` host with `MockUserManager`/`MockSignInManager` substitutes registered, a test middleware setting `context.User = FakePrincipal.For(userId)` principal before routing, `AddAuthorization` with `AuthNPolicies.Account` added via `AuthNPolicies.ConfigureAccount`, `MapAdditionalIdentityEndpoints()`, `TestServer`: `The_link_start_is_a_GET_that_challenges_the_named_provider` (GET `/Account/Manage/LinkExternalLogin?provider=Google` → `signInManager.Received(1).ConfigureExternalAuthenticationProperties("Google", Arg.Is<string>(u => u.EndsWith("/Account/Manage/LinkExternalLoginCallback")), Arg.Any<string>())`); `No_POST_link_start_remains` (POST → 405 or 404); `The_callback_adds_the_login_and_redirects_with_the_added_key` (`GetExternalLoginInfoAsync` returns an `ExternalLoginInfo`, `AddLoginAsync` → `Success` → 302 to `/Account/Manage/ExternalLogins?status=external-login-added`); `A_login_already_associated_redirects_with_the_not_added_key` (`describer.LoginAlreadyAssociated()`); `A_missing_external_cookie_redirects_with_the_not_added_key`; `The_manage_group_requires_the_account_policy` (an anonymous-role principal → 403).
- [ ] **Step 2: Run** — build errors / failures.
- [ ] **Step 3: Implement** the two handlers and the endpoint changes.
- [ ] **Step 4: Run** — PASS, including `"*.OutcomeSurfaceEnforcementTests"` (the endpoint file is scanned; `AddLoginAsync` must go through the view).
- [ ] **Step 5: Stage** and stop.

### Task 2.7: `DeletePersonalData` — the erasure front door (§5.6)

**Files:**
- Create: `Himinbjorg/src/Identity.Web.Server/Account/DeletePersonalDataHandler.cs` (the command exists from Task 2.3)
- Test: `Himinbjorg/tests/Identity.Web.Server.Tests/Account/DeletePersonalDataHandlerSuite.cs`, `Account/PostgresDeletePersonalDataHandlerTests.cs`, `Account/SqlServerDeletePersonalDataHandlerTests.cs`

**Interfaces:**
- Produces `sealed partial class DeletePersonalDataHandler(UserManager<NorseUser> userManager, SignInManager<NorseUser> signInManager, IPrincipalAccessor principalAccessor, ErasureService erasure, DeferredCompletion deferredCompletion, IHttpContextAccessor httpContextAccessor, ILogger<DeletePersonalDataHandler> logger)`:
  - `const int ShredAttempts = 3`; `static readonly Failed _incorrectPassword = new(Problem.ModelError(ErrorCategory.InvalidCredentials, "Incorrect password."))`; `const string ShredFailureMessage = "Your account could not be fully deleted. The failure has been logged for an administrator to finish."`.
  - fold; `if (await userManager.HasPasswordAsync(user) && !await userManager.CheckPasswordAsync(user, wire.Password)) return new(_incorrectPassword);`
  - `var shred = await erasure.ShredWithRetriesAsync(user.Id, ShredAttempts, ct)`; `Failed failed` → `new Failed(failed.Problem with { Errors = new Dictionary<string, string[]> { [""] = [ShredFailureMessage] } })` — a record copy replacing only `Errors`, so the category (`Fault` / `Indeterminate` / `NotFound`), the correlation id and any receipt survive and the wording is the one user-facing line; `LogShredFailed(logger, user.Id, category)` at `Error`.
  - success → `await signInManager.SignOutAsync()`; `LogSelfErasure(logger, user.Id)` (`[LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} deleted themselves.")]`); `var appRoot = UriHelper.BuildRelative(pathBase, "/")`; `NextUrl = deferredCompletion.TryBuildUrl(appRoot) ?? appRoot`.

- [ ] **Step 1: Write the failing suite.** `ErasureService` is `sealed` and takes a `NorseIdentityDbContext`, the key store and a logger (`DisclosureHandlerTests.cs:94` constructs it that way), so every handler fact runs over a real database: `public abstract class DeletePersonalDataHandlerSuite(IdentityFixture fixture)` in the `ErasureServiceSuite` shape, with a `BuildHandler(DefaultHttpContext http, ErasureService erasure)` helper over `_fixture.CreateUserManager()`, `_fixture.CreateSignInManager(http)`, `FakePrincipal.For(id)`, a real `DeferredCompletion`, and `NullLogger`. Sealed `PostgresDeletePersonalDataHandlerTests` / `SqlServerDeletePersonalDataHandlerTests` in their collections. Facts:
  - `A_wrong_password_answers_the_static_instance_and_shreds_nothing` — seed with a password; call with `"wrong"` → `TryGetValue(out Failed f)`, `f.ShouldBeSameAs(DeletePersonalDataHandler.IncorrectPassword)` (an `internal static` accessor over the field; `InternalsVisibleTo` is already granted); `RereadAsync(id)` is not null; `http.Response.Headers.SetCookie` carries no sign-out.
  - `A_passwordless_account_is_shredded_without_a_password_check` — seed without a password (the `ScaffoldWritePathSuite.SeedAsync()` default); call with `Password = ""` → `Success`, `NextUrl == "/"`; `RereadAsync(id)` is null.
  - `A_password_account_is_shredded_and_signed_out_with_the_right_password` — `Success`, `NextUrl == "/"`; row gone; the fixture's key store reports the subject key destroyed; a second call with the same principal → `NotFound`.
  - `A_shred_fault_keeps_its_category_under_the_one_wording` — build the `ErasureService` over a context carrying `CommitFailureInterceptor(CommitFailure.AfterCommit)` (the `CommitOutcomeSuite.cs:564` recipe) → `Failed` whose `Problem.Category` is `Indeterminate`, `Errors[""]` is `ShredFailureMessage`, and the user row is still present.
  - `An_erased_subject_answers_Erased` — a principal whose subject key is already destroyed (the `ErasureServiceSuite` destroyed-key setup) → `Erased`.
- [ ] **Step 2: Run** — build errors.
- [ ] **Step 3: Implement** the handler.
- [ ] **Step 4: Run** `"*DeletePersonalDataHandlerTests"` on both providers and `"*.OutcomeSurfaceEnforcementTests"` — PASS.
- [ ] **Step 5: Stage** and stop.

### Task 2.8: Retire the six Manage pages; docs; ship gate

**Files:**
- Delete: `Himinbjorg/src/Identity.Web.Server/Components/Pages/Manage/Index.razor`, `Email.razor`, `ChangePassword.razor`, `SetPassword.razor`, `ExternalLogins.razor`, `DeletePersonalData.razor`
- Modify: `Himinbjorg/README.md:11,13,63`, `CLAUDE.md:17,23,47,62`, `src/Identity.Web.Server/README.md:3,5`
- Test: `Himinbjorg/tests/Identity.Web.Server.Tests/ScaffoldRetirementTests.cs`

- [ ] **Step 1: Write the failing test** — extend the retired-route array with `/Account/Manage`, `/Account/Manage/Email`, `/Account/Manage/ChangePassword`, `/Account/Manage/SetPassword`, `/Account/Manage/ExternalLogins`, `/Account/Manage/DeletePersonalData` (exact match, so `/Account/Manage/Passkeys` and the other Train 3 routes stay).
- [ ] **Step 2: Run** — FAIL (six matches).
- [ ] **Step 3: Delete** the six files. `Shared/ManageLayout.razor`, `Shared/ManageNavMenu.razor`, `Manage/_Imports.razor` and the seven Train 3 pages stay; `ManageNavMenu`'s links to the deleted routes now resolve to Heimdall's pages in the host. Grep `Identity.Web.Server` for `ExternalLogins.`, `LinkLoginCallbackAction` and `RedirectToCurrentPageWithStatus` and fix every remaining reference (the endpoint file stopped referencing `ExternalLogins` in Task 2.6; `IdentityRedirectManager` keeps its methods for the Train 3 pages).
- [ ] **Step 4: Run** `dotnet build Himinbjorg.slnx` and `dotnet test Himinbjorg.slnx` — green (`ScaffoldWritePathSuite` keeps its page-named cases; they drive the managers, not the pages).
- [ ] **Step 5: Docs.** `README.md:11`, `CLAUDE.md:47` and `src/Identity.Web.Server/README.md:3`: the hydrate-and-send list gains `Account/AccountService` and the eight account commands; `README.md:63`, `CLAUDE.md:17/23`: what survives is now "the two-factor and passkey pages under `/Account/Manage` and the external-login callback"; `CLAUDE.md:62` unchanged (the exemption ends in Train 3). Add two architecture facts: the §5.9 guard ("`FlushExecutor`'s `RemoveLogin` arm counts under a `FOR UPDATE` / `UPDLOCK, HOLDLOCK` lock on `user_credentials` and refuses the last sign-in method with `LastSignInMethod → Conflict`; `LastSignInMethodSuite` pins it on both providers") and the link pair ("`GET /Account/Manage/LinkExternalLogin?provider=` and `GET /Account/Manage/LinkExternalLoginCallback` are the only Manage HTTP endpoints; the manage group requires `AuthNPolicies.Account`; the callback redirects with `?status=external-login-added|not-added`"). Update the Doc index row for the lifecycle spec to name the Train 2 plan.
- [ ] **Step 6: Stage** and stop. Ship gate: Himinbjörg PR, tag, publish (`HiminbjorgVersion` → next). No migration changes in this train; no volume wipe.

---

## Phase 3 — Bragi

### Task 3.1: Scenarios, fakes, session provider, shell, registrations

**Files:**
- Create: `Bragi/src/DesignSystem.Stories/Account/AccountScenario.cs`, `FakeAccountService.cs`; `Identity/IdentityScenario.cs`, `FakeIdentityService.cs`; `Session/SessionScenario.cs`, `FakeAuthenticationStateProvider.cs`; `Shell/StoryShell.razor`, `StoryShellLayout.cs`
- Modify: `Bragi/src/DesignSystem.Stories/ServiceCollectionExtensions.cs`, `_Imports.razor`
- Test: `Bragi/tests/DesignSystem.Stories.Tests/Account/FakeAccountServiceTests.cs`, `Identity/FakeIdentityServiceTests.cs`, `Session/FakeAuthenticationStateProviderTests.cs`, `ServiceCollectionExtensionsTests.cs`

**Interfaces:**
- Consumes Heimdall's `IAccountService`, `IIdentityService`, the six validators, `IAppShellLayout`, `NorseRoles.Anonymous`, `AuthNPolicies.ConfigureAccount`.
- Produces `enum AccountScenario { Unspecified = 0, Success = 1, ExternalOnly = 2, TwoFactorDisabled = 3, RecoveryCodesLow = 4, NoPasskeys = 5, IncorrectPassword = 6, PasswordPolicy = 7, InvalidVerificationCode = 8, Fault = 9 }` (the whole §7 list now, so Train 3 renumbers nothing; `InvalidVerificationCode` applies to no Train 2 method and throws "does not apply" like every foreign scenario).
- Produces `sealed class FakeAccountService(Scenario<AccountScenario> scenario) : IAccountService` (the `FakeAuthenticationService` switch shape, `CatalogCorrelationId` reused for `Fault`). Immutable fixtures:
  - status `Success`: `{ HasPassword = true, EmailConfirmed = true, TwoFactorEnabled = true, HasAuthenticator = true, RecoveryCodesLeft = 3, IsMachineRemembered = false, PasskeyCount = 2, ExternalLoginCount = 1 }`; `ExternalOnly`: `HasPassword = false`; `TwoFactorDisabled`: `TwoFactorEnabled = false, HasAuthenticator = false, RecoveryCodesLeft = 0`; `RecoveryCodesLow`: `RecoveryCodesLeft = 1`; `NoPasskeys`: `PasskeyCount = 0`; every other non-`Fault` scenario answers the `Success` status (reads must succeed for a write story to render its form).
  - logins `Success`: `Logins = [{ "Google", "story-google", "Google" }]`, `AvailableProviders = [{ "Microsoft", "Microsoft Account" }]`, `CanRemove = true`; `ExternalOnly`: `CanRemove = false`; other non-`Fault` → the `Success` fixture.
  - `SetPhoneNumber`: `Success` → `Ok("/Account/Manage?status=phone-updated")`; `Fault`.
  - `ChangePassword`: `Success` → `Ok("/Account/Manage/ChangePassword?status=password-changed")`; `IncorrectPassword` → `Err(InvalidCredentials, { ["OldPassword"] = ["Incorrect password."] })`; `PasswordPolicy` → `Err(Validation, { ["NewPassword"] = ["Passwords must have at least one digit ('0'-'9')."] })`; `Fault`.
  - `SetPassword`: `Success` → `Ok("…?status=password-set")`; `PasswordPolicy`; `Fault`.
  - `ChangeEmail`: `Success` → `Ok(true)`; `Fault`.
  - `RemoveExternalLogin`: `Success` → `Ok("/Account/Manage/ExternalLogins?status=external-login-removed")`; `ExternalOnly` → `Failed(Problem.ModelError(Conflict, "This is the only way to sign in to your account."))`; `Fault`.
  - `DeletePersonalData`: `Success` → `Ok("/")`; `IncorrectPassword` → `Failed(Problem.ModelError(InvalidCredentials, "Incorrect password."))`; `Fault` → `Failed(Problem.ModelError(Fault, "Your account could not be fully deleted. The failure has been logged for an administrator to finish."))` with the catalog correlation id.
- Produces `enum IdentityScenario { Unspecified = 0, Success = 1, Fault = 2 }` and `FakeIdentityService`: `GetMyPersonalDataAsync` → `Success`: `Ok(new PersonalDataResponse { Email = "designer@example.com", PhoneNumber = "+15551234567" })`; `Fault`. `GetMaskedPersonalDataAsync` → `Success`: `Ok(new MaskedPersonalDataResponse { … })` with the masked forms the primitives produce (`EmailAddress.Parse("designer@example.com")` → `.Masked`, `PhoneNumber.Parse("+15551234567")` → `.Masked`); `Fault`.
- Produces `enum SessionScenario { Unspecified = 0, SignedIn = 1, Anonymous = 2 }` and `sealed class FakeAuthenticationStateProvider(Scenario<SessionScenario> scenario) : AuthenticationStateProvider`: `SignedIn` → `ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, "0badc0de-0bad-c0de-0bad-c0de0badc0de"), new(ClaimTypes.Name, "designer@example.com"), new(ClaimTypes.Role, "user")], "Story"))`; `Anonymous` → `ClaimsIdentity([new(ClaimTypes.Role, NorseRoles.Anonymous)], "Story")` (authenticated, like the real anonymous principal).
- Produces `Shell/StoryShell.razor` (`@inherits LayoutComponentBase`, `@Body` only) and `sealed class StoryShellLayout : IAppShellLayout { public Type LayoutType => typeof(StoryShell); }`.
- `AddNorseStoryFakes()` gains: `AddScoped(static _ => new Scenario<AccountScenario>(AccountScenario.Success))`, `AddScoped<IAccountService, FakeAccountService>()`, the `IdentityScenario`/`FakeIdentityService` pair, the `SessionScenario` pair with `AddScoped<AuthenticationStateProvider, FakeAuthenticationStateProvider>()`, `AddCascadingAuthenticationState()`, `AddAuthorizationCore(options => options.AddPolicy(AuthNPolicies.Account, AuthNPolicies.ConfigureAccount))` (this closes the Train 1 carried gap — the catalog resolves `AuthNPolicies.Account` without the server generator; Yggdrasil's stories host needs no edit), `AddSingleton<IAppShellLayout, StoryShellLayout>()`, and the six validators.
- `_Imports.razor` gains `@using Norse.AuthN.Components.FluentUI.Manage`, `@using Norse.DesignSystem.Stories.Account`, `.Identity`, `.Session`, `.Shell`.

- [ ] **Step 1: Write the failing tests** — parity facts per arm (`FakeAuthenticationServiceTests` shape; `CreateFake(AccountScenario)`), a does-not-apply theory per method, `FakeAuthenticationStateProviderTests.SignedIn_yields_the_user_role_principal` / `Anonymous_yields_the_anonymous_role_principal`, and in `ServiceCollectionExtensionsTests`: `Registers_every_train_two_validator`, `Registers_the_account_identity_and_session_fakes_per_scope`, `Resolves_the_account_policy` (`IAuthorizationPolicyProvider.GetPolicyAsync(AuthNPolicies.Account)` not null; the anonymous-role principal fails it through `IAuthorizationService`), `Registers_the_story_shell_layout`.
- [ ] **Step 2: Run** `dotnet test Bragi/tests/DesignSystem.Stories.Tests` — build errors.
- [ ] **Step 3: Create the Bragi fork** `feature/account-core-stories` from `master`; **implement**.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Stage** and stop.

### Task 3.2: `StoryDriver` second password

**Files:**
- Modify: `Bragi/src/DesignSystem.Stories/Scenarios/StoryDriver.razor`, `wwwroot/storyDriver.js`
- Test: `Bragi/tests/DesignSystem.Stories.Tests/Scenarios/StoryDriverTests.cs`, `storyDriver.test.mjs`

**Interfaces:**
- Produces `[Parameter] public string NewPassword { get; set; } = "bbbbbbbb";` on `StoryDriver`, passed as a fifth argument: `drive(root, fill, email, password, newPassword)`. In `drive`, the first password input gets `password`, every later password input gets `newPassword` (the change-password form's validator refuses equal values, so one fixture cannot drive it). No new mode (§7).

- [ ] **Step 1: Write the failing tests** — `storyDriver.test.mjs`: a form with two password inputs is filled `aaaaaaaa` then `bbbbbbbb`; one password input is unchanged in behavior. `StoryDriverTests`: `Passes_the_new_password_as_the_fifth_argument` (JS interop invocation recorded with the five arguments).
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Stage** and stop.

### Task 3.3: Stories, driven-story locks, catalog docs

**Files:**
- Create: `Bragi/src/DesignSystem.Stories/Account/Profile.stories.razor`, `Email.stories.razor`, `ChangePassword.stories.razor`, `SetPassword.stories.razor`, `ExternalLogins.stories.razor`, `DeletePersonalData.stories.razor`, `Identity/PersonalData.stories.razor`
- Modify: `Bragi/src/DesignSystem.Stories/ScenarioCatalog.md`, `Bragi/CLAUDE.md`, `Bragi/README.md`
- Test: `Bragi/tests/DesignSystem.Stories.Tests/DrivenStoryNavigationTests.cs`

**Interfaces:**
- Consumes Heimdall's Manage pages and forms (`ProfileForm Email PhoneNumber`, `ChangeEmailForm Email EmailConfirmed`, `ChangePasswordForm`, `SetPasswordForm`, `ExternalLogins`, `DeletePersonalDataForm HasPassword`), `ScenarioScope<AccountScenario>`, `ScenarioScope<IdentityScenario>`, `StoryDriver` (phone rides the `Email` slot: `Email="+15551234567"`), `LayoutView(ManageLayout)` (renders through `StoryShellLayout`).
- Produces stories (`[Stories("Account/…")]`, every driven story with a `@key`):
  - `Profile`: "Default" (the `Index` page inside `LayoutView(ManageLayout)` — exercises the loader through the fakes), "Validation Errors" (`ProfileForm` + `StoryDriver SubmitOnly Email="5551234567"`), "Saved" (`FillAndSubmit Email="+15551234567"`), "Fault".
  - `Email`: "Default" (page), "Unconfirmed" (`ChangeEmailForm EmailConfirmed="false"`), "Validation Errors", "Link Sent" (`FillAndSubmit Email="new@example.com"`), "Fault".
  - `ChangePassword`: "Default" (page), "Validation Errors", "Changed" (`FillAndSubmit`), "Incorrect Password" (`IncorrectPassword`), "Password Policy" (`PasswordPolicy`), "Fault".
  - `SetPassword`: "Default" (page under `ExternalOnly`, so the set form renders), "Set", "Password Policy", "Fault".
  - `ExternalLogins`: "Default", "External Only" (`ExternalOnly`: no remove button), "Removed" (`ClickOnly`), "Fault".
  - `DeletePersonalData`: "Default", "Passwordless" (`ExternalOnly`), "Deleted" (`FillAndSubmit`), "Incorrect Password", "Fault".
  - `Identity/PersonalData`: "Default" (page in `LayoutView(ManageLayout)`), "Download" (`ClickOnly`).

- [ ] **Step 1: Write the failing tests** in `DrivenStoryNavigationTests`:

```csharp
[Fact] async Task A_profile_save_records_one_transition_and_no_history()
[Fact] async Task A_change_password_success_records_one_transition()
[Theory] [InlineData(AccountScenario.IncorrectPassword)] [InlineData(AccountScenario.PasswordPolicy)] [InlineData(AccountScenario.Fault)]
async Task A_pinned_change_password_story_renders_its_state_without_navigating(AccountScenario scenario)
[Fact] async Task A_set_password_success_records_one_transition()
[Fact] async Task A_change_email_success_renders_in_place_with_no_navigation()
[Fact] async Task Removing_an_external_login_records_one_transition()
[Fact] async Task An_external_only_account_renders_no_remove_button()
[Fact] async Task A_delete_personal_data_success_records_one_transition()
[Fact] async Task The_manage_layout_renders_through_the_story_shell()  // Render<Index>() inside LayoutView(ManageLayout) → markup contains "Manage your account"
```

- [ ] **Step 2: Run** — build errors or failures.
- [ ] **Step 3: Implement** the story files; `ScenarioCatalog.md` gains "Account scenarios", "Identity scenarios" and "Session scenarios" tables after line 60 and a "Train 2 surfaces" table; `Bragi/CLAUDE.md` line 17 names the three new scenario families, the policy registration and `StoryShellLayout`; line 21 adds the new test classes; `README.md` line 19 gains the Account and Identity story lists and line 36 the new fakes.
- [ ] **Step 4: Run** `dotnet test Bragi.slnx` — green. Run the stories host (`dotnet run --project Yggdrasil/src/Hosting.Stories` on project references) and open every new story through the MCP `list_stories` / `render` tools; record any story that renders a validation error, a missing policy, or an exception.
- [ ] **Step 5: Stage** and stop. Ship gate: Bragi PR, tag, publish.

---

## Phase 4 — Yggdrasil

### Task 4.1: Package bumps

**Files:**
- Modify: `Yggdrasil/Directory.Packages.props:6-8`

- [ ] **Step 1: Create the fork** `feature/account-lifecycle-train-2` from `master`.
- [ ] **Step 2: Bump** `HeimdallVersion`, `HiminbjorgVersion`, `BragiVersion` to the tags shipped in Phases 1–3.
- [ ] **Step 3: Run** `dotnet build Yggdrasil.slnx` — green.
- [ ] **Step 4: Stage** and stop.

### Task 4.2: Composition facts — the account service on both lanes, the Manage tree

**Files:**
- Modify: `Yggdrasil/tests/Hosting.Web.Server.Tests/Authentication/WebServerHost.cs`, `ChallengeAndForbidTests.cs`, `Yggdrasil/tests/Hosting.Web.Client.Tests/CompositionTests.cs`

**Interfaces:**
- Produces `WebServerHost.CreateIdentityClient()`: a client like `CreateBrowserClient()` whose default `Cookie` header carries a minted `IdentityConstants.ApplicationScheme` ticket — resolve `IOptionsMonitor<CookieAuthenticationOptions>` from `Services`, `Get(IdentityConstants.ApplicationScheme)`, build a `ClaimsPrincipal` with the `IdentityOptions.ClaimsIdentity` claim types (`UserIdClaimType` = a fresh `Guid.CreateVersion7()`, `RoleClaimType` = `"user"`, `SecurityStampClaimType` = `"stamp"`), authentication type the scheme name, `AuthenticationTicket` with `IssuedUtc = now`, `ExpiresUtc = now + 1h`, `options.TicketDataFormat.Protect(ticket)`, cookie name `options.Cookie.Name`. A fresh ticket is inside `SecurityStampValidatorOptions.ValidationInterval`, so no store read happens on the way through the cookie handler.

- [ ] **Step 1: Write the failing tests.**
  - `CompositionTests`: `AddNorseGrpcClients_registers_IAccountService_resolvable_from_the_container` (the `IAuthenticationService` fact's shape).
  - `ChallengeAndForbidTests`:

```csharp
[Fact]
async Task An_anonymous_browser_principal_is_forbidden_on_the_account_service()
// browser = host.CreateBrowserClient(); GET / to mint the anonymous cookie;
// POST /grpc.account.v1.AccountService/GetAccountStatus with EmptyGrpcBody() → 403

[Fact]
async Task An_identity_principal_passes_the_account_gate()
// identity = host.CreateIdentityClient(); same POST → 200 and the grpc-status trailer is not "7" (PERMISSION_DENIED) nor "16"

[Fact]
async Task An_anonymous_browser_principal_is_redirected_from_the_manage_tree_to_login()
// browser GET /Account/Manage → 302; Location starts with "/Account/Login?returnUrl="

[Fact]
async Task An_identity_principal_reaches_the_manage_shell_from_heimdalls_assembly()
// identity GET /Account/Manage/PersonalData → 200; body contains "Manage your account" and "Personal Data"
```

- [ ] **Step 2: Run** — FAIL (no `CreateIdentityClient`; before the bump the anonymous Manage GET is served by Himinbjörg's page).
- [ ] **Step 3: Implement** `CreateIdentityClient`.
- [ ] **Step 4: Run** `dotnet test Yggdrasil/tests/Hosting.Web.Server.Tests -- --filter-class "*.ChallengeAndForbidTests"` and the client tests — PASS. If the lane rejects the ticket because the anonymous cookie is absent, apply the re-plan fallback (seed the anonymous cookie first) and note it in the handoff.
- [ ] **Step 5: Stage** and stop.

### Task 4.3: End-to-end verification and docs

**Files:**
- Modify: `Yggdrasil/CLAUDE.md:43` (the identity page-tree row: Heimdall owns the account core — `/Account/Manage`, `Email`, `ChangePassword`, `SetPassword`, `ExternalLogins`, `DeletePersonalData`, `PersonalData` — and Himinbjörg keeps the two-factor and passkey pages plus the external-login callback and the `LinkExternalLogin` GET pair), `README.md` if it narrates the page tree

- [ ] **Step 1: Run the composition end to end.** From Bifröst: `dotnet run --project src/Orchestration.AppHost`. On the Server host, and on the WASM host as far as the runtime allows (record the `CompileError` if it recurs — see the §10 deviation above):
  - register, log in, open `/Account/Manage` → Heimdall's profile page inside the app shell with the nav menu; set a phone `+15551234567` → forced reload lands on `/Account/Manage?status=phone-updated` with "Your profile has been updated."; set `5551234567` → the format message, no call; clear it → saved;
  - `/Account/Manage/Email`: unconfirmed → "Send verification email" → the link in the `DevelopmentEmailSender` log → confirm through Heimdall's click-to-confirm → the page now shows the confirmed mark; change email to a new address → "Confirmation link to change email sent." → open the logged link signed in → lands on `/Account/Manage/Email?status=email-changed`; log out, log in with the new address;
  - `/Account/Manage/ChangePassword`: wrong current password → "Incorrect password." on the field; equal passwords → the client message; a weak new password → the server's rule on the field; a good one → reload with "Your password has been changed."; log in with the new password;
  - `/Account/Manage/SetPassword` on the password account → renders the change form (no redirect);
  - `/Account/Manage/ExternalLogins`: with no provider configured → "no external logins… none available"; (if a provider is configured locally) link through the GET start, land with "The external login was added.", remove → "The external login was removed.";
  - `/Account/Manage/DeletePersonalData`: wrong password → in-place; right password → shred, sign-out, lands on `/`; the old login fails; `norse_identity.users` has no row;
  - open `/Account/Manage` signed out → `/Account/Login?returnUrl=%2FAccount%2FManage`, not `/Account/Logout`.
  Record the outcome of each in the task handoff.
- [ ] **Step 2: Update** `Yggdrasil/CLAUDE.md` line 43 (and README if applicable).
- [ ] **Step 3: Run** `dotnet test Yggdrasil.slnx` — green.
- [ ] **Step 4: Stage** and stop. Ship gate: Yggdrasil PR. Train 2 is done; Train 3's plan starts from the shipped state.

---

## Self-review record

**Spec coverage (Train 2 scope):** §4.1 conventions → 1.1/1.2; §4.3 Train 2 rows → 1.1 (contract), 2.3–2.7 (handlers); §4.4 the `Account` policy on every command and the manage group → 2.3 (`AccountServiceTests`), 2.6 (endpoint), 1.3 (pages), 3.1 (catalog), 4.2 (both lanes); §5.1 handler shape → 2.3–2.7; §5.2 five write views and the one map row → 2.1; §5.4 refresh family → 2.3/2.4/2.6 (`RefreshSignInAsync` + `DeferredCompletion`); §5.6 erasure front door → 2.7; §5.7 `LinkExternalLogin` GET pair → 2.6; §5.8 `ManageLayout`/`ManageNavMenu` rebuilt in Heimdall → 1.3 (Himinbjörg's copies survive until Train 3 for the pages still there); §5.9 store guard, handler carries no count, verification query → 2.2/2.6; §6.2 loader + form → 1.4–1.8; §6.3 continuation rule and status keys → 1.3–1.8, 2.3–2.7; §6.4 `Manage/_Imports`, reflection lock, `PersonalData` move, static nav → 1.3; §6.7 six validators → 1.2; §7 `AccountScenario`/`FakeAccountService`, `IdentityScenario`/`FakeIdentityService`, `SessionScenario`/`FakeAuthenticationStateProvider`, `StoryShellLayout`, validators registered, no new driver mode → 3.1–3.3; §8 proxy resolves, policy resolves (Train 1), `/Account/Manage` routes from Heimdall, anonymous forbidden / identity passes, ordering hazard → 4.1/4.2; §10 shapes → every task, with the WASM Playwright phase recorded as deferred. Out of Train 2 by design: §4.3 Train 3 rows, §5.3, §5.5, §6.5, §6.6, the Train 3 validators and stories, the `Components/` exemption removal, the coverage ratchet.

**Type consistency:** `ManageStatus` keys (`PhoneUpdated`, `PasswordChanged`, `PasswordSet`, `EmailChanged`, `ExternalLoginRemoved`, `ExternalLoginAdded`, `ExternalLoginNotAdded`) are declared in 1.3 and consumed by 2.3–2.7 and 3.1's fixtures; `IdentityErrorFields.ForPasswordChange` / `Regroup(errors, fieldFor)` (2.3) are used in 2.4; `NorseIdentityErrorCodes.LastSignInMethod` (2.1) is raised in 2.2, mapped in 2.1, asserted in 2.6; `AccountLinks.ConfirmEmailChange(HttpRequest, string, string, string)` (2.5) matches the Heimdall shim's `userId`/`email`/`code` query names; the form parameter names (`Email`, `PhoneNumber`, `EmailConfirmed`, `HasPassword`) in 1.4–1.8 match 3.3's story usage; `FormName`s (`authn-profile`, `authn-change-email`, `authn-change-password`, `authn-set-password`, `authn-delete-personal-data`) are asserted by name in 1.4–1.8 and 4.3; `WebServerHost.CreateIdentityClient()` (4.2) is consumed only in 4.2.

**Proportion:** 4 phases, 22 tasks, against the Train 2 share of a 280-line spec. Code bodies appear only where the signature and tests do not determine them (the policy lock, the minted-cookie recipe, the concurrency suite outline, the fixture tables).
