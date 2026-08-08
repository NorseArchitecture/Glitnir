# house-rules.md — Settled Law of the Hall

Glitnir is where disputes end. Every rule below is a dispute that has already been
heard, argued, and judged — treat them as precedent, not suggestions. If a rule seems
wrong for a specific situation, raise it in the plan and let the judge rule; do not
silently deviate.

## When to read this document

Read this file **in full before writing any plan**. That is the only mandatory
reading point.

- **Systematic debugging:** not needed — fix the code.
- **Brainstorming / spec work:** not needed — ideas are pre-law.
- **Plan execution (subagent-driven development):** not needed — the plan already
  encodes the law.

## Platform baseline

- **.NET 11 preview 6, C# 15.** Every language feature is on the table, including
  preview features.
- Prefer **modern idiomatic C# over legacy C#**.
- Prefer **performance and readability over convenience**. When two modern forms
  compete, the terser one wins unless it costs clarity.

## Naming

- **Purely Microsoft standards** — the canonical reference is
  <https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names>.
  No house dialect: `_camelCase` fields, `I`-prefixed interfaces, PascalCase
  members, `Async`-suffixed async methods.
- The one sanctioned deviation: **test method names** are sentence-shaped with
  underscores (see Tests).

## Construction and collections

- **Target-typed `new()` whenever the language allows it** — even when the type is
  not apparent at the callsite. Less code is more.
  - `DataTable dt = new();` — never `var dt = new DataTable();`
  - `context.Set<MyObject>().Add(new() { ... });` — correct even though the type
    is inferred through the generic.
- **Collection expressions everywhere:**
  - `IList<int> vals = [];`
  - `int[] vals = [.. myEnumerable];` — over `myEnumerable.ToArray()`
- **Range indexers over `Substring`.** `s[2..5]`, `s[^4..]`, never `s.Substring(...)`.
- **Everywhere else, `var` is not just sanctioned but preferred.**
  `var val = obj.Function();` is the house form. `var` yields in exactly two
  buckets, both above: a constructor call (target-typed `new()` wins) and a
  collection materialization (collection expressions win). There is no third
  bucket.
- **Tuple deconstruction when possible:** `var (code, name) = GetPair();` over
  pulling `.Item1`/`.Item2` or intermediate variables.
- **Prefer primary constructors whenever applicable.** If a constructor exists
  only to capture its parameters — stashing them for the members to use, or
  forwarding them to a base — the primary-constructor form wins and the
  hand-written constructor goes:

  ```csharp
  // Old (bad)
  sealed class TestContributor : EfMigrationContributor<TestContext>
  {
      public TestContributor(TestContext ctx) : base(ctx) { }
  }

  // New (good)
  sealed class TestContributor(TestContext ctx) : EfMigrationContributor<TestContext>(ctx);
  ```

  A constructor earns its body only when it genuinely does work (validation,
  transformation, conditional wiring) that a primary constructor cannot express.
- **Fold repeated same-type declarations into one multi-declarator statement.**
  Two or more consecutive locals (or fields) of the same type declare the type
  once — each declarator on its own line, indented:

  ```csharp
  // Old (bad)
  SequentialGuid first = new();
  SequentialGuid second = new();

  // New (good)
  SequentialGuid
      first = new(),
      second = new();
  ```

## Strings

- **String concatenation is banned. Ever.** String interpolation for the simple
  case; `StringBuilder` for loops and accumulation. Choose based on the case, but
  `+` between strings is never the answer.

## Usings and namespaces

- **Always hoist namespaces into the `using` section at the top of the file** —
  even for a type used exactly once. Inline fully-qualified names are banned in
  hand-written source:

  ```csharp
  // Old (bad)
  MetadataReference.CreateFromFile(typeof(System.ServiceModel.ServiceContractAttribute).Assembly.Location),

  // New (good) — using System.ServiceModel; at the top
  MetadataReference.CreateFromFile(typeof(ServiceContractAttribute).Assembly.Location),
  ```

- **Carve-out: emitted code fully-qualifies, per industry standard for generated
  source.** Generator templates can't assume what usings or colliding type names
  exist at the consumption site, so generated output fully-qualifies inline
  (`global::`-prefixed where ambiguity is possible) — exactly as the gateway
  exemplar below does. The hoisting law governs the source you write, not the
  source you emit.

## Expression bodies

- **Prefer expression-bodied members when possible.**
- **Formatting: the arrow stays on the declaration line; the body goes on the
  following line, indented** — so a breakpoint lands cleanly on just the
  expression in question:

  ```csharp
  public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
      GlobalOptions;
  ```

## Control flow

- **Always remove redundant `else` statements.** If the `if` branch returns
  (or throws, or continues), the `else` is dead weight — drop it and dedent.
- **Prefer a ternary over `if (condition) return a; else return b;`** whenever
  possible.
- **Ternary format:** condition on the declaration line ending with `?`, true
  value on the next line ending with `:`, false value on the line after ending
  with the terminator — each indented:

  ```csharp
  return condition ?
      trueValue :
      falseValue;
  ```

  Nested ternaries chop the same way, each level indented one further — and
  nesting caps at three conditions. Past three, the chain is a readability
  crime; it becomes a `switch` expression, which reads flat at any branch
  count and brings compiler exhaustiveness a ternary chain never has.

- **Nesting ternaries is fine — keep indenting one level per nest, and never
  nest more than 3 in total:**

  ```csharp
  return status is Status.Active ?
      HandleActive() :
      status is Status.Pending ?
          HandlePending() :
          HandleClosed();
  ```

  Past three, it's a `switch` expression's job.

## Null handling

- **Pattern matching for null checks:** `is null` / `is not null` — never
  `== null` / `!= null`.
- **Exception: `Nullable<T>` value types prefer `.HasValue`** (and `.Value` /
  `GetValueOrDefault()` for access).
- **The null-forgiving operator `!` has one sanctioned idiom** — `= null!;`
  defaults on EF reference navigations (see Entities below; scalars and owned
  view documents use `required` instead). Outside that idiom, reaching for `!`
  to silence a nullability warning is the null-world cousin of a lazy
  `<NoWarn>`: fix the flow instead, or justify the `!` in a comment.

## Fluent APIs

- **When a fluent API is available, chain it.** Repeated statement-per-call
  invocation of a fluent API is banned:

  ```csharp
  // Old (bad) — it has been seen, and it has already been lost over
  services.FunctionA();
  services.FunctionB();
  services.FunctionC();

  // New (good)
  services
      .FunctionA()
      .FunctionB()
      .FunctionC();
  ```

- **Chain format:** receiver on its own line, each call on its own line below,
  indented, dot-leading — the same shape as LINQ method chains.

## LINQ

- **Method chains only.** Query (SQL-style) syntax is banned.
- **Prefer LINQ when possible.** Vectorization work in the runtime has removed most
  of the historical performance objections. If a measured hot path says otherwise,
  that is a dispute to bring to the hall — not a license to quietly hand-roll loops.

## Extension members

- Prefer **C# 14 extension blocks** over the older static-class extension-method
  syntax for new code.
- At the callsite, **invoke extension methods in extension style 100% of the time**.
  Never static-invocation style.

## Regex

- **Always use the regex source generators** (`[GeneratedRegex]`). No inline
  `new Regex(...)`, no static `Regex.Match(...)` with pattern strings.

## Logging

- **Always use logging delegates** (`LoggerMessage` source-generated delegates).
  No direct `_logger.LogInformation(...)` calls with interpolated or templated
  strings at the callsite.

## Async

- **Every async method takes `CancellationToken cancellationToken = default` as its
  last parameter** and propagates it to everything it awaits or returns.
- **Only mark a method `async` and use `await` if you need the result.** If the
  method's last act is returning a `Task`/`ValueTask` untouched, return it as-is —
  no `async` modifier, no `await`, no extra state machine.
  - The one place elision is *wrong*: when the returned task is produced inside a
    `using`/`try` scope in that method (the scope exits before the task completes).
    There you must `await`. Anywhere else, elide.
- **`await using` when possible.** If the type implements `IAsyncDisposable`,
  dispose it asynchronously — plain `using` on an async-disposable type blocks.
- **`ConfigureAwait(false)` in library code** — src and generator projects, on
  every await. Never in tests, where xUnit1030 is the authoritative rule (no
  SynchronizationContext to escape, and it can bypass xUnit's parallelization
  limits). CA2007 at latest-All is the enforcement arm in src.

## Helper functions

- **A helper with one call site had better really improve that call site.** If it
  doesn't clearly earn its extraction, inline it — the whole tree should be
  visible where the work happens, not scattered across single-use indirections.
- **DRY applies in tests too.** Repeated arrange/setup boilerplate across tests
  gets a helper (builder, factory method, shared fixture) instead of copy-paste.
  Test helpers follow the same one-call-site rule as everything else.

## Classes and accessibility

- **Every class is `sealed`, `abstract`, or `static`.** A class serving as both a
  polymorphic parent *and* an instantiable end-of-the-line is permitted but is the
  exception, never the rule — and should be called out in the plan when it happens.
- **Omit accessibility modifiers when adopting the language default.** Expand or
  contract accessibility only when explicitly needed. The goal is the smallest
  public API footprint possible — and when the default *is* the choice, leaving the
  modifier off signals that the default was adopted deliberately, not typed by habit.
- **Never escalate a class to `public` to test it.** Every library and generator
  project carries:

  ```xml
  <InternalsVisibleTo Include="$(AssemblyName).Tests" />
  ```

  with 1:1 class-library-to-test-project parity. Internals are already visible
  where they need to be.
- **Confine internals to DI wireup wherever possible.** The ideal shape: internal
  implementation classes, with only configuration/registration methods exposed.
  Small footprint, always.

## Enums

- **Every enum explicitly claims `0` as a value meaning "not one of the
  options"** — `None`, `Unknown`, `Unspecified`, whatever name fits the domain.
  Zero is never a real option, and never left unclaimed.
- **Every member carries an explicit integer value** — no relying on compiler
  auto-increment.
- **Flags enums use bit-shift values:** `1 << 0`, `1 << 1`, `1 << 2` — not
  hand-computed `1, 2, 4, 8`.

  ```csharp
  [Flags]
  enum Realms
  {
      None       = 0,
      Svartalfheim = 1 << 0,
      Asgard       = 1 << 1,
      Midgard      = 1 << 2,
  }
  ```

## XML documentation

- **`GenerateDocumentationFile` is on platform-wide** — both for package XML docs
  and because build-time IDE0005 (unused usings) only fires when doc generation
  is on. The unused-using enforcement rides on this switch; it does not come off.
- **The doc-comment obligation binds src only.** CS1591 is NoWarn'd in generator
  and test projects, so in practice: every publicly visible member in a src
  project carries XML docs, in the house style the exemplars in this document
  demonstrate — `<summary>` always; `<param>`/`<returns>` where they say
  something the signature doesn't; `<see cref=...>`/`<c>` for symbols.
- **Doc-comment layout is ReSharper's, declared as law so cleanup passes
  produce no churn.** Content that fits one line stays inline on the tag line;
  content that wraps goes block form — tags on their own lines, content
  indented four spaces after the `///`, wrapping at the 120-column limit the
  platform `.editorconfig` declares. Write new doc comments in this shape from
  the start — in plans, in generated exemplars, in implementations — so an R#
  reformat leaves them byte-identical:

  ```csharp
  /// <summary>Resolves a key by id.</summary>
  /// <remarks>
  ///     Implementations return a caller-owned copy — the caller may zero the returned buffer after use
  ///     without affecting the ring's internal state.
  /// </remarks>
  /// <exception cref="KeyNotFoundException">The id is not on the ring.</exception>
  ```

  Never hand-reflow an existing comment to a different wrap — the formatter
  owns the wrap; a human (or agent) re-wrapping by eye is exactly the diff
  noise this rule exists to prevent.

## Records

- **Records for request & response types, entity classes, and Ratatoskr commands**
  (when we get there). Immutability and structural equality are the point. The
  platform does not use EF change tracking or lazy loading, so entities have no
  reason to be mutable classes.
- **Prefer positional records** for terseness — except where polymorphism (or sheer
  member count) makes positional form unreadable; use nominal records there.

## Failure and the Two Unions

- **Direction of law: `Outcome<T>` over throwing for expected failure.**
  Exceptions are for the exceptional; expected failure travels in the unions.
  The rollout is in progress and this rule is applied with less rigidity until
  the overhaul effort lands — but new designs must not *add* throw-based control
  flow, and plans should note where they touch code awaiting conversion.
- **Request objects carry `Result<T>` (or `Result<T>?`) for scalars.** The same
  applies to response classes on egress `HttpClient` operations that produce a
  result.
- Full doctrine: the **Two Unions** spec in Glitnir — `Result<T>` is the
  Svartalfheim scalar/boundary union, `Outcome<T>` the Asgard event envelope.
  This section is the summary, not the source.

## Entities (EF Core)

The platform runs EF Core with **no lazy loading and no change tracking**. The
laws below are what make that possible — they are load-bearing, not stylistic.

- **Entities are `sealed record`s** implementing the platform entity contracts,
  with a `static Configure(EntityTypeBuilder<T>)` colocated on the entity (see
  Records: no change tracking means no mutable-class requirement).
- **No shadow properties, ever — on FKs or navigations.** Every foreign key and
  every navigation is declared on the entity, and **every relationship maps both
  ends explicitly**: `HasOne(...).WithMany(c => c.Inverse)` against a declared
  inverse collection, never a bare `WithMany()`. **Bridge (join) entities are
  declared 100% explicitly** — no implicit many-to-many. This is the pivotal key
  to living without lazy loading and change tracking.
- **Scalars and owned read-model objects carry the `required` modifier** —
  reference types always, value types whenever the type's default is not a
  valid state (a `default` `DeterministicGuid` identity, a zero code, a flags
  enum sitting at its `None` member), and owned `View` documents because the
  writer of the row writes the view. Optional scalars stay nullable and
  un-`required` (`DeterministicGuid? ParentRegionId`).
- **Reference navigations get `= null!;`.** The materializer owns their
  hydration, so `required` would be wrong there — and their nullability
  annotation is otherwise unimportant (no lazy-loading proxies to appease);
  non-null just keeps projection expressions clean. **Collection navigations
  are never `virtual` and always initialize to `[]`** — that pair is
  imperative: `public ICollection<Region> ChildRegions { get; init; } = [];`.
- **Never hand-write `.IsRequired()` / `.IsRequired(false)`.** EF infers
  required-vs-optional from the scalar's nullability annotation — mark the
  property's nullability correctly and the store columns follow
  (`NOT NULL` / `NULL`) on both providers.
- **Unbounded default lengths on strings and byte arrays are explicitly
  forbidden.** Every string/binary property declares its length, and the
  attribute path and the fluent path are equally sanctioned — you are not
  forced into one model or the other: **`[FixedLength(n)]`** for fixed-width
  columns (the alpha codes below), **`[MaxLength(n)]`** or **`HasMaxLength(n)`**
  for variable-width, and unbounded storage only by deliberate opt-in — the
  `[UnboundedLength]` attribute or the `-1` sentinel in the fluent API. A
  property with no length specified anywhere goes kaboom when
  `RequireExplicitLengthConvention` finalizes the model in migrations — silence
  is not an option, unbounded must be *said*.
  (Provider note: `[FixedLength(n)]` maps to `nchar(n)` on SQL Server but
  deliberately stays `character varying(n)` on PostgreSQL, where `char(n)` has
  no performance advantage and is usually the slowest choice — blank-padding
  costs storage and processing for nothing.)

  ```csharp
  /// <summary>
  /// Marks a string or binary property as explicitly unbounded — <c>nvarchar(max)</c>/<c>text</c>,
  /// <c>varbinary(max)</c>/<c>bytea</c>. Passes EF Core's own <c>-1</c> sentinel for "no maximum."
  /// The only attribute-path escape hatch from <see cref="RequireExplicitLengthConvention"/>.
  /// </summary>
  [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
  public sealed class UnboundedLengthAttribute() :
      System.ComponentModel.DataAnnotations.MaxLengthAttribute(-1);
  ```

- **Compose read-model `View` columns as owned JSON documents** so projection
  expressions can answer queries without joins (the CQRS read side). When adding
  a view: **always include the top-level object and its entire ancestor chain.**
  Child collections are fine at reasonable cardinality — an invoice with its
  invoice lines, yes; a state with every zip code in it, no.

The canonical greenfield entities — a parent/child pair showing both ends of an
explicit relationship:

```csharp
/// <summary>
/// A country or area per UN M49 with ISO and LDC classifications.
/// </summary>
public sealed record CountryOrArea : NorseEntityBase<CountryOrArea>, INorseEntity<CountryOrArea>
{
	/// <summary>The country-or-area identifier.</summary>
	public required DeterministicGuid Id { get; init; }
	/// <summary>The UN M49 code (3 digits).</summary>
	public required ushort Code { get; init; }
	/// <summary>The ISO 3166-1 alpha-2 code (2 letters).</summary>
	[FixedLength(2)]
	public required string Alpha2 { get; init; }
	/// <summary>The ISO 3166-1 alpha-3 code (3 letters).</summary>
	[FixedLength(3)]
	public required string Alpha3 { get; init; }
	/// <summary>The country or area name in English.</summary>
	public required string Name { get; init; }
	/// <summary>The parent region identifier, if applicable.</summary>
	public DeterministicGuid? ParentRegionId { get; init; }
	/// <summary>The parent region, if applicable.</summary>
	public Region ParentRegion { get; init; } = null!;
	/// <summary>The UN classification flags this country or area holds. Test with <see cref="Enum.HasFlag"/>.</summary>
	public required Classification Classification { get; init; }
	/// <summary>
	/// The denormalized read-model column: this row's own scalar fields alongside the ancestor
	/// Region/Subregion/IntermediateRegion chain, hydrated by the seed contributor and stored as an
	/// owned JSON document. Always present — only <see cref="CountryOrAreaView.Region"/> is
	/// <see langword="null"/>, and only for Antarctica, which has no ancestor at all. Named
	/// <c>View</c> as a deliberate homage to the SQL view it replaced: this is the platform's first
	/// "peer + ancestry" read column, one per entity, queried without joins.
	/// </summary>
	public required CountryOrAreaView View { get; init; }
	/// <summary>Configures the EF entity mapping.</summary>
	public static void Configure(EntityTypeBuilder<CountryOrArea> builder)
	{
		builder.HasKey(c => c.Id);
		builder.Property(c => c.Name).HasMaxLength(256);
		builder.HasIndex(c => c.Code).IsUnique();
		builder.HasIndex(c => c.Alpha2).IsUnique();
		builder.HasIndex(c => c.Alpha3).IsUnique();
		builder
			.HasOne(c => c.ParentRegion)
			.WithMany(c => c.CountriesOrAreas)
			.HasForeignKey(c => c.ParentRegionId);
		// View model map
		builder.OwnsOne(c => c.View, view =>
		{
			view.ToJson();
			view.OwnsOne(v => v.Region, region =>
				region.OwnsOne(r => r.Subregion,
					sub => sub.OwnsOne(s => s.IntermediateRegion)));
		});
	}
}

/// <summary>
/// A geographic region per UN M49 (Region, Subregion, or Intermediate Region).
/// </summary>
public sealed record Region : NorseEntityBase<Region>, INorseEntity<Region>
{
	/// <summary>The region identifier.</summary>
	public required DeterministicGuid Id { get; init; }
	/// <summary>The UN M49 code (3 digits).</summary>
	public required ushort Code { get; init; }
	/// <summary>The region name in English.</summary>
	public required string Name { get; init; }
	/// <summary>The hierarchical level of this region.</summary>
	public required RegionLevel Level { get; init; }
	/// <summary>The parent region identifier, if this region is a child.</summary>
	public DeterministicGuid? ParentRegionId { get; init; }
	/// <summary>The parent region, if this region is a child.</summary>
	public Region ParentRegion { get; init; } = null!;
	/// <summary>Child region navigation property</summary>
	public ICollection<Region> ChildRegions { get; init; } = [];
	/// <summary>Countries or areas</summary>
	public ICollection<CountryOrArea> CountriesOrAreas { get; init; } = [];
	/// <summary>Configures the EF entity mapping.</summary>
	public static void Configure(EntityTypeBuilder<Region> builder)
	{
		builder.HasKey(r => r.Id);
		builder.Property(r => r.Name).HasMaxLength(256);
		builder.HasIndex(r => r.Code).IsUnique();
		builder
			.HasOne(r => r.ParentRegion)
			.WithMany(c => c.ChildRegions)
			.HasForeignKey(r => r.ParentRegionId);
	}
}
```

Receipts — the same model, migrated to both providers, every column typed and
constrained exactly as the entity declares (`nchar(2)` from `[FixedLength(2)]`,
`NULL` on the optional FK from the nullable scalar, `json`/`jsonb` from the
owned view):

```sql
-- SQL Server
CREATE TABLE [CountryOrArea] (
    [Id] uniqueidentifier NOT NULL,
    [Code] int NOT NULL,
    [Alpha2] nchar(2) NOT NULL,
    [Alpha3] nchar(3) NOT NULL,
    [Name] nvarchar(256) NOT NULL,
    [ParentRegionId] uniqueidentifier NULL,
    [Classification] tinyint NOT NULL,
    [View] json NOT NULL,
    CONSTRAINT [PK_CountryOrArea] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CountryOrArea_Region_ParentRegionId] FOREIGN KEY ([ParentRegionId]) REFERENCES [Region] ([Id])
);

-- PostgreSQL
CREATE TABLE country_or_area (
    id uuid NOT NULL,
    code integer NOT NULL,
    alpha2 character varying(2) NOT NULL,
    alpha3 character varying(3) NOT NULL,
    name character varying(256) NOT NULL,
    parent_region_id uuid,
    classification smallint NOT NULL,
    view jsonb NOT NULL,
    CONSTRAINT pk_country_or_area PRIMARY KEY (id),
    CONSTRAINT fk_country_or_area_region_parent_region_id FOREIGN KEY (parent_region_id) REFERENCES region (id)
);
```


## Projects and dependencies

- **Leverage transitive dependencies whenever possible — *especially* in tests.**
  Do not add a `<PackageReference>` for something already flowing transitively.
  The point is anti-brittleness: versions are managed in one place, and a floor
  bump in a src project can never strand a stale pin somewhere downstream — a
  test project that hand-pins what its subject already carries is a future
  NU1605 with a timer on it (proven live, 2026-08-03, `Grpc.Net.Client` in
  `Hosting.Web.Client.Tests`). The one exception is **Yggdrasil**: NuGet Central
  Package Management is on there, and as the composition root it pins everything
  explicitly — one place to pick up a hotfix/patch instead of walking the tree
  and cutting a cascade of releases.
- **Tag package versions to the major:** `Version="3.*"`. While .NET 11 is in
  preview, framework-tracking packages are `Version="11.*-*"` — drop the
  prerelease wildcard at RTM.
- **One `<PropertyGroup>` and one `<ItemGroup>` per csproj**, members sorted
  alphabetically inside each — so any property is findable by scan, not by
  archaeology. (`Directory.*.props` files live in Ginnungagap and answer to their
  own structure; this law governs csproj files.)
- **The only sanctioned break of transitive-first: a stale package hosts a
  compromised transitive version.** When a dependency pins a known-vulnerable
  version of something it drags in (the canonical case:
  `System.ServiceModel.Primitives` hosting a compromised
  `System.Security.Cryptography.Xml`), add a direct `<PackageReference>` floated
  on the current train — `11.*-*` while .NET 11 is in preview, deliberately one
  greppable pattern platform-wide so the RTM sweep replaces every site with
  `11.*` or `*` in a single pass — with a comment naming the stale source that
  hosts it, so the reference isn't "cleaned up" later:

  ```xml
  <!-- Floats over the known-vulnerable transitive version hosted by System.ServiceModel.Primitives. -->
  <PackageReference Include="System.Security.Cryptography.Xml" Version="11.*-*" />
  ```

## Analyzers and the Suppression Law

The global baseline, scattered by Ginnungagap into every repo's
`config/Directory.Build.props`:

```xml
<!--
  Analyzer tiers follow the platform baseline: Security/Performance/Reliability/Usage
  at latest-All; Design stays at the global baseline because latest-All enables rules
  (e.g. CA1034) that conflict with discriminated-union-style type shapes.
-->
<AnalysisLevel>latest-Recommended</AnalysisLevel>
<AnalysisLevelSecurity>latest-All</AnalysisLevelSecurity>
<AnalysisLevelPerformance>latest-All</AnalysisLevelPerformance>
<AnalysisLevelReliability>latest-All</AnalysisLevelReliability>
<AnalysisLevelUsage>latest-All</AnalysisLevelUsage>
```

Each project *type* (generator, src, tests) narrows this via its own
`Directory.Build.props` `<NoWarn>` block — and every entry in that block carries a
written justification comment naming the rule ID, the rule's intent, and exactly why
it is wrong *in that context*. See the tests-level block for the canonical style.

The law:

1. **IDE0005 (unnecessary using) is never suppressed.** Removing the using line is
   *less* effort than suppressing the warning. This has happened repeatedly and it
   is noticed every time. Delete the line.
2. **Repeated-hit protocol.** If the same warning code fires many times in a row
   during plan-writing or plan-implementation: **stop**. Report the code, the
   project context (generator / src / tests), and the reason it fires. Odds are it
   belongs hoisted into the `<NoWarn>` array at the correct level and scattered by
   Ginnungagap — not laid as a trail of dogmess pragmas through the codebase.
3. **Inline suppression is the last resort.** A `#pragma` or `[SuppressMessage]`
   needs the same written justification a `<NoWarn>` entry would carry, and its
   presence should prompt the question: should this be hoisted?
4. **Never fix a warning by silencing it when the root cause is fixable.**
   `<NoWarn>` exists for warnings that are *wrong in context*, not warnings that
   are inconvenient.

## Source generators

House law for all files, generated files included: **BOM-free UTF-8, LF-only line
endings.** Generated source must be byte-identical regardless of the build
machine's OS — that is the platform's deterministic-build convention. Both helpers
below exist so this happens automatically; using them is not optional.

- **Emit templates as raw interpolated string literals** (`$$"""`) and build
  output through **Asgard's Emit library** (`CSharpEmit.AppendCSharp`).
  `AppendCSharp` appends the code followed by a single `\n` — always `\n`, never
  `Environment.NewLine` — and its `[StringSyntax("C#")]` parameter annotation
  gives you C# syntax highlighting inside the literal in VS / Rider.
- **Never hand-roll `sb.AppendLine(...)` for code emission.** `AppendLine` is
  `Environment.NewLine`-dependent and breaks determinism on Windows build agents —
  and line-by-line emission obscures the shape of the generated code.
- **Decompose repeating sections into helper methods interpolated into the
  template** (`{{ValidatorFields(model)}}`), not loops interleaved with
  `AppendLine` calls. The template should read like the file it produces.

  The canonical before/after:

  ```csharp
  // Old (bad): line-by-line emission — OS-dependent newlines, unreadable shape
  var builder = new StringBuilder();
  builder.AppendLine("// <auto-generated/>");
  builder.AppendLine($"namespace {model.Namespace};");
  builder.AppendLine($"sealed class {model.ContextName}InProcessGateway : I{model.ContextName}Gateway");
  builder.AppendLine("{");
  builder.AppendLine($"\treadonly {model.ServiceInterfaceName} _service;");
  for (var i = 0; i < model.Methods.Length; i++)
  {
      var method = model.Methods[i];
      var validatorFieldName = "_" + char.ToLowerInvariant(method.Name[0]) + method.Name.Substring(1) + "Validator";
      builder.AppendLine($"\treadonly FluentValidation.IValidator<{method.RequestTypeName}> {validatorFieldName};");
  }
  // ... dozens more AppendLine calls ...
  ```

  ```csharp
  // New (good): the template reads like the file it produces
  internal static string Emit(GatewayInterfaceModel model)
  {
      StringBuilder builder = new();
      builder.AppendCSharp(
          $$"""
          // <auto-generated/>
          namespace {{model.Namespace}};
          using Norse.Abstractions.Contracts;
          using Norse.Abstractions.Web.Server.Mediator;
          sealed class {{model.ContextName}}InProcessGateway : I{{model.ContextName}}Gateway
          {
              readonly {{model.ServiceInterfaceName}} _service;
              readonly Microsoft.Extensions.Logging.ILoggerFactory _loggerFactory;
              readonly Microsoft.AspNetCore.Authorization.IAuthorizationService _authorizationService;
              readonly Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider _authenticationStateProvider;
          {{ValidatorFields(model)}}
              public {{model.ContextName}}InProcessGateway(
                  {{model.ServiceInterfaceName}} service,
                  Microsoft.Extensions.Logging.ILoggerFactory loggerFactory,
                  Microsoft.AspNetCore.Authorization.IAuthorizationService authorizationService,
                  Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider authenticationStateProvider,
          {{ConstructorParams(model)}}
              {
                  _service = service;
                  _loggerFactory = loggerFactory;
                  _authorizationService = authorizationService;
                  _authenticationStateProvider = authenticationStateProvider;
          {{FieldAssignments(model)}}
              }
              async ValueTask<System.Security.Claims.ClaimsPrincipal> GetPrincipalAsync() =>
                  (await _authenticationStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false)).User;
          {{Methods(model)}}
          }
          """);
      return builder.ToString();
  }
  ```
- **Write files with `Utf8NoBom.Encoding`** (Asgard):

  ```csharp
  public static readonly Encoding Encoding =
      new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
  ```

  Use it for every file write; the framework default `Encoding.UTF8` emits a BOM
  and violates house law.

- **Generated code suppresses its own noise.** Warnings that are inherent to
  emitted output (CS1591 missing-XML-docs being the classic) are silenced by a
  `#pragma warning disable` in the generated file's header — emitted by the
  generator, right after `// <auto-generated/>` — **never** by a `<NoWarn>` in the
  consuming project's csproj. A csproj-level NoWarn for a generator's output is
  the Suppression Law violated at one remove: it silences the warning for the
  *whole project*, hand-written code included, to paper over the generator's
  omission. Fix the generator.

## Tests

- **Stack: xUnit v3 on Microsoft Testing Platform v2** as the runner. Every test
  project inherits from `Directory.Test.props`:

  ```xml
  <ItemGroup>
      <PackageReference Include="Microsoft.Testing.Extensions.CodeCoverage" Version="*" />
      <PackageReference Include="Microsoft.Testing.Platform.MSBuild" Version="*" />
      <PackageReference Include="NSubstitute" Version="*" />
      <PackageReference Include="Shouldly" Version="*" />
      <PackageReference Include="xunit.v3.mtp-v2" Version="*" />
      <Using Include="NSubstitute" />
      <Using Include="NSubstitute.ExceptionExtensions" />
      <Using Include="Shouldly" />
      <Using Include="Xunit" />
  </ItemGroup>
  ```

  So: **NSubstitute** for mocking, **Shouldly** for assertions, and the usings are
  already global — do not re-add them per file.
- **Test classes are `public sealed`** — public only because xUnit must see them;
  sealed per the class law above.
- **Test methods omit the accessibility modifier** — bare `void` / bare
  `async Task`, never `public void`. Private is fine for xUnit v3's generated
  invoker, and omitting the modifier follows the accessibility law above. (This is
  why IDE0051 lives in the tests `<NoWarn>` block.)
- Test method names are **sentence-shaped with underscores** for readability in
  runner output: `Navigates_to_root_when_the_gateway_completes_sign_out_directly`.

## Amendments

This document changes only by ruling from Buvy. If reality and the law disagree,
the plan should surface the conflict explicitly — Glitnir exists so that disputes
are settled once, in writing, and never re-litigated in commit messages.
