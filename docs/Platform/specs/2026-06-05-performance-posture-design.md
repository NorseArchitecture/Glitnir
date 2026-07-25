# Platform Performance Posture — Design

**Date:** 2026-06-05
**Status:** Active
**Amends:** CLAUDE.md §4/§5/§8 (queued — reconciliation items 2.18–2.20); cross-references the primitives spec §9/§12 (tier policy, benchmark machinery — that spec's instance of this law predates the law)

---

## 1. Purpose and Scope

The primitives spec built complete performance machinery for one submodule: BenchmarkDotNet with `[MemoryDiagnoser]`, zero-allocation targets per path, a committed baseline, nightly regression detection, AOT smoke tests. Nothing generalized it. Meanwhile three platform-wide goals existed only as intent — never written anywhere:

1. Logging is source-generated and allocation-free (`[LoggerMessage]`).
2. JSON serialization is source-generated and AOT-friendly (no reflection-based System.Text.Json).
3. Hot paths are allocation-free when feasible, and "feasible" is measured, not asserted.

This spec promotes Primitives's machinery to platform law and codifies the three goals with enforcement. The mission anchor already exists — "decisioning latency is a feature requirement" (CLAUDE.md §1) — this spec gives it mechanism.

**Out of scope:** numeric latency SLOs, throughput targets, and budgets. Those are Observability-era work (observability realm — renamed from Heimdall 2026-06-07); there is nothing to measure yet. This spec governs the *construction-time* posture: how code is built so that, when there is something to measure, the numbers are good and the regressions are visible.

---

## 2. Benchmark Project Convention

### 2.1 Placement and naming

One **`{Submodule}.Benchmarks`** project per submodule, under `benchmarks/` at submodule root, included in the submodule's `.slnx` — exactly the layout the primitives spec established (`benchmarks/Norse.Primitives.Benchmarks/`), promoted to convention. Per-assembly mirroring (the `.Tests` pattern) is deliberately **not** used: one suite covers the submodule's pipeline; locality below that is folder structure inside the suite.

### 2.2 Inclusion rule

A benchmark project is **required** when the submodule contains in-process, CPU-bound behavior on a hot path: parsing, dispatch, serialization, projection, materialization, computation.

A benchmark project is **exempt** when the submodule is:

- **Declaration-only** — contracts, attribute models, marker hierarchies. Declaring data structures needs no benchmark. (A source-generated `JsonSerializerContext` inside a contracts assembly does not break the exemption — generated serializers are benchmarked where they are *exercised*, at the door that uses them.)
- **IO-bound orchestration** — a BenchmarkDotNet run wrapping a database or broker call measures the database, not the code. IO-shaped work is measured by tracing (Observability), not microbenchmarks.
- **Build-time tooling** — analyzers and generators; if build time hurts, build telemetry is the instrument.

### 2.3 Inventory

| Submodule | Verdict | Rationale |
|---|---|---|
| `Norse.Primitives` | **Required (exists)** | Parsing, `Result<T>` composition — already specified (primitives §12.4) |
| `norse-abstractions-mediator` / `norse-infrastructure-mediator` | **Required** (one suite, in `norse-infrastructure-mediator`) | Dispatch pipeline, pre/post behaviors; the Abstractions side is declaration-only |
| `norse-infrastructure-persistence` | **Required** | Repository pipeline, entity materialization, snake_case/MaxLength conventions, `IConnectionResolver` resolution |
| `norse-infrastructure-api` | **Required** | JSON door: request/response serialization through the source-generated contexts |
| `norse-infrastructure-ui-composition` | **Required** | Widget registry resolution, layout composition, `WidgetContext` construction — the C# framework only (§3) |
| `Norse.ReferenceData` | **Required** | Temporal projection, as-of traversal, seed-engine parsing. **Amendment (2026-07-25):** this realm dissolved 2026-06-11 — temporal contracts moved to Asgard, implementations to Midgard. See `docs/codenames.md` and `docs/the-crooked-path.md` #8. |
| `norse-primitives-architecture` | Exempt | Build-time tooling |
| `norse-abstractions-hosting` / `norse-abstractions-infrastructure` / contracts assemblies | Exempt | Declaration-only |
| `norse-hosting` / `norse-hosting-host` | Exempt (boot path) | Startup cost is real but is a cold-start/container metric (§7), not a BenchmarkDotNet microbenchmark |
| `Norse.Warehouse` | Deferred | Batch ETL throughput is measured at the warehouse, not in-process; revisit when the Warehouse realm gets its spec |
| `{company}-{context}` | **Required where a computational core exists** | Underwriting risk scoring, Billing premium recognition, Reporting bordereaux assembly: yes. CRUD-shaped handler orchestration: exempt (IO-bound rule) |

### 2.4 Mechanics (generalized verbatim from the primitives spec §12.4)

- **BenchmarkDotNet + `[MemoryDiagnoser]` on every benchmark.** Allocation columns are not optional — the allocation regression signal is the primary product.
- **A baseline is committed to the submodule repo.**
- **Benchmarks never run per-PR.** Nightly job plus on-demand workflow dispatch.
- **Diff trigger:** any benchmark regressing **>10%**, or **allocating where it previously did not**, posts a diff report. New allocations on a previously allocation-free path are treated as seriously as a correctness regression — they get a named justification or they get reverted.
- Benchmark projects are not test projects: no Shouldly, no assertions, excluded from test discovery. The CI diff job is the gate.

---

## 3. Razor Components Are Not Benchmarked

The UI Composition framework's C# hot path (registry resolution, layout composition, context construction) is behavior-bearing and covered by §2. The `.razor` components themselves are not benchmarked:

- bUnit-under-BenchmarkDotNet measures **host-JIT render time** — a number that does not transfer to WASM or circuit reality. Optimizing against an unrepresentative target is worse than not measuring.
- Real Blazor performance failures are **re-render frequency** — parameter churn, missing `ShouldRender` — which is a correctness/test concern, not a microbenchmark one.

**Re-entry trigger:** measurable render lag in a Shell dashboard. The instrument at that point is browser-based measurement (DevTools profiling, WASM runtime metrics), not BenchmarkDotNet. Until the trigger fires, no component benchmark suites exist.

---

## 4. Logging Law

This decision was made conversationally and never written down. It is now written.

### 4.1 The law

**`[LoggerMessage]` source-generated partial methods are the only logging surface.** No interpolated `LogInformation($"...")`, no `LogError("literal " + var)`, no params-array template calls — anywhere, any tier. Source-generated log methods are allocation-free below the enabled level and allocation-minimal above it; interpolated calls box, allocate, and evaluate arguments even when the level is off.

### 4.2 Placement — co-located by default

- **Log methods live in the class that calls them**, as `private static partial` methods on that class. The log statement reads next to the logic it describes; a human tracing behavior never leaves the file.
- **Promotion rule:** the moment a second class needs the same log method, it moves to an assembly-level `internal static partial class Log`. Co-location is for single-caller methods; shared methods get the shared home. No copy-paste of log methods between classes.
- **The `partial` consequence is sanctioned.** Co-located `[LoggerMessage]` methods force the declaring class `partial`. This is the documented generator demand that CLAUDE.md §8 requires before `partial` is permitted — this spec is the documentation. The exception covers exactly this: a class is `partial` because it declares `[LoggerMessage]` methods, and for no other reason.

### 4.3 EventIds — central, explicit, permanent

- Each assembly declares **one `internal static class LogEvents`** holding `internal const int` values — the single declaration location for every EventId in the assembly.
- EventIds follow the enum law (CLAUDE.md §5): **explicit, unique within the assembly, and never reused** once they have shipped. EventIds end up persisted in Observability's log store; renumbering is a silent data-corruption event in query-land, exactly like reordering a persisted enum.
- `[LoggerMessage(LogEvents.PolicyBound, LogLevel.Information, "...")]` — the attribute references the constant; no inline numeric literals at the call site.

### 4.4 Enforcement

Existing analyzers ratcheted to error, platform-wide — no new YGG rule (ReferenceData principle: never write a YGG rule where an existing mechanism will do):

| Rule | Meaning |
|---|---|
| **CA1848** | Use the LoggerMessage delegates (source-generated path) — error |
| **CA2254** | Template must be a static expression (no interpolation/concatenation) — error |
| **CA1727** | PascalCase log template placeholders — error |

---

## 5. JSON Law

### 5.1 The law

**System.Text.Json source generation is the only serialization path.** Reflection-based `JsonSerializer` use is disabled platform-wide:

- **`<JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>`** in the root `Directory.Build.props`. Any reflection-based serialization attempt throws at first use — fail loud, fail at startup-or-first-request, never silently fall back to the reflection resolver.
- Every serialized shape is registered in a **`JsonSerializerContext`**. Contexts live **with the shapes they serialize** (a context over `{Company}.Billing.Contracts` request/response shapes lives in that assembly; generated code does not break declaration-only status).

This is already implied in two places — the messaging spec mandates System.Text.Json platform-wide (§4.5 there) and the UI Composition spec states "System.Text.Json source generators handle serialization" — but no spec carried the feature switch or the context-placement rule until now.

### 5.2 Known integration points (specced, not discovered)

- **NServiceBus:** the hosting runtime wires NSB's System.Text.Json serializer to the platform's resolver chain (combined `JsonTypeInfoResolver` over the contexts of every loaded plugin's message assemblies) — declared once, in the same place the unobtrusive-mode conventions are declared (messaging spec §4.5). A message type missing from every registered context is a **startup failure**, not a first-publish surprise, wherever the resolver chain can be verified against the plugin's declared handler/saga set.
- **Mongo** is BSON (its own driver serialization) and **gRPC** is protobuf — both unaffected by the feature switch.
- **Third-party code that internally requires reflection-based STJ** is a dependency-selection criterion from now on (§7.3): prefer libraries that accept a `JsonSerializerOptions`/resolver, or wrap them at a boundary that supplies one.

---

## 6. Allocation Posture

### 6.1 Hot paths, enumerated

"Hot path" is a defined list, not a vibe. Per-occurrence cost on these paths multiplies by message volume:

1. Per-message handler pipeline (NSB behaviors, mediator dispatch, handler bodies)
2. Per-request middleware and the JSON door (serialization in/out)
3. Parsing (`SpanParser<T>`, BDX-shaped ingest, seed-engine TSV)
4. Logging (§4 makes this structurally allocation-free)
5. Widget registry resolution and layout composition
6. Repository materialization and temporal projection

### 6.2 The rule

- **Zero allocations on success paths of platform primitives — measured.** `[MemoryDiagnoser]` is the instrument; the §2.4 new-allocation diff trigger is the enforcement.
- **"When feasible" means: a new allocation on a hot path must survive the benchmark diff review** — it gets a named justification in the diff discussion or it gets reverted. It does not mean "forbidden outright," and it does not mean "nobody looked."
- Spans at parse boundaries (`ReadOnlySpan<char>` ingress — already law via the primitives spec) and no per-request reflection (already law, CLAUDE.md §8). This section consolidates; it does not invent.
- Off the enumerated hot paths, idiomatic allocation is fine. This is not a `stackalloc`-everything culture; it is a measured-hot-path culture.

---

## 7. AOT End State

### 7.1 The goal

**The ideal state is every deployable published Native AOT** — including `Norse.Hosting.Web.Server` (Web.Server surface) and the worker endpoints — so a container hotstarts in milliseconds without JIT warmup or reflection-driven startup cost. Reflection had its time and place when it was the norm; those days belong in the rearview. **Reflection at build time — source generators, interceptors, compile-time DI wiring — is the way.**

The primitives tier policy (primitives spec §9) stands as today's posture: library tier `IsAotCompatible`/`IsTrimmable`, CLI and clients `PublishAot=true`, server tier JIT. But the server tier's JIT status is reframed by this spec: it is a **temporary concession to named blockers, not a stance.**

### 7.2 Blocker register

Each blocker is tracked with a re-check trigger; the standing platform calendar trigger is **.NET 11 RC1 (≈Sept 2026)** — the same trigger already carrying the PG19 re-verification and the primitives union-syntax re-pin.

| Blocker | Blocks | State |
|---|---|---|
| ASP.NET Core MVC controllers | `JsonControllerBase<TService>` (norse-infrastructure-api JSON door) | MVC is not AOT-compatible; minimal-API + source-gen route is the likely exit — re-evaluate at RC1 |
| Blazor interactive server circuits | `Norse.Hosting.Web.Server` Blazor Web App surface | Circuit runtime not AOT-rated; re-check each preview |
| NServiceBus | Every worker endpoint | Already a tracked "v11 trajectory bet" (CLAUDE.md §4 → Messaging); this register is where it reports |
| EF Core Native AOT maturity | `.Worker` system-of-record tier | Compiled models + precompiled queries are the path; verify coverage at EF Core 11 RC1 |

When a blocker clears, the affected deployable flips to `PublishAot=true` — and because of §7.3, the flip is a csproj property change, not a rewrite.

### 7.3 The rule with teeth: no new blockers

- **Everything is written AOT-clean now, including JIT-tier code.** No reflection-dependent patterns, no trim-unsafe constructs, no dynamic code generation — even where today's deployable would tolerate them.
- **Introducing an AOT-incompatible dependency or pattern requires a documented exception** in the spec that introduces it, with a stated exit path — the same discipline as every other §7-style open question. Silent accrual of new blockers is how platforms get permanently stuck on JIT.
- AOT compatibility is a **dependency-selection criterion** with the same weight as license compatibility.

### 7.4 Smoke checks

Every deployable that publishes AOT keeps a CI publish smoke check, per the primitives `.Aot.SmokeTests` pattern (primitives §12.3): publish with `PublishAot=true`, fail on trim/AOT warnings, run a minimal execution probe. As blockers clear and deployables flip, they enter this gate.

---

## 8. Mechanical Tail (Queued Amendments)

Queued to `docs/spec-reconciliation-2026-06-04.md` as items 2.18–2.20; they ride the existing mechanical-pass train.

1. **CLAUDE.md (item 2.18):**
   - §4 gains the logging law (§4 here) and JSON law (§5 here) — short statements with a pointer to this spec.
   - §4 "AOT-clean where feasible" strengthens to: AOT is the end state for every deployable; AOT-clean is mandatory now; blockers are named and tracked (§7 here).
   - §5 gains the `{Submodule}.Benchmarks` naming convention alongside the `.Tests` convention.
   - §8 gains two anti-patterns: **No string-interpolated logging** (CA1848/CA2254 enforce) and **No reflection-based JSON serialization** (feature switch enforces); plus **No new AOT blockers without a documented exception**.
2. **Hosting spec (item 2.19):** the NSB serializer resolver-chain wiring (§5.2 here) lands where the unobtrusive-mode/serializer configuration is specified; rides the 2.2/2.3 hosting amendment pass.
3. **Primitives spec (item 2.20):** §12.4 gains a one-line note that its machinery is now an instance of platform law (this spec); §9 tier policy gains the §7.1 end-state framing (server-tier JIT = temporary concession).

The CA ratchets (§4.4) and the JSON feature switch (§5.1) are `.editorconfig` / root `Directory.Build.props` mechanics — they land with the meta-repo build-infrastructure work already planned (primitives plan task 2), not as a separate workstream.

---

## 9. Acceptance Criteria

1. Every submodule in the §2.3 inventory marked **Required** ships a `{Submodule}.Benchmarks` project with `[MemoryDiagnoser]`, a committed baseline, and the nightly + dispatch CI job before its implementation plan is declared complete.
2. CA1848, CA2254, CA1727 are errors platform-wide; a `LogInformation($"...")` call fails the build.
3. `JsonSerializerIsReflectionEnabledByDefault=false` is set in the root `Directory.Build.props`; a reflection-based serialization attempt throws.
4. Each assembly that logs declares `LogEvents`; no inline EventId literals at `[LoggerMessage]` call sites.
5. The §7.2 blocker register is re-checked at .NET 11 RC1 alongside the PG19 and union-syntax re-pins (one calendar trigger, three checks).
6. No spec written after this date introduces an AOT-incompatible dependency without a documented exception and exit path.
