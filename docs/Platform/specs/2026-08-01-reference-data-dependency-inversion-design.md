# Reference Data — Dependency Inversion and the Sovereign Namespace Doctrine

**Status:** Design approved (Buvy, 2026-08-01) — not yet planned or implemented.

**REQUIRED SUB-SKILL:** `superpowers:subagent-driven-development` (the platform default — `superpowers:executing-plans` is the narrow fallback for a separate-session review checkpoint, never an interchangeable alternative), paired with `superpowers:test-driven-development` on every task.

**Companion spec:** `2026-08-01-well-seam-midgard-excision-design.md` (same session, same verdict) — that spec handles the Midgard dependency and the runtime `DbContext` excision; this one handles the Mimir ⇄ Mimisbrunnr cycle and the namespace-registry doctrine. They ship as independent plans.

## 1. Problem

Mimir and Mimisbrunnr depend on each other. Realm dependencies flow one direction, ever — this is the highest-severity item in the current cleanup state.

The cycle, edge by edge:

- **Mimisbrunnr → Mimir (the offending direction):**
  - `src/Reference.Data`'s `CountryOrArea.Code` and `CountryOrAreaView.Code` are typed `IsoCountryCode` — generated into Mimir's `Reference.Contracts`.
  - `src/Reference.Data.Migrations`' `ReferenceDataSeedContributor` calls `IsoCountryCodes.Parse(...)` and indexes `Iso3166.Ids[...]` — both Mimir-generated surface.
  - `tools/SeedTool` carries `<NorseRef Include="Reference.Seeds"><Repo>Mimir</Repo></NorseRef>` to reach the embedded UNSD M49 CSV.
- **Mimir → Mimisbrunnr (the correct direction):** `src/Reference.Web.Server` references `Reference.Data` for `CountryOrAreaView` and the well it reads from.

The raw dataset itself (`seeds/raw/UNSD — Methodology.csv`) lives in Mimir and is consumed twice there: embedded into `Reference.Seeds` (so SeedTool can reach it across the realm boundary) and fed as `AdditionalFiles` to `Reference.Contracts.Generator`. The processed TSVs exist in **both** repos (`Mimir/seeds/` and `Mimisbrunnr/seeds/`).

Mimir consumes Mimisbrunnr for persistence; the seed data, and everything generated from it that persistence needs, therefore belongs in Mimisbrunnr. Today it is upside down.

## 2. Decision

**The seed data, the generator, and the generated surface all move to Mimisbrunnr.** The cycle dies by relocation, not rework — every current consumer of the generated surface keeps compiling with an assembly swap. Two new Mimisbrunnr projects carry the moved surface, the EF-specific projects take an `.EntityFramework` segment in their names (the same vendor-family law Urðarbrunnr and Ratatoskr live under), and Mimir's `Reference.Seeds` is deleted outright. Alongside the mechanics, this spec records the platform doctrine for deterministic-identifier namespaces: **sovereign per-realm roots, no central registry.**

## 3. Design

### 3.1 Mimisbrunnr's resulting layout

| Project | Contents | Platform / audience |
|---|---|---|
| `Reference.Data.Primitives` **(new)** | Generated `IsoCountryCode` (tri-form span parsing), `Iso3166` dataset with baked v5 identifiers | Browser-supported; EF structurally unreachable |
| `Reference.Data.Namespaces` **(new)** | Generated `ReferenceNamespaces` (ex-`MimirNamespaces`) — the v5 namespace-GUID constants | Server/tooling/tests only — never WASM |
| `Reference.Data.EntityFramework` (ex-`Reference.Data`) | Entities, `View` chain, entity configurations | Server |
| `Reference.Data.EntityFramework.Migrations` (ex-`Reference.Data.Migrations`) | Migration contributor, `ReferenceDataSeedContributor`, TSV intake | Migration tooling |
| `Reference.Data.EntityFramework.Migrations.PostgreSQL` / `.SqlServer` (renamed likewise) | Provider-targeted design-time factories, checked-in migrations, DDL scripts | Migration tooling |

The name `Reference.Data.Primitives` deliberately lines up with Svartálfheim's `Norse.Primitives` notion: primitives, but reference-data-specific and driven by third-party canonical datasets rather than hand-forged. As additional code systems land (ISO 4217, ISO 639, IANA tzdata, NCCI/SIC/NAICS class codes), their generated surfaces land here too.

### 3.2 `Reference.Data.Primitives` — the WASM charter, enforced

- `<SupportedPlatform Include="browser" />` — the charter is compile-checked, not prose.
- Dependency cap: Svartálfheim `Primitives` and Asgard `Abstractions.Contracts`. **No reference to `Reference.Data.Namespaces`** — there is never a world where the WASM app computes a v5 GUID for a code that is already baked into the enum, so the constants must not travel into the browser bundle. (Consequence: the emitted xmldoc on the `Iso3166` dataset loses its `cref` to the namespace constants — it becomes plain prose. Same for `CountryResponse.cs`'s doc line in Mimir advertising the id as "recomputable client-side": that claim is doctrine-false now; recomputation is a server/tooling/tests act.)
- **Generated surface keeps emitting into `namespace Norse.Reference`** — the shared-namespace convention between these two realms already in force. A cross-context consumer writes `Norse.Reference.IsoCountryCode` and does not care which realm bakes it. `RootNamespace` is overridden to `Norse.Reference` for the same IDE0130 reason Mimir's `Reference.Contracts` does it today.

### 3.3 `Reference.Data.Namespaces` — separate on purpose

The constants class is renamed `MimirNamespaces` → **`ReferenceNamespaces`** (it no longer lives in Mimir, and the platform convention this spec ratifies is `{Context}Namespaces` — see §3.7). It gets its own project — not folded into Primitives (WASM must not carry it) and not folded into any EF project (it contains no EF-specific code, and Mimisbrunnr may roll additional persistence families in later; the constants belong to the realm, not to a vendor family). It emits into `namespace Norse.Reference` like the rest of the generated surface.

### 3.4 The `.EntityFramework` renames

Injecting `.EntityFramework` into the EF-specific project names is the same law that renamed Urðarbrunnr's family to `Norse.Persistence.EntityFramework.*`: EF is a live vendor family, not the definition of persistence. When a different store lands in Mimisbrunnr it arrives as a sibling family instead of forcing renames. Pre-launch, the package-id break costs nothing (crooked path #11: accuracy over caution until a real consumer exists).

Rename ripple (mechanical, wide — the plan enumerates it exhaustively):

- Mimir `Reference.Web.Server`'s `NorseRef` → `Reference.Data.EntityFramework`.
- Yggdrasil CPM pins in `Directory.Packages.props` (old ids out, new ids in).
- One-test-project-per-package law: test projects rename in lockstep.
- `Mimisbrunnr.slnx`, `Mimir.slnx`, `Bifrost.slnx` entries.
- The migrations-service closure that discovers contributors through `Reference.Data.EntityFramework.Migrations.PostgreSQL`.
- README/CLAUDE.md pairs in both realms plus `decomposition.md` — boy-scout law, same change.

### 3.5 Seeds consolidate; `Reference.Seeds` dies

- `seeds/raw/UNSD — Methodology.csv` moves to `Mimisbrunnr/seeds/raw/`. The stale TSV copies in `Mimir/seeds/` are deleted; `Mimisbrunnr/seeds/` remains the only TSV home.
- **Mimir's `Reference.Seeds` project and `Reference.Seeds.Tests` are deleted outright.** The embedded-resource stream ceremony existed purely to carry the file across a realm boundary that no longer needs crossing. No realm needs the raw bytes as a stream.
- `SeedTool` drops its `NorseRef` to Mimir and reads `seeds/raw/` from disk — it is realm-internal tooling now.

### 3.6 The generator relocates and emits into two projects

`Mimir/gen/Reference.Contracts.Generator` moves to `Mimisbrunnr/gen/` (renamed to match its new home, e.g. `Reference.Data.Primitives.Generator` — plan finalizes), with its tests. Its `AdditionalFiles` CSV intake is unchanged in mechanism and now same-repo in path.

It targets two projects: enum/parsing/dataset into `Reference.Data.Primitives`, the `ReferenceNamespaces` constants into `Reference.Data.Namespaces`. Same generator core, dispatch on compilation assembly name — or two thin generator heads over shared emitters; both shapes are proven on this platform, plan decides.

The vendored `Uuid5.cs` stays vendored (generators cannot reference runtime packages), and the `NamespaceSelfVerificationTests` drift guard moves with it — that test project now lives in Mimisbrunnr and continues proving the generator's v5 math against Svartálfheim's `DeterministicGuid`.

### 3.7 Mimir's resulting shape

- `Reference.Contracts` keeps the gRPC wire records only. It drops the generator reference and the `AdditionalFiles` line, gains `<NorseRef Include="Reference.Data.Primitives"><Repo>Mimisbrunnr</Repo></NorseRef>`, and keeps its `Norse.Reference` `RootNamespace` override. `CountryQueryHandler`'s `Iso3166.Ids` lookup does not change spelling — it resolves from the new assembly.
- **No re-export, no wrapping.** Mimir references the Mimisbrunnr surface and uses it in its wire records; consumers reference whichever layer they actually need; neither assembly pretends to own the other's types.
- `Reference.Web.Server` is handled by the companion spec.

### 3.8 The Sovereign Namespace Doctrine

Ratified for the platform, recorded here because this restructure is its first application. The future state: every canonical code system (zip, ISO country/currency/language, NCCI/SIC/NAICS, time zones) — and beyond that, *any* unique text/numeric index — resolves to a deterministic v5 GUID, so a writer computes the immutable key instead of querying a lookup table, and the FK scalar is set without ever smashing the lookup. Three layers:

1. **The mechanism is Svartálfheim's, universal, and never moves.** `DeterministicGuid(namespace, name)` is the entire computational contract. Every future code system is a namespace constant plus a name string; nothing new is ever built here.
2. **Namespace constants are sovereign — one hand-minted root per realm, no central registry, no platform root.** A central registry assembly would make every new code system a cross-realm release, make one realm know every other realm's vocabulary, and choke a product realm on its own bridge. v5 namespaces are inherently decentralized: independent roots cannot collide, and chaining realm roots from a platform root buys recomputability nobody needs (roots are published constants) at the cost of either a shared dependency or copied-constant drift. The registry is a **convention**: any realm that mints deterministic ids owns a `.Namespaces` project holding a `{Context}Namespaces` class (`ReferenceNamespaces`, `IdentityNamespaces` when Himinbjörg's roles formalize, …) — server/tooling-side always. The dividing line: industry/global standards are Mimisbrunnr's charter; anything beyond belongs to the realm that owns the concept.
3. **The SQL-facing surface is per-context, shipped by migrations that already exist.** The seeded tables are the lookup (`country_or_area` carries guid and code side by side); a realm that wants a consolidated pre-joined view for the SQL jockeys ships it from its own migrations contributor into its own context's database — separate-databases law intact, no new framework. The human-facing registry of minted roots is a Glitnir doc, not code.

The least-pain test this doctrine passes: a new code system in an existing realm is one emitter/seed change in that realm; a new realm with its own codes mints a root and adds `.Namespaces`; a product company on its own bridge makes the same moves with zero platform edits, and their roots owe nothing to ours.

## 4. Testing

- Existing test suites move with their projects (one-per-package law) and stay green: generator emission tests, `NamespaceSelfVerificationTests`, `Reference.Data` entity/configuration tests, seed contributor tests, Mimir contracts tests.
- `Reference.Seeds.Tests` is deleted with its project.
- A `Reference.Data.Primitives` test asserts the browser `SupportedPlatform` and the dependency cap (no EF, no Namespaces reference in the closure) — the charter as a test, not just a csproj line.
- The Yggdrasil E2E country-lookup test continues to prove the end-to-end id math (it recomputes via `ReferenceNamespaces` — legitimate, tests see everything).

## 5. Out of scope

- The Midgard dependency in `Reference.Web.Server`, the `[NorseWell]` seam, runtime `DbContext` excision, and the `DbSet<T>` discovery retarget — companion spec.
- Additional code systems (ISO 4217, tzdata, …) — they adopt this shape when they land.
- The Glitnir doc listing minted namespace roots — created when a second root is minted; one root is not yet a registry.
