# The Law of the Realms — Compiler-Enforced Realm Dependency Law

**Status:** Ratified 2026-08-03 · **Realm:** Svartálfheim (authoring), platform-wide (jurisdiction)
**Supersedes:** the paper-only `YGG003`/`YGG004` rules of `2026-05-19-architecture-analyzers-design.md` (never implemented; their intent is absorbed here as NORSE071/NORSE072). The remaining YGG-numbered visions in that spec (`YGG101`, `YGG301`, …) stay future work and are unaffected.

## 0. Why This Exists

On 2026-08-03, `System.Text.Json` machinery (`MaskedValueJsonConverter<T>` + `[JsonConverter]` attributes on PII structs) reached Svartálfheim — through a ratified spec, a Forseti review, a written plan, a pre-flight plan scan, an implementer, and a task reviewer. Six advisory layers, zero catches; the human caught it live. A concurrent sweep found a second, older leak nobody knew about (`System.Text.Json` in Himinbjörg's Identity endpoints). The empirical result: **prose law does not hold under momentum — any prose, anyone's, no matter how many copies.** Every layer reads law as context; only the compiler executes it as a gate.

This spec converts the platform's standing dependency doctrine into build errors. A ruling is codified once, as law the build executes, and is never re-litigated — not in a spec review, not by a subagent, not at 2 a.m.

## 1. The Statutes

### Law #1 — Wire format never leaves Midgard/Yggdrasil

Anything **explicitly wire-format** — any symbol that *names or executes a concrete encoding* — exists only in Midgard (`Norse.Infrastructure.*`) and Yggdrasil (`Norse.Hosting.*`). JSON, XML, protobuf, all of it. Full stop.

**The razor:** WCF contract attributes (`[DataContract]`, `[DataMember]`, `[ServiceContract]`, `[OperationContract]`) are **blessed everywhere** — they are declarations of *intent*, not encoding directives. `[DataContract]` says "this shape crosses a boundary"; it commits to no bytes. Midgard alone decides what bytes that becomes (protobuf today, JSON at the REST edge, whatever tomorrow needs) without the declaring realm changing a line. By opting into the attributes, a realm gets everything downstream for free. `JsonConverter` names an encoding; `XmlWriter` names an encoding; the protobuf runtime names an encoding — those are wire format. The attributes name none — those are contracts.

### Law #2 — Midgard is consumed by the tree alone

Midgard inherits upward from the foundation realms; **no realm takes Midgard as a dependency except Yggdrasil.** Midgard publishes no surface — no `.Contracts`, no `.Services` — so no legal door into it exists. The composition root is what the world tree *is*; that is the whole exemption.

### Law #3 — Components are platform-free RCLs on contract seams

Blazor components live in realm `.Components` RCLs and consume gRPC service contracts (`I{Context}Service`), so one component runs under MAUI, Blazor Server (WebSocket), and WASM (gRPC-Web) as a pure deployment choice. The enforceable edge is dependency purity: a `.Components` assembly may not reference server-side machinery **even within its own realm** — no `.EntityFramework`, no `.Web.Server`, ever.

### Law #4 — Realms are bounded contexts; the only doors are published surfaces

Cross-realm reach goes through published surfaces alone. Mímisbrunnr never sees Himinbjörg's innards, nor vice versa. You inherit from the abstraction providers and you are good; you inherit from anything else — unless it published an event or a client — yeah nah.

## 2. The Reference Formula

A Norse-assembly reference is **legal iff at least one** of:

1. **Target is foundation.** `{Brand}.Primitives.*` (Svartálfheim — blessed by the Æsir, rides the rail), `{Brand}.Abstractions.*` (Asgard), `{Brand}.Persistence.*` (Urðarbrunnr), `{Brand}.Messaging.*` (Ratatoskr). Plus `{Brand}.DesignSystem.Tokens` (Naglfar's 100%-generated token seed — data, not machinery).
2. **Target is your own realm family** (same functional family segment — `Identity.Web.Server` → `Identity.EntityFramework`; also auto-blesses Mímir → Mímisbrunnr, both `Reference.*`).
3. **Target is a published surface**: assembly name ends `.Contracts` (events), `.Services` (gRPC client seam), or `.Components` (UI — Bragi and hosts consume these) — **or contains the `.Components.` segment: vendor drops (`.Components.FluentUI`) are part of the published component surface (ruled 2026-08-03, final-review pass: "FluentUI will always be part of the dependency tree, especially in Bragi — no way around that"; supersedes the earlier not-a-surface pin).** The NORSE073 purity stricture still keys on the exact `.Components` suffix — a vendor drop is a legal *target* everywhere but is itself governed by the general formula, not the stricture.
4. **Source is Yggdrasil** (`{Brand}.Hosting.*`) — the tree composes everything.

**Foundation internal ordering (stricter than the formula):**

- **Svartálfheim references no Norse assembly outside its own family.** The forge sits under everything (`Primitives.Ingestion` → `Primitives` stays a family matter).
- **Asgard references only Svartálfheim (and its own family).**
- Urðarbrunnr and Ratatoskr follow the general formula (foundation + own family).

**Midgard has no published surface** — rule 3 can never admit it; only rule 4 (the tree) reaches it. `.Components` assemblies are additionally restricted to rules 1 + 3 only (Law #3): no rule-2 own-realm server references. **Except the tree's own (ruled 2026-08-03, discovered live by Task 8's verification):** rule 4 precedes the component stricture — "Hosting is *the* composition root; it should be able to pull in whatever it wants." A `{Brand}.Hosting.*.Components` assembly is still the tree; NORSE073 governs realm component RCLs, never Yggdrasil's own (the strike this ruling resolves: `Hosting.Web.Components` → `Reference.Data.Primitives`, Mímisbrunnr's generated WASM-lean surface, transitively via `Reference.Contracts`).

**Precedence and evaluation order (ruled 2026-08-03, Forseti pre-flight):** the formula's arms are not co-equal. NORSE071's target check (`{Brand}.Infrastructure.*`) evaluates **first and wins over every arm** — a hypothetical `Norse.Infrastructure.Contracts` is not a legal door through the `.Contracts` suffix; "Midgard publishes no surface" is enforced, not descriptive. The Svartálfheim/Asgard foundation ordering likewise **replaces** the general formula for those source assemblies rather than adding to it. Both carry guilty fixtures: `Infrastructure.Contracts` referenced from a realm must strike; Asgard referencing Urðarbrunnr must strike.

## 3. Jurisdiction From Names Alone

The analyzer derives everything from what the compiler already sees — **zero new plumbing** (no `CompilerVisibleProperty` — deliberately retired platform-wide 2026-07-27 — no `.globalconfig`, no per-realm config to drift):

- **Who am I:** `compilation.AssemblyName`, parsed as `{Brand}.{Function}[...]`. Matching is **brand-agnostic** — the law keys on the functional segments after the brand prefix, so a fork that rebrands `Norse` → `Acme` in `Directory.Build.props` stays fully governed.
- **What do I reference:** `Compilation.ReferencedAssemblyNames` — identical for NuGet packages and NorseRef dev-mode `ProjectReference`s.
- **What do I use:** namespace/symbol usage for Law #1.

**Brand-boundary parsing (ruled 2026-08-03, Forseti pre-flight):** the function vocabulary — `Primitives`, `Abstractions`, `Persistence`, `Messaging`, `Infrastructure`, `Hosting`, `DesignSystem` — is itself law, living in the analyzer; the brand is everything before the first recognized segment (handles multi-segment brands: `Acme.Corp.Primitives`). A name containing no vocabulary segment is a realm-family assembly (`Norse.Identity.Web.Server`): its brand resolves from the referenced governed assemblies' anchors (every realm assembly rides the rails, so at least one reference carries a vocabulary segment whose prefix prefixes the current name), and its family is the segment immediately after the brand — realm families are **inferred, never enumerated**, so onboarding a realm needs no analyzer release. A reference is Norse-governed iff it shares the current compilation's brand prefix; **cross-brand references are ungoverned by NORSE072** — deliberate and recorded.

**NORSE070 is brand-blind (ruled 2026-08-03, second pre-flight):** Law #1 evaluates on the function segments of `compilation.AssemblyName` alone — if no segment is `Infrastructure` or `Hosting`, the banned set applies, whether or not a brand was resolved. Brand resolution failure never exempts an assembly from Law #1. This closes the fail-open: a pure `.Contracts` assembly holding only `[DataContract]` records may reference no governed assembly at all (the razor encourages exactly this shape), leaving no anchor to infer a brand from — it remains fully under Law #1 regardless. Brand resolution is required only by NORSE071/072/073, where an assembly with zero governed references has nothing for those strikes to convict anyway.

**Transitive reference semantics (ruled 2026-08-03, Forseti pre-flight):** `ReferencedAssemblyNames` includes transitively-flowing compile assets, so NORSE071/072 convict assemblies whose own `.csproj` is innocent. This is **correct law** — a transitive dependency is still a dependency. The diagnostic reports at `Location.None` (there is no syntax to point at) and its message must name the source assembly, the offending target, and the formula arms that failed, so the first transitive strike costs a glance, not an hour over a clean project file.

The naming model (function names, build-injected brand, one `Directory.Build.props` per realm) is already law; this design makes it the jurisdiction map.

**Exempt assemblies:** `*.Tests`, `*.Benchmarks`, `*.Aot.Smoke` — the law governs shipped architecture, not evidence rigs. **`*.Analyzers`/`*.Generators` (gen/ assemblies) are exempt too (ruled 2026-08-03):** they execute inside the compiler and ship as build-time tooling (`analyzers/dotnet/cs`), never as runtime architecture — a generator parsing JSON from `AdditionalFiles` is reading its config, not putting an encoding on a wire.

## 4. The Strikes — NORSE070–073

Minted in the forge's `Diagnostics.cs` header ledger (NORSE060-069 block is Svartálfheim's; 070-079 now claimed for architecture law — grep confirmed clean at authoring). All four: `DiagnosticSeverity.Error`, `isEnabledByDefault: true`, tagged `NotConfigurable` — severity cannot be downgraded by a consuming realm.

| ID | Strike | Rule |
|---|---|---|
| NORSE070 | Wire format outside the border | Banned wire namespace/symbol used in an assembly whose function segment is not `Infrastructure`/`Hosting` |
| NORSE071 | Midgard taken as a dependency | `{Brand}.Infrastructure.*` in the reference list of a non-`Hosting`, non-`Infrastructure` assembly |
| NORSE072 | Cross-realm reach | Norse-assembly reference failing every arm of the §2 formula (incl. the Svartálfheim/Asgard foundation ordering) |
| NORSE073 | Component impurity | `*.Components` assembly referencing anything outside foundation + published surfaces |
| NORSE079 | Suppressing the law | Any `[SuppressMessage]` whose checkId names a `NORSE07x` rule — suppressing the law is a violation of the law (ruled 2026-08-03, final-review pass) |

**NORSE079 — the meta-strike.** `NotConfigurable` closes the severity channel, but `SuppressMessageAttribute` rides a different one and erases any strike entirely, `Location.None` reports included (verified live at final review — the exact 2 a.m. escape §0 exists to prevent). No reporting mechanism defeats attribute suppression, so the law convicts the attempt instead: a syntactic check on every `[SuppressMessage]` (any target level, assembly included) whose checkId argument matches `NORSE07`-prefixed rules reports NORSE079 at the attribute's location — and because the check is syntactic, suppressing NORSE079 itself is just another matching attribute, re-convicted recursively. The §7 fixture matrix covers both mechanisms (`#pragma`, `[SuppressMessage]`) against the strikes.

### NORSE070 banned set (v1 — one list, one place, extended by ruling, never re-litigated)

- **Namespaces (wholesale):** `System.Text.Json` (all), `Newtonsoft.Json` (all), `System.Xml` (all — deny-by-default on the entire XML surface; an exemption requires a named concrete need and lands as a recorded amendment), `System.Runtime.Serialization.Json`, `System.Net.Http.Json` (`GetFromJsonAsync`/`PostAsJsonAsync` — the idiomatic leak shape the Himinbjörg conviction took), `Microsoft.AspNetCore.Http.Json`, `ProtoBuf` (protobuf-net runtime), `Grpc`, `Google.Protobuf`, `MessagePack`.
- **Symbols (namespace is mixed):** in `System.Runtime.Serialization` — the serializer machinery (`DataContractSerializer`, `XmlObjectSerializer`, friends) is banned; the contract attributes are blessed and untouched. `System.ServiceModel` attributes: blessed. `Microsoft.AspNetCore.Http.Results.Json` and `TypedResults.Json` — banned as symbols (their namespaces are innocent; the members name an encoding).
- External wire packages are caught by namespace even though NORSE072 governs only `{Brand}.*` references.
- **Document-model ruling (ratified 2026-08-03; conviction audit run same day — result recorded in §6):** `JsonDocument`/`JsonElement`/`JsonNode` are *not* pre-exempted as "data-shape" types — deny-by-default holds. The audit verified the ban convicts **zero** persistence/Worker/JSONB code today: every `ToJson()`/`HasColumnType("jsonb")` site (Urðarbrunnr, Himinbjörg and Mímisbrunnr migrations) is EF's own owned-entity mapping API with no user code touching banned symbols — the serialization happens inside EF, invisible to the law. A future JSONB write path needing the document model directly petitions with a named concrete need at that time — relocate behind a Midgard seam or land a recorded type-level exemption then, on evidence.

## 5. Delivery — Aggressively Upstream, No Opt-Out

- **Project:** new analyzer project in Svartálfheim `gen/` (proposed name `Architecture.Analyzers`, assembly `Norse.Architecture.Analyzers`), netstandard2.0, `IsRoslynComponent`, mirroring `Primitives.Analyzers`' scaffold — but **packable standalone** (`Norse.Architecture.Analyzers` NuGet, `analyzers/dotnet/cs`), because no-opt-out delivery cannot ride inside a host package: attachment would be contingent on the host reference existing. (Corrected 2026-08-03 — this line previously claimed Asgard "references nothing else"; false: Asgard NorseRefs `Norse.Primitives`, per its own csproj files and the §2 foundation ordering. Svartálfheim is the base domain even for the Æsir — but the law leans on no voluntary dependency, today's or tomorrow's.)
- **SDK implicit-usings hazard (ruled 2026-08-03, final-review pass; carrier corrected same day):** the .NET 11 SDK injects `global using System.Net.Http.Json;` into every project's generated `GlobalUsings.g.cs` — a banned root that would convict every governed assembly (the forge included) the moment the law attaches. The `<Using Remove="System.Net.Http.Json" />` must live at **targets evaluation time** — a props-level Remove evaluates before the SDK's own Include and is a no-op (proven empirically during Task 6 verification). Carriers: the scattered `src/`/`tests/`/`gen/` `Directory.Build.targets` (standalone/CI mode) and Bifröst's root `Directory.Build.targets` (workspace mode). An *authored* using still convicts (the tripwire stays meaningful); border realms re-add locally where it is legal.
- **Distribution:** the Ginnungagap-scattered `Directory.Build.props` (`dotnet` group — already reaches every realm, already not the realm's to edit) gains the analyzer reference: workspace mode resolves a `ProjectReference` (`OutputItemType="Analyzer"`) into the forge via the proven NorseRef `Choose` pattern; standalone/CI mode resolves the NuGet package. A new realm inherits the law at scatter time, before its first line of code.
- **Self-jurisdiction:** the analyzer runs on Svartálfheim itself (the `Primitives.Analyzers` precedent) — the forge is governed by its own law.

## 6. Day-One Convictions (all known, from the 2026-08-03 sweep)

1. `Svartalfheim/src/Primitives/Pii/MaskedValueJsonConverter.cs` — deleted (branch `feature/pii-primitives`); the masked-serialization defense relocates to Midgard's existing `Infrastructure.Web.Server/Json/` pipeline as an `IMaskedValue`-aware converter beside `LexicalJsonConverters`, registered once in `MvcBuilderExtensions`.
2. `Svartalfheim/src/Primitives/Pii/EmailAddress.cs` — `[JsonConverter]` attribute + `using System.Text.Json.Serialization;` stripped.
3. `Himinbjorg/src/Identity.Web.Server/IdentityComponentsEndpointRouteBuilderExtensions.cs` — `JsonSerializer.SerializeToUtf8Bytes` in the scaffolded personal-data-download endpoint; the pre-existing leak prose law never caught. Remediation folds into the PII disclosure-surface work (that endpoint *is* the disclosure concern, currently hand-rolling its own wire bytes).

Each conviction becomes a permanent regression fixture in the analyzer tests. The full-banned-set audit (2026-08-03, second pass — document-model types, `System.Net.Http.Json`, XML surface, protobuf/gRPC/MessagePack, package references) confirmed every other realm clean **for Law #1 authored source**. The final whole-branch review (same day) extended the audit to Laws #2/#4 over real project files and corrected the record — two additions:

4. `Mimir/src/Reference.Web.Server` NorseRefs `Infrastructure.Persistence.EntityFramework` — **NORSE071, the postmortem shape, live on master.** Ruled 2026-08-03: genuine remediation, not a door — the Midgard reference moves to the composition root (Yggdrasil registers the persistence implementations; Mímir consumes the Asgard read-contract seam).
5. The SDK implicit-usings systemic conviction (§5) — resolved by the scattered `<Using Remove>`, not per-realm code.

(The Himinbjörg and Bragi `AuthN.Components.FluentUI` references the same review flagged are **not** convictions under the vendor-drop door ruling in §2 — the blessed drop-in pattern stands.) Ratatoskr is a bare shell today (zero code), so its Law #1 posture is enforced from birth: when the messaging realm is built out, NServiceBus serializer configuration (`UseSerialization` and friends) is wire machinery and lands in Midgard/Yggdrasil, never in `{Brand}.Messaging.*` — the §7 standing fixture holds the line from the first commit.

## 7. Testing

`AnalyzerTestHarness` precedent (compile-clean-first, stub sources, metadata-name resolution). Per strike: guilty fixture + innocent fixture + the razor's edge cases (NORSE070: `[DataContract]` record in a `.Services`-shaped compilation is clean; `JsonSerializer.Serialize` in a `Primitives`-shaped compilation strikes; the same call in an `Infrastructure`-shaped compilation is clean). NORSE072: each formula arm proven both directions, including Svartálfheim-references-nothing-foreign, Asgard-references-only-the-forge, and the precedence fixtures (`Infrastructure.Contracts` from a realm strikes; Asgard → Urðarbrunnr strikes). **Suppression-proofing (ruled 2026-08-03):** one fixture per strike proving `#pragma warning disable` and `[SuppressMessage]` do not dodge a `NotConfigurable` error — if pragmas turn out to pierce it, we learn it in the harness, not in a 2 a.m. subagent commit; the plan then adds the compilation-end backstop before shipping. Ratatoskr gets a standing verification fixture: its assemblies must satisfy NORSE070 with zero exemptions — contracts and dispatch abstractions only, MCP/JSON-RPC wire handling lives in Midgard. **Brand-blind fixtures:** an anchorless Contracts-shaped compilation (no governed references) containing `JsonSerializer.Serialize` strikes NORSE070; the same compilation containing only `[DataContract]`/`[DataMember]` is clean. Innocent fixture: `Norse.Architecture.Analyzers` itself — no vocabulary segment, would infer "Architecture" as family, exempt via `*.Analyzers` before inference ever runs. The ultimate integration proof is the platform itself: every realm builds green under the law after the §6 remediations.

## 8. Sequencing

1. Analyzer ships from Svartálfheim (normal ship gate: PR → CI → tag → publish). **Bootstrap ordering:** the package must exist on the feed *before* the scatter lands — once the scattered `Directory.Build.props` reaches Svartálfheim itself, the forge's standalone/CI mode resolves its own NuGet package, so forge CI builds workspace-mode (or with a one-time bootstrap escape) for the very first publish only.
2. Ginnungagap scatter change lands the reference platform-wide.
3. The three §6 convictions are remediated as the law reaches their realms.
4. The halted PII effort (`2026-08-03-pii-primitives-identity-erasure-seam` plan) resumes, amended to the new world: converter tasks deleted from the forge phase, masked-JSON defense re-planned into the Midgard phase.

## 9. Deliberately Out of Scope

- `YGG101` (no bare `string` on message types), `YGG301` (`ProjectReference` in `<Target>`), and the rest of the 2026-05-19 vision — future statutes, same delivery rail once written.
- MSBuild BuildCheck — still experimental, zero platform precedent, and blind to Law #1 (framework assemblies are referenced implicitly); revisit when the API stabilizes.
- Runtime/reflection-based enforcement of any kind — the entire point is compile time.
