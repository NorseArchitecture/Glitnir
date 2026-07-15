# Deferred sign-in: correct realm placement

## Problem

Task 2 of the deferred-sign-in fix (`../Heimdall/plans/2026-07-14-deferred-signin-fix.md`) staged `IDeferredSignIn` — contract and `MemoryCacheDeferredSignIn` implementation together — in Midgard's `Infrastructure.Web.Server`, and had Himinbjörg's `Identity` project take a direct `NorseRef` on it. That NorseRef is what CI caught on 2026-07-14 (`CS0234`/`CS0246` — Midgard's side was still staged on an unpublished branch), which is what started this redesign. But the NorseRef itself is wrong independent of publish sequencing, for two separate reasons surfaced in this session (2026-07-15):

1. **Himinbjörg has no reason to know Midgard exists.** Midgard's charter is implementing Asgard's declared law and providing what Yggdrasil needs — not serving Himinbjörg. `IDeferredSignIn` is genuinely generic (zero references to Midgard's mediator or persistence machinery — BCL + ASP.NET Core `Authentication`/`Claims` types only), so nothing requires Himinbjörg to reach into Midgard for it. The right home for a *contract* nothing-domain-specific is Asgard, not Midgard.

2. **`Identity` is shared with migration tooling — confirmed live, not assumed.** `Himinbjorg/src/Identity.Migrations/Identity.Migrations.csproj` carries a direct `<ProjectReference Include="../Identity/Identity.csproj" />`. `Identity.csproj`'s own `<Description>` already claims "referenced by Identity.Web.Server; never by migration tooling" — but on the current feature branch it also carries `<NorseRef Include="Infrastructure.Web.Server"><Repo>Midgard</Repo></NorseRef>`, added solely so `NorseSignInManager` (which lives in `Identity` today) can resolve `IDeferredSignIn`. That NorseRef transitively drags Midgard's gRPC/mediator/hosting surface into the migrations console tool's build — the doc comment is aspirational, not true, on this branch. `NorseSignInManager` needs `HttpContext`, `AuthenticationProperties`, and `IDeferredSignIn` — all ASP.NET-Core-web-hosting concerns a console migration tool has no business pulling in. It is filed in the wrong project, independent of where `IDeferredSignIn` itself lives.

## Decision

**Contract → Asgard. Implementation → stays in Midgard. Consumer (`NorseSignInManager`) → moves to Himinbjörg's `Identity.Web.Server`, not the base `Identity` project.**

This mirrors the platform's own precedent: Asgard declares law (`IMigrationContributor`, `Outcome<T>`), Midgard embodies it, and the ASP.NET-Core-hosting-flavored consumer sits in whichever project is actually allowed to know about ASP.NET Core hosting.

### Dependency graph

Before:
```
Himinbjörg/Identity  --NorseRef-->  Midgard/Infrastructure.Web.Server
                                        (contract + impl, both here)
Yggdrasil/Hosting.Web.Server  --(transitive only, via Identity.Web.Server)-->  Midgard/Infrastructure.Web.Server
```

After:
```
Asgard/Abstractions.Web.Server            (IDeferredSignIn, DeferredSignInAction — declared law)
        ^                                         ^
        |  NorseRef (already exists)              |  NorseRef (implements the law)
Himinbjörg/Identity.Web.Server            Midgard/Infrastructure.Web.Server
   (NorseSignInManager lives here)           (MemoryCacheDeferredSignIn, MapDeferredSignIn, AddDeferredSignIn())
                                                      ^
                                                      |  NorseRef (explicit, was transitive-only)
                                             Yggdrasil/Hosting.Web.Server
```

Himinbjörg's base `Identity` project (shared with `Identity.Migrations`) touches neither Midgard nor this feature at all, restoring its own doc comment as true. Heimdall (`Norse.AuthN.*`) is untouched by this document — it stays fully agnostic of hosting model (WASM/MAUI/Blazor Server); deferred sign-in only exists because Himinbjörg's identity is, in this one case, hosted under an interactive Blazor Server circuit. WASM/MAUI callers are always genuine HTTP/gRPC requests and never touch this path.

## Changes by repo

### Asgard — `Abstractions.Web.Server`

Add, verbatim from Midgard (no logic change):
- `IDeferredSignIn` — `StashSignIn`, `StashSignOut`, `TryConsume`
- `DeferredSignInAction` (sealed record)

Same project that already holds the `Outcome<T>` mediator-law contracts — same tier, same "declared law, zero domain knowledge" shape.

### Midgard — `Infrastructure.Web.Server`

- Remove `IDeferredSignIn.cs` (moved to Asgard).
- `MemoryCacheDeferredSignIn`, `DeferredSignInEndpointRouteBuilderExtensions`, `ServiceCollectionExtensions` (`AddDeferredSignIn()`) stay exactly where they are, now implementing Asgard's contract (`using Norse.Abstractions.Web.Server;` in place of the removed local declaration).
- No new NorseRef needed — Midgard's `Infrastructure.Web.Server` already NorseRefs `Asgard/Abstractions.Web.Server` for the mediator half.
- `MemoryCacheDeferredSignInTests` unchanged in behavior; just references the contract from its new namespace.

### Himinbjörg

- **Move** `NorseSignInManager.cs` from `src/Identity/` to `src/Identity.Web.Server/`. Its override logic (`SignInWithClaimsAsync` × 2, `SignOutAsync`, the `Context.Response.HasStarted` check) does not change.
- `Identity.Web.Server.csproj` needs **no new NorseRef** — it already carries `NorseRef Abstractions.Web.Server (Asgard)` for the `Outcome<T>` mediator law; `IDeferredSignIn` rides on that same edge.
- `Identity.csproj` **drops** `NorseRef Infrastructure.Web.Server (Midgard)` entirely.
- The `.AddSignInManager<NorseSignInManager>()` call cannot live inside `IdentityBuilderExtensions.AddNorseIdentity()` in the base `Identity` project anymore (that would cycle: `Identity` → `Identity.Web.Server` → `Identity`). It moves to a new extension method in `Identity.Web.Server` (e.g. `AddNorseIdentityWebServer()`), chained after `AddNorseIdentity()` is called, which layers `.AddSignInManager<NorseSignInManager>()` on top of the `IdentityBuilder` the base method returns.
- `NorseSignInManagerTests` moves from `tests/Identity.Tests/` to `tests/Identity.Web.Server.Tests/` (project already exists) — same test, same assertions, new home.

### Yggdrasil — `Hosting.Web.Server`

- Add an explicit `NorseRef Infrastructure.Web.Server (Midgard)` to `Hosting.Web.Server.csproj`. It already resolves today only because it rides in transitively through Himinbjörg's `Identity.Web.Server`; `Program.cs` and `BlazorServerAuthenticationGateway.cs` use `Norse.Infrastructure.Web.Server.DeferredSignIn` types directly and should declare that dependency rather than get it for free.

## Ship-gate order

Each realm ships behind its own gate (PR, CI green, tag, NuGet publish) — no step starts before the one before it has actually published, not just merged locally:

1. **Asgard** — add the contract, ship.
2. **Midgard** — consume the new Asgard version, drop its own `IDeferredSignIn.cs`, ship.
3. **Himinbjörg** (`feature/identity-web-server`) — move `NorseSignInManager`, update NorseRefs both directions, this is the change that actually unblocks the branch's merge.
4. **Yggdrasil** (`feature/hosting-web-server-authn`) — add the explicit Midgard NorseRef.

## Out of scope

This document only resolves *where* `IDeferredSignIn` and its consumer live. It does not change:
- Heimdall's planned `AuthenticationResult.DeferredCompletionUrl` field, or the `Login.razor`/`Logout.razor` navigate-on-deferred behavior — both already correctly scoped in `../Heimdall/plans/2026-07-14-deferred-signin-fix.md` and unaffected by this move.
- The deferred-sign-in mechanism itself (memory-cache stash, meta-refresh completion endpoint) — unchanged, just relocated.

## Follow-up after implementation

- Remove Open Decision #2 from `../../../CLAUDE.md` (Bifrost) — this document resolves it.
- Amend `../Heimdall/specs/2026-07-13-authn-identity-split-design.md` per that plan's own follow-up instruction, noting the final realm placement.
