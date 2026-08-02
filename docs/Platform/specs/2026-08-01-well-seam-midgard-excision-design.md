# The Well Seam — Midgard Becomes Yggdrasil-Only and the Runtime DbContext Is Excised

**Status:** Design approved (Buvy, 2026-08-01) — not yet planned or implemented.

**REQUIRED SUB-SKILL:** `superpowers:subagent-driven-development` (the platform default — `superpowers:executing-plans` is the narrow fallback for a separate-session review checkpoint, never an interchangeable alternative), paired with `superpowers:test-driven-development` on every task.

**Companion spec:** `2026-08-01-reference-data-dependency-inversion-design.md` (same session, same verdict) — the Mimir ⇄ Mimisbrunnr cycle and namespace doctrine. This spec resolves the halt recorded 2026-08-01 ("Mimir cannot depend on Midgard — full stop") and amends `2026-08-01-well-composition-dbcontext-isolation-design.md` §3.2, whose Task 4 design put the `AddNorseWell` call in the wrong realm.

## 1. Problem

### 1.1 Mimir depends on Midgard

`Mimir/src/Reference.Web.Server` carries `<NorseRef Include="Infrastructure.Persistence.EntityFramework"><Repo>Midgard</Repo></NorseRef>` and calls `builder.AddNorseWell<ReferenceDbContext>(NorsePostgresEfProvider.Instance, connectionStringName)`. Real package-mode CI exposed it (Mimir PR #17's failing run) after `UseProjectReferences=true` had hidden it locally since the well-and-wire slice shipped. The verdict, rendered live: **the dependency itself is architecturally wrong, not a release-sequencing symptom.** Mimir also hardcodes the Postgres provider — a decision that belongs to whoever owns the runtime containers, i.e. the composition root.

### 1.2 `DbSet<T>` is outlawed, and the discovery law depends on it

`DbSet<T>` usage is outlawed outside design time, migrations, seeding (everything that feeds the migrations service), tests, and throwaway proofs of concept. The DbContext notion must not seep into the domain. Yet Midgard's `AddWell<TContext>()` makes DbSet-rooted-ness the *discovery law* — it reflects over `TContext`'s public `DbSet<TEntity>` properties at startup, then closes `RegisterCore` via `MakeGenericMethod`, dragging `RequiresUnreferencedCode`/`RequiresDynamicCode` along the whole path. The only runtime DbContext on any `src/` path today is Mimisbrunnr's `ReferenceDbContext`, which exists chiefly to carry the one DbSet property that discovery demands.

## 2. The Dependency Doctrine (ratified)

The realm-consumption law, as ruled — recorded here as the platform's dependency doctrine:

- **Svartálfheim** produces the tools the Æsir themselves use. That dependency is baked in, everywhere, always.
- **Asgard** is law every realm may pull — **sliced by domain**. `Abstractions.Web.Server` law travels only through `*.Web.Server`-shaped projects (realm → Midgard's `Infrastructure.Web.Server` → Yggdrasil's `Hosting.Web.Server`); it never reaches migrations or worker shapes, and the inverse holds equally. Web law stays out of workers; worker law stays out of the web.
- **The chassis realms — Urðarbrunnr, and Ratatoskr when it lands — exist to be consumed.** Himinbjörg calling Urðarbrunnr's `AddNorseContext`, Mimisbrunnr riding the EF chassis: intended shape, not violations.
- **Midgard rides as a peer and is consumed by exactly one realm: Yggdrasil.** Midgard takes Æsir law and produces working code, plus conventions and configurations for Yggdrasil (gRPC, protobuf, JSON, XML serialization concerns) that Asgard will never care about. No other realm — serving realms included — may have Midgard in its dependency graph.
- **Yggdrasil grabs Midgard plus all other realms and stitches them together, inversion-of-control style.** The composition root sees everything; the realms see almost nothing.

Enforcement is structural, per §2.7's preference order (compile/build time over review): see §3.6.

## 3. Design

### 3.1 `[NorseWell]` — the declaration (Asgard, `Abstractions.Backend`)

A realm declares that its entity assembly constitutes a well — dumb data, no Midgard anywhere:

```csharp
[assembly: NorseWell("norse_reference")]
```

The declaration carries the connection-string name and nothing else in the default case. **No provider** — provider binding is a composition-root act, so a consumer swapping Postgres for SQL Server touches their bridge, never the realms. The brownfield overload (see §3.5) additionally names an explicit context type.

### 3.2 The seam generator (Midgard `gen/`, runs in Yggdrasil)

A new Midgard wiring generator — same citizenship as `MapNorseGrpcServices`/`AddNorseGrpcClients`: discovery-and-wiring, never composition policy — referenced by Yggdrasil's composition root with `Generator="true"`. It walks the reference closure for `[NorseWell]` declarations and, per declared well:

1. **Discovers the entity set** from the declaring assembly — `INorseEntity` implementors, the same target Urðarbrunnr's `EntityConfigurationApplicationGenerator` already scans — and the `IViewBearer<TView>` pairs among them. **DbSet-rooted-ness stops being the discovery law**; the closure is. Well-root uniqueness (exactly one root per view) is enforced as a generator diagnostic at build time instead of a startup throw.
2. **Emits the runtime context itself** — a `file`-scoped sealed `NorseDbContext` subclass, unreferenceable by construction (the shape Glitnir's CLAUDE.md §4 already records as doctrine: "concrete per-service DbContexts are source-generated, `file`-scoped"). The generated context applies the same generated entity configurations the design-time context uses, so runtime and migrations models cannot drift — both derive from the identical `Configure` methods.
3. **Emits closed-generic registrations** — `AddNorseContextFactory<TGenerated>` (Urðarbrunnr's factory-shaped seam, unchanged from the well-composition spec §3.1) plus per-pair repository registrations. No `MakeGenericMethod`, no startup reflection scan, and the `RequiresUnreferencedCode`/`RequiresDynamicCode` annotations on the discovery path die with the reflection.

The composed entry point emitted for Yggdrasil:

```csharp
builder.AddNorseWells(NorsePostgresEfProvider.Instance);
```

One call, one provider decision, every declared well in the closure wired. Diagnostics follow the migrations-seam family (zero declarations found, duplicate view claims, entity set empty — IDs assigned at plan time from the next free `NORSE0xx` range).

### 3.3 Full excision — what a realm looks like afterward

- **The domain realm authors no runtime DbContext at all.** `ReferenceDbContext` leaves `Reference.Data.EntityFramework`'s runtime surface; the realm's `src/` shows entities, views, and configurations — the machine that hosts them is the composition root's business. One less thing a new realm author has to write, and the same thing every time they would have written it.
- **The design-time context moves to the migrations project** (`Reference.Data.EntityFramework.Migrations`), the one home where the DbContext notion is legitimate. Snapshot re-homes with it; one squash per the squash law — pre-launch, free.
- **Mimir's `Reference.Web.Server` sheds Midgard and the provider.** Its `NorseRef`s to Midgard and to Urðarbrunnr's `Persistence.EntityFramework.PostgreSQL` are deleted. `AddNorseReferenceService` shrinks to the registrations it genuinely owns: handlers and `IReferenceService`. It keeps referencing `Reference.Data.EntityFramework` for `CountryOrAreaView` — a legal realm-to-realm edge in the correct direction.

### 3.4 What Midgard deletes

`AddWell<TContext>()`'s reflection body, the `AddNorseWell<TContext>()` public entry point, and the DbSet scan go. `Repository<TContext,TEntity,TView>`, `WellMap`, `WellValidation`, and the deferred model validation stay — the generator emits registrations against an internal registration core, shrinking Midgard's public persistence surface further toward zero (smallest-footprint law). `WellMap.For`'s own reflection is untouched for now (see §6).

### 3.5 The Himinbjörg exception — designed, named, confined

Brownfield goo — ASP.NET Core Identity's `IdentityDbContext` and OpenIddict's model — cannot adopt the generated-context shape; identity flips the world on its head compared to every other realm, and the exception is expected to remain confined to Himinbjörg permanently. The escape hatch: the `[NorseWell]` overload naming an explicit context type binds a declared well to a hand-authored brownfield context instead of a generated one. Nothing else ever qualifies. Today the exception costs nothing — Himinbjörg declares no wells and wires through Urðarbrunnr's `AddNorseContext` (a legal chassis edge); the hatch exists so identity can participate the day it wants a read repository, without pretending to be greenfield. Per crooked-path meta-lesson #6: the wall's one exception is designed here, not slipped in later.

### 3.6 Enforcement — the build check

An MSBuild-level check in the scattered `Directory.Build.targets`: a `NorseRef` (or `NorseDesignRef`) with `Repo=Midgard` in any repo other than Yggdrasil fails the build with a real diagnostic naming this spec. Structural, at the layer where the sin is committed (the csproj), cheap to evaluate. This lands in Ginnungagap's scatter source and rides the normal scatter → merge → fan-out loop — scatter files are hands-off in realm repos by standing rule, so the change is made once, at the source. The domain-sliced Asgard law (§2) is documented doctrine for now; extending the same check to it is future work (§6).

## 4. Testing

- Generator tests: declaration discovery, entity-set discovery, duplicate-view diagnostic, zero-well diagnostic, brownfield-overload passthrough, emitted-context shape (file-scoped, sealed, configurations applied), closed registration emission. Emission via `CSharpEmit.AppendCSharp` house style throughout.
- Midgard keeps `Repository`/`WellMap`/`WellValidation` suites; the deleted reflection path's tests go with it.
- Yggdrasil's E2E country-lookup suite is the live proof: the real composition, generated context, migrated-and-seeded Testcontainers Postgres, gRPC round trip.
- The build check gets a fixture proving `Repo=Midgard` outside Yggdrasil fails and inside Yggdrasil passes.

## 5. Rollout notes

- **Mimir PR #17 (`feature/well-composition-isolation`) is superseded, not patched** — its Task 4 built further on the dependency this spec deletes. The well-composition plan's remaining tasks are reworked against both 2026-08-01 specs before anything else ships from that plan.
- An amendment note lands in `2026-08-01-well-composition-dbcontext-isolation-design.md` (§3.2 placed the call in Mimir; corrected the same day) — additive and dated, per the point-in-time record convention. §3.1's `AddNorseContextFactory` survives intact; the seam generator emits calls to it.
- Sequencing: Asgard (attribute) → Midgard (generator + deletions) → Mimisbrunnr (context re-homing, rides the companion spec's renames) → Mimir (shed Midgard/provider) → Yggdrasil (adopt `AddNorseWells`) → Ginnungagap (build check). Each behind its normal ship gate.

## 5a. Plan-time resolutions (2026-08-01, same day)

Recorded when the implementation plan (`../plans/2026-08-01-well-seam-midgard-excision.md`) was written against the real code:

- **§3.1's example key corrected to `norse_reference`** (underscore) — the spelling `[MigrationConnectionString]` and `Program.cs` already use; the hyphenated form in the first draft was illustrative and wrong.
- **§3.4's "internal registration core" is public in practice** — the generated code compiles in Yggdrasil's assembly, where an internal Midgard member is unnameable. `RegisterCore` ships as `public static RegisterWell<TContext, TEntity, TView>(IServiceCollection)`, XML-doc'd as generator-facing surface.
- **§3.2's "applies the same generated entity configurations" is achieved per-entity, not by reuse** — `GeneratedNorseModelConfigurations` is internal to the entity assembly, so the seam generator emits `builder.Entity<T>(eb => T.Configure(eb))` itself; identical `Configure` methods still make drift unrepresentable.
- **Urðarbrunnr joins the sequencing** (Asgard → **Urðarbrunnr** → Midgard → …): re-homing the design-time context requires `EntityConfigurationApplicationGenerator` to discover entities through the reference closure, not just its own syntax trees — plus an emission skip when the compiling assembly declares no partial `NorseDbContext` subclass.

## 6. Out of scope

- `WellMap.For`'s runtime reflection (the promotion map). A future pass can emit it from the same generator; it is startup-once and correct today.
- Per-well provider overrides in `AddNorseWells` — one provider per composition until a real need shows up.
- Extending the build check to the domain-sliced Asgard law and the broader `YGG003`/`YGG004` analyzer effort (`2026-05-19-architecture-analyzers-design.md`, still draft) — this check is deliberately narrow and MSBuild-shaped; the analyzer effort may absorb it later.
- Write-side repository contracts — unchanged, still future work at the same Asgard address.
