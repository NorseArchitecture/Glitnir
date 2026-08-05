# PII Primitives, Identity Integration, and the Erasure Seam — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Pairs with superpowers:test-driven-development on every task.

**Goal:** Ship the ratified PII design end to end — four PII primitives with masking law and the NORSE061/062 analyzer (Svartálfheim), the `Erased` category + key-seam contracts (Asgard), edge mappings + dev key provider (Midgard), the protected-PII value converter (Urðarbrunnr), and Identity integration with the three-act shred ceremony and disclosure surface (Himinbjörg; the disclosure wire contract rides Heimdall's `AuthN.Services` per the 2026-08-04 amendment), wired at the composition root (Yggdrasil).

**Architecture:** Spec: `../../Platform/specs/2026-08-03-pii-primitives-identity-erasure-seam-design.md` — read it before any task. Strict realm ship order (spec §9); each realm phase ends at a human ship gate (PR → CI → tag → NuGet) before the next realm's tasks consume the published surface. In-Bifröst development uses `NorseRef` project references, so tasks compile locally before gates.

**Tech Stack:** .NET 11 preview / C# 15, xUnit v3 + Shouldly + NSubstitute on MTP, Roslyn `DiagnosticAnalyzer`, EF Core 11 preview, ASP.NET Core Identity (`IPersonalDataProtector`/`ILookupProtector` seams), AES-256-GCM + HMAC-SHA256.

## Global Constraints

- Read `../../house-rules.md` in full before implementing any task; it governs every line of code below (tabs, `sealed`, target-typed `new()`, collection expressions, expression bodies, `ConfigureAwait(false)` in src, XML docs in src, Shouldly/NSubstitute, no FluentAssertions/Moq).
- **Namespace ruling (2026-08-03, Buvy — supersedes the flat-namespace code blocks below):** namespace and folder never collide (IDE0130 is an error, never suppressed). The PII types live in `namespace Norse.Primitives.Pii;` matching `src/Primitives/Pii/` (the Identifiers precedent — and a `using Norse.Primitives.Pii;` at the top of a file declares PII mode at a glance). Everything referencing them adjusts: Task 6's analyzer metadata names become `Norse.Primitives.Pii.IMaskedValue` / `Norse.Primitives.Pii.RetentionPolicyAttribute`, and downstream tasks add `using Norse.Primitives.Pii;` beside (not instead of) `using Norse.Primitives;` where `Result<T>` is also in play.
- **CURATION PASS (2026-08-03, post-Law-of-the-Realms — this plan was halted mid-Phase-A when its own converter tripped what became NORSE070; the law now exists and is attached platform-wide):**
  - **Wire format never leaves Infrastructure/Hosting (NORSE070, compiler-enforced).** `MaskedValueJsonConverter<T>` and every `[JsonConverter]` attribute are DELETED from this plan (Tasks 1–5 amended below); the masked-serialization defense-in-depth relocates to Midgard as new Task 12b. Tests/benchmarks are law-exempt.
  - **Resume protocol:** `feature/pii-primitives` carries Tasks 1–2 with the convicted files and has master (law included) merged in — the branch build prints the exact strip-list. The resume's FIRST commit strips: delete `src/Primitives/Pii/MaskedValueJsonConverter.cs` + `tests/Primitives.Tests/Pii/MaskedValueJsonConverterTests.cs`, remove the `[JsonConverter(...)]` attribute and `using System.Text.Json.Serialization;` from `EmailAddress.cs`. Then Task 3 dispatches per the amended text.
  - **Keys placement (ruled 2026-08-03: no per-functional-group packages — the `Infrastructure.Backend` precedent):** Task 8's contracts land in **`Asgard/src/Abstractions.Backend/Keys/`** (`namespace Norse.Abstractions.Backend.Keys;`), NOT a new `Abstractions.Keys` assembly; Task 12's dev store lands in **`Midgard/src/Infrastructure.Backend/Keys/`** (`namespace Norse.Infrastructure.Backend.Keys;`), NOT a new `Infrastructure.Keys` project. No new csproj/slnx work in either task; tests join the existing 1:1 `Abstractions.Backend.Tests`/`Infrastructure.Backend.Tests` under `Keys/` folders. Every `using Norse.Abstractions.Keys;` the original text mandated in later tasks reads `using Norse.Abstractions.Backend.Keys;`.
  - **The serialization seam exists** (`ISerializerProvider`/`ISerializer`/`NamingStrategy` in `Abstractions.Backend`, STJ machinery in `Infrastructure.Backend`, composed at the tree): anything disclosure-adjacent that needs bytes uses the seam, never STJ directly. Himinbjörg master now carries the seam-restored `DownloadPersonalData` scaffold endpoint — Task 19b deletes it outright (download is a gRPC call per the 2026-08-04 ruling; `GetMyPersonalDataAsync` is the replacement, the ported Heimdall `PersonalData.razor` materializes the file client-side).
  - **Midgard is consumed only by the tree (NORSE071):** already true in this plan — the ONLY Midgard reference is Task 20's composition root, plus law-exempt test fixtures. Task 18/19's `PostgresIdentityFixture` composes the Midgard dev key store: the TEST csproj needs its own direct `<NorseRef Include="Infrastructure.Backend"><Repo>Midgard</Repo></NorseRef>` (nothing flows it transitively; the Mímir integration-test precedent).
- **Branching:** every realm phase starts on a fresh local feature branch in that realm's repo; commits stay local and unpushed; Buvy pushes/PRs at the ship gate. Never branch or commit Bifröst itself.
- **Commit policy:** subagents commit only files they authored, named explicitly — never `git add -A`/`git add .`.
- **Hands-off files:** `src/Directory.Build.props`, `tests/Directory.Build.props`, `gen/Directory.Build.props`, `config/*` are Ginnungagap scatter — never edit; halt and ask if a change seems needed there.
- **Erased = 11** (`ErrorCategory.Erased`), next free explicit value after `MultipleMatches = 10`.
- **Diagnostic IDs: NORSE061 (retention gate), NORSE062 (direct-scalar ban), NORSE063 reserved** — claimed in `Svartalfheim/gen/Primitives.Analyzers/Diagnostics.cs` header ledger with the platform-wide grep recorded. NORSE040–049 belong to Wells; do not touch.
- **Squash law:** exactly one `InitialCreate` migration per provider in Himinbjörg — delete the `Migrations/` folder and re-add; never stack a second migration.
- **No throwing constructors on external-input paths** — PII structs parse via `Result<T>`; `ParseFailure.Empty`/`Malformed` only; culture-insensitive parsers carry no `IFormatProvider`.
- **No clock in Svartálfheim primitives** — time-dependent masking takes `DateOnly asOf` from the caller.
- **US English** everywhere; relative paths only in documents; LF-only, BOM-free UTF-8.
- **Package versions:** framework-tracking packages `11.*-*`; realm cross-references ride `NorseRef` in-repo (CI swaps to NuGet); Yggdrasil CPM pins everything explicitly.
- **Test naming:** `Should_{behavior}_when_{condition}` (Svartálfheim scalar style) or sentence-shaped `{Action}_{observed_behavior}` — match the project's existing files.
- Coverage packages/CI are already wired per realm; touched projects' tests must be green before each commit.

## File Structure (created/modified, by realm)

```
Svartalfheim/
  src/Primitives/Pii/IMaskedValue.cs                    (new)
  src/Primitives/Pii/IPiiScalar.cs                      (new)
  src/Primitives/Pii/RetentionBasis.cs                  (new)
  src/Primitives/Pii/RetentionPolicyAttribute.cs        (new)
  src/Primitives/Pii/EmailAddress.cs                    (new)
  src/Primitives/Pii/PhoneNumber.cs                     (new)
  src/Primitives/Pii/PersonalName.cs                    (new)
  src/Primitives/Pii/BirthDate.cs                       (new)
  gen/Primitives.Analyzers/Diagnostics.cs               (modify — ledger + NORSE061/062)
  gen/Primitives.Analyzers/WellKnownTypes.cs            (modify — PII symbols + INorseEntity)
  gen/Primitives.Analyzers/RetentionPolicyAnalyzer.cs   (new)
  gen/Primitives.Analyzers/PiiCompositionWalker.cs      (new)
  tests/Primitives.Tests/Pii/*Tests.cs                  (new, one per struct)
  tests/Primitives.Analyzers.Tests/RetentionPolicyAnalyzerTests.cs (new)
Asgard/
  src/Abstractions.Contracts/ErasureReceipt.cs          (new)
  src/Abstractions.Contracts/ErrorCategory.cs           (modify — Erased = 11)
  src/Abstractions.Contracts/Problem.cs                 (modify — Receipt member)
  src/Abstractions.Backend/Keys/*.cs                    (new folder — seam contracts; curation ruling)
  src/Abstractions.Web.Server/Facade/GrpcControllerBase.cs (modify — 410 fold)
  tests/Abstractions.Backend.Tests/Keys/*.cs            (new folder in existing project)
Midgard/
  src/Infrastructure.Web.Server/Mediator/Grpc/ProblemExtensions.cs   (modify)
  src/Infrastructure.Web.Client/Grpc/RpcExceptionExtensions.cs       (modify)
  src/Infrastructure.Web.Server/Xml/ProblemXmlWriter.cs              (modify — remarks only)
  src/Infrastructure.Web.Server/Json/MaskedValueJsonConverterFactory.cs (new — Task 12b, relocated defense)
  src/Infrastructure.Web.Server/Json/MvcBuilderExtensions.cs         (modify — register the factory)
  src/Infrastructure.Backend/Serialization/SystemTextJsonSerializer.cs (modify — mask IMaskedValue on the seam)
  src/Infrastructure.Backend/Keys/DevelopmentSubjectKeyStore.cs      (new folder — curation ruling)
  src/Infrastructure.Backend/Keys/ServiceCollectionExtensions.cs     (new)
  tests/Infrastructure.Backend.Tests/Keys/*.cs                       (new folder in existing project)
  tests/Infrastructure.Web.Server.Tests/.../MaskedValueJsonConverterFactoryTests.cs (new — Task 12b)
Urdarbrunnr/
  src/Persistence.EntityFramework/ProtectedPiiValueConverter.cs      (new)
  src/Persistence.EntityFramework/PiiProtectionModelExtensions.cs    (new)
  tests/Persistence.EntityFramework.Tests/ProtectedPiiValueConverterTests.cs (new)
Heimdall/
  src/AuthN.Services/IIdentityService.cs                 (new — disclosure contract, Task 19a)
  src/AuthN.Services/{GetMyPersonalDataRequest,GetMaskedPersonalDataRequest,PersonalDataResponse,MaskedPersonalDataResponse}.cs (new wire records)
  src/AuthN.Services/IdentityPolicies.cs                 (new — policy names beside AuthNPolicies)
  src/AuthN.Components/GetMaskedPersonalDataRequestValidator.cs (new)
  tests/AuthN.Services.Tests/RequestContractTests.cs     (extend — purity lock covers new records)
  tests/AuthN.Components.Tests/GetMaskedPersonalDataRequestValidatorTests.cs (new)
Himinbjorg/
  src/Identity.EntityFramework/SubjectKey.cs             (new entity)
  src/Identity.EntityFramework/NorseUser.cs              (modify — index/lockout split)
  src/Identity.EntityFramework/NorseIdentityDbContext.cs (modify — temporal, protection)
  src/Identity.Web.Server/NorsePersonalDataProtector.cs  (new)
  src/Identity.Web.Server/NorseLookupProtector.cs        (new)
  src/Identity.Web.Server/NorseUserClaimsPrincipalFactory.cs (new)
  src/Identity.Web.Server/ErasureService.cs              (new)
  src/Identity.Web.Server/IdentityBuilderExtensions.cs   (modify — wiring)
  src/Identity.Web.Server/Disclosure/*.cs                (new — handlers + service, Task 19b)
  src/Identity.Migrations.PostgreSQL|SqlServer/Migrations/ (regenerated, squash law)
  tests/…                                                (per project, 1:1)
Yggdrasil/
  Directory.Packages.props                               (modify — new pins)
  src/Hosting.Web.Server + src/Hosting.Migrations        (modify — AddNorseDevelopmentKeys)
```

---

## Phase A — Svartálfheim (`feature/pii-primitives`)

### Task 1: PII marker interfaces, retention attribute (CURATED: converter deleted — NORSE070; already landed on the branch, the strip commit finishes it)

**Files:**
- Create: `src/Primitives/Pii/IMaskedValue.cs`, `src/Primitives/Pii/IPiiScalar.cs`, `src/Primitives/Pii/RetentionBasis.cs`, `src/Primitives/Pii/RetentionPolicyAttribute.cs`
- Test: `tests/Primitives.Tests/Pii/RetentionPolicyAttributeTests.cs`

**Interfaces (Produces):**
- `interface IMaskedValue { string Masked { get; } string ToMasked(DateOnly asOf); }`
- `interface IPiiScalar<TSelf> : IMaskedValue where TSelf : struct, IPiiScalar<TSelf> { string WireValue { get; } static abstract Result<TSelf> Parse(ReadOnlySpan<char> value); }` — the generic hook Urðarbrunnr's converter and the disclosure surface both bind to. `WireValue` is the named, deliberate plaintext egress the spec (§1.5) requires; `IMaskedValue` alone stays the analyzer marker so non-struct downstream PII types can join governance.
- `enum RetentionBasis : byte { Unspecified = 0, SubjectKey = 1, StatutoryEpoch = 2 }`
- `sealed class RetentionPolicyAttribute(RetentionBasis basis, string? citation = null)` — property/field targets only; ctor throws `ArgumentOutOfRangeException` on `Unspecified` (the `Result(Failure)` smuggled-sentinel precedent).
- ~~`MaskedValueJsonConverter<T>`~~ — CURATED OUT: wire format never enters the forge (NORSE070). The spec §1.5 layer-2 defense relocates to Midgard (Task 12b).

- [ ] **Step 1: Write the failing tests**

```csharp
namespace Norse.Primitives.Tests.Pii;

public sealed class RetentionPolicyAttributeTests
{
	[Fact]
	void Should_throw_when_basis_is_the_unspecified_sentinel() =>
		Should.Throw<ArgumentOutOfRangeException>(() => new RetentionPolicyAttribute(RetentionBasis.Unspecified));

	[Fact]
	void Should_carry_basis_and_citation_when_constructed()
	{
		RetentionPolicyAttribute attribute = new(RetentionBasis.StatutoryEpoch, "GDPR Art. 17(3)(b)");
		attribute.Basis.ShouldBe(RetentionBasis.StatutoryEpoch);
		attribute.Citation.ShouldBe("GDPR Art. 17(3)(b)");
	}

	[Fact]
	void Should_target_properties_and_fields_only()
	{
		var usage = typeof(RetentionPolicyAttribute)
			.GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
			.Cast<AttributeUsageAttribute>()
			.Single();
		usage.ValidOn.ShouldBe(AttributeTargets.Property | AttributeTargets.Field);
	}
}
```

*(CURATED: the `MaskedValueJsonConverterTests` block that stood here is deleted with its subject — NORSE070.)*

- [ ] **Step 2: Run tests to verify they fail**

Run (from `Svartalfheim/`): `dotnet test tests/Primitives.Tests -- --filter-class "*.RetentionPolicyAttributeTests"`
Expected: FAIL — `RetentionPolicyAttribute` does not exist (compile error is the failure).

- [ ] **Step 3: Write the implementation**

`src/Primitives/Pii/IMaskedValue.cs`:

```csharp
namespace Norse.Primitives;

/// <summary>
/// The masking law every PII type carries, and the marker the retention analyzer keys on:
/// implementing this interface is what makes a type PII in the compiler's eyes (NORSE061/NORSE062).
/// A type cannot opt into PII governance while opting out of masking — they are the same symbol.
/// </summary>
public interface IMaskedValue
{
	/// <summary>
	/// The pure, clock-free masked rendering — what <see cref="object.ToString"/> and the JSON write
	/// path emit. A value, never prose: no labels, no English inside the string.
	/// </summary>
	string Masked { get; }

	/// <summary>
	/// The disclosure-time masked rendering as of <paramref name="asOf"/>. Most implementers ignore
	/// the parameter and return <see cref="Masked"/>; time-dependent masks (current age) are a pure
	/// function of (value, asOf) — no clock lives in a primitive.
	/// </summary>
	string ToMasked(DateOnly asOf);
}
```

`src/Primitives/Pii/IPiiScalar.cs`:

```csharp
namespace Norse.Primitives;

/// <summary>
/// The generic contract a PII scalar struct fulfills so infrastructure (the encrypting EF value
/// converter, the disclosure surface) can round-trip it without knowing the concrete type.
/// <see cref="WireValue"/> is the named, deliberate plaintext egress — the canonical wire string the
/// transport contracts carry; every accidental rendering path goes through
/// <see cref="IMaskedValue.Masked"/> instead.
/// </summary>
public interface IPiiScalar<TSelf> : IMaskedValue where TSelf : struct, IPiiScalar<TSelf>
{
	/// <summary>The canonical unmasked wire string. Deliberate egress only.</summary>
	string WireValue { get; }

	/// <summary>Parses the canonical wire form. Untrusted input — no throwing path.</summary>
	static abstract Result<TSelf> Parse(ReadOnlySpan<char> value);
}
```

`src/Primitives/Pii/RetentionBasis.cs`:

```csharp
namespace Norse.Primitives;

/// <summary>The declared legal basis under which a persisted PII field is retained.</summary>
public enum RetentionBasis : byte
{
	/// <summary>Sentinel CLR default — never a valid basis; a declaration always names its law.</summary>
	Unspecified = 0,
	/// <summary>Erased when the subject's key is destroyed (Class A/C — crypto-shredding).</summary>
	SubjectKey = 1,
	/// <summary>Retained under a statutory epoch key (Class B — reserved; cite the statute).</summary>
	StatutoryEpoch = 2
}
```

`src/Primitives/Pii/RetentionPolicyAttribute.cs`:

```csharp
namespace Norse.Primitives;

/// <summary>
/// Declares the retention basis for a persisted PII property. Property/field targets only — the
/// classification law is per field, never per table; there is no entity-level shorthand. Required by
/// NORSE061 on every persisted property whose type implements <see cref="IMaskedValue"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class RetentionPolicyAttribute : Attribute
{
	/// <summary>Declares the retention basis, with an optional statutory citation.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="basis"/> is the sentinel.</exception>
	public RetentionPolicyAttribute(RetentionBasis basis, string? citation = null)
	{
		if (basis is RetentionBasis.Unspecified)
			throw new ArgumentOutOfRangeException(nameof(basis), basis, "A retention declaration always names its basis.");
		Basis = basis;
		Citation = citation;
	}

	/// <summary>The declared basis.</summary>
	public RetentionBasis Basis { get; }

	/// <summary>The statutory citation, when the basis demands one.</summary>
	public string? Citation { get; }
}
```

*(CURATED: the `MaskedValueJsonConverter<T>` implementation that stood here is deleted — NORSE070; its defense-in-depth intent lives on as Task 12b in Midgard, where encodings are legal.)*

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.RetentionPolicyAttributeTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git checkout -b feature/pii-primitives
git add src/Primitives/Pii/IMaskedValue.cs src/Primitives/Pii/IPiiScalar.cs src/Primitives/Pii/RetentionBasis.cs src/Primitives/Pii/RetentionPolicyAttribute.cs tests/Primitives.Tests/Pii/RetentionPolicyAttributeTests.cs
git commit -m "feat: PII marker interfaces, retention attribute, masked JSON converter"
```

### Task 2: `EmailAddress`

**Files:**
- Create: `src/Primitives/Pii/EmailAddress.cs`
- Test: `tests/Primitives.Tests/Pii/EmailAddressTests.cs`

**Interfaces:**
- Consumes: Task 1's `IPiiScalar<TSelf>`.
- Produces: `readonly record struct EmailAddress : IPiiScalar<EmailAddress>` — `WireValue` (trimmed as-entered), `Normalized` (lowercase invariant — the exact blind-index input), `Masked` (`j***@d***.com` shape), `Parse(ReadOnlySpan<char>)`/`Parse(string?)`/`TryParse`, `ToString()` → `Masked`. `MaxLength = 254`.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace Norse.Primitives.Tests.Pii;

public sealed class EmailAddressTests
{
	[Fact]
	void Should_parse_and_expose_wire_and_normalized_forms_when_input_is_valid()
	{
		var result = EmailAddress.Parse("  Buvy@Example.COM ");
		result.TryGetValue(out Success<EmailAddress> success).ShouldBeTrue();
		success.Value.WireValue.ShouldBe("Buvy@Example.COM");
		success.Value.Normalized.ShouldBe("buvy@example.com");
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	void Should_fail_with_empty_reason_when_input_is_blank(string input)
	{
		EmailAddress.Parse(input).TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Empty);
	}

	[Theory]
	[InlineData("no-at-sign")]
	[InlineData("two@@ats.com")]
	[InlineData("a@b@c.com")]
	[InlineData("@nodomain.com")]
	[InlineData("nolocal@")]
	[InlineData("local@nodot")]
	[InlineData("local@.leadingdot.com")]
	[InlineData("local@trailingdot.com.")]
	[InlineData("spa ce@domain.com")]
	void Should_fail_with_malformed_reason_when_shape_is_invalid(string input)
	{
		EmailAddress.Parse(input).TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
	}

	[Fact]
	void Should_fail_with_malformed_reason_when_input_exceeds_max_length()
	{
		var input = $"{new string('a', 250)}@x.com";
		EmailAddress.Parse(input).TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
	}

	[Fact]
	void Should_mask_to_first_characters_and_tld_when_rendered()
	{
		var result = EmailAddress.Parse("jane@domain.com");
		result.TryGetValue(out Success<EmailAddress> success).ShouldBeTrue();
		success.Value.Masked.ShouldBe("j***@d***.com");
		success.Value.ToMasked(new DateOnly(2026, 8, 3)).ShouldBe("j***@d***.com");
		success.Value.ToString().ShouldBe("j***@d***.com");
	}

	[Fact]
	void Should_keep_only_the_final_label_when_domain_is_multi_label()
	{
		var result = EmailAddress.Parse("jane@mail.domain.co.uk");
		result.TryGetValue(out Success<EmailAddress> success).ShouldBeTrue();
		success.Value.Masked.ShouldBe("j***@m***.uk");
	}

	[Fact]
	void Should_round_trip_through_try_parse_when_input_is_valid()
	{
		EmailAddress.TryParse("buvy@example.com", out var email).ShouldBeTrue();
		email.WireValue.ShouldBe("buvy@example.com");
	}

	[Fact]
	void Should_throw_when_default_instance_is_accessed()
	{
		EmailAddress malformed = default;
		Should.Throw<InvalidOperationException>(() => malformed.WireValue);
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.EmailAddressTests"`
Expected: FAIL — `EmailAddress` does not exist.

- [ ] **Step 3: Write the implementation**

`src/Primitives/Pii/EmailAddress.cs`:

```csharp
namespace Norse.Primitives;

/// <summary>
/// An email address as PII: carries the normalization law (<see cref="Normalized"/> is the exact
/// string the blind-index HMAC is computed over) and the masking law
/// (<c>j***@d***.com</c> — first character each side of the <c>@</c>, final domain label kept).
/// <see cref="object.ToString"/> renders the mask; <see cref="WireValue"/> is the deliberate egress.
/// </summary>
/// <remarks>
/// <c>default(EmailAddress)</c> is malformed by construction (the <c>default(Result&lt;T&gt;)</c>
/// footgun class) — every member throws <see cref="InvalidOperationException"/> on it. Equality is
/// wire-value equality; identity-level sameness is a <see cref="Normalized"/> comparison.
/// </remarks>
public readonly record struct EmailAddress : IPiiScalar<EmailAddress>
{
	/// <summary>RFC 5321 total-length bound.</summary>
	public const int MaxLength = 254;

	readonly string _value;

	EmailAddress(string value) => _value = value;

	/// <summary>The canonical wire string (trimmed, as entered). Deliberate egress only.</summary>
	public string WireValue =>
		_value ?? throw new InvalidOperationException("default(EmailAddress) is malformed — construct via Parse.");

	/// <summary>The blind-index input: the wire value case-folded to lowercase invariant.</summary>
	public string Normalized =>
		WireValue.ToLowerInvariant();

	/// <inheritdoc />
	public string Masked
	{
		get
		{
			var value = WireValue;
			var at = value.IndexOf('@');
			var domain = value[(at + 1)..];
			var lastDot = domain.LastIndexOf('.');
			return $"{value[0]}***@{domain[0]}***{domain[lastDot..]}";
		}
	}

	/// <inheritdoc />
	public string ToMasked(DateOnly asOf) =>
		Masked;

	/// <inheritdoc />
	public override string ToString() =>
		Masked;

	/// <summary>Parses an email address shape: one <c>@</c>, non-empty local part, dotted domain.</summary>
	public static Result<EmailAddress> Parse(ReadOnlySpan<char> value)
	{
		var trimmed = value.Trim();
		if (trimmed.IsEmpty)
			return new(new Failure(ParseFailure.Empty, trimmed, nameof(EmailAddress)));
		if (trimmed.Length > MaxLength || !HasValidShape(trimmed))
			return new(new Failure(ParseFailure.Malformed, trimmed, nameof(EmailAddress), format: "local@domain.tld"));
		return new(new Success<EmailAddress>(new(trimmed.ToString())));
	}

	/// <summary>String overload forwarding to the span parser.</summary>
	public static Result<EmailAddress> Parse(string? value) =>
		Parse(value.AsSpan());

	/// <summary>Try-pattern over <see cref="Parse(ReadOnlySpan{char})"/>; <c>false</c> leaves default.</summary>
	public static bool TryParse(ReadOnlySpan<char> value, out EmailAddress email)
	{
		if (Parse(value).TryGetValue(out Success<EmailAddress> success))
		{
			email = success.Value;
			return true;
		}
		email = default;
		return false;
	}

	static bool HasValidShape(ReadOnlySpan<char> value)
	{
		var at = value.IndexOf('@');
		if (at < 1 || at != value.LastIndexOf('@'))
			return false;
		var domain = value[(at + 1)..];
		if (domain.Length < 3 || domain[0] == '.' || domain[^1] == '.' || domain.IndexOf('.') < 0)
			return false;
		foreach (var c in value)
		{
			if (char.IsWhiteSpace(c) || char.IsControl(c))
				return false;
		}
		return true;
	}
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.EmailAddressTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Primitives/Pii/EmailAddress.cs tests/Primitives.Tests/Pii/EmailAddressTests.cs
git commit -m "feat: EmailAddress PII primitive with normalization and masking law"
```

### Task 3: `PhoneNumber`

**Files:**
- Create: `src/Primitives/Pii/PhoneNumber.cs`
- Test: `tests/Primitives.Tests/Pii/PhoneNumberTests.cs`

**Interfaces:**
- Consumes: Task 1's `IPiiScalar<TSelf>`.
- Produces: `readonly record struct PhoneNumber : IPiiScalar<PhoneNumber>` — E.164 shape only (`+`, first digit 1–9, 8–15 digits total); separators (space, hyphen, dot, parentheses) stripped during parse; `WireValue == Normalized` (E.164 is already canonical); `Masked` = `***` + last four digits.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace Norse.Primitives.Tests.Pii;

public sealed class PhoneNumberTests
{
	[Theory]
	[InlineData("+15551234567", "+15551234567")]
	[InlineData("+1 (555) 123-4567", "+15551234567")]
	[InlineData("+44 20.7946.0958", "+442079460958")]
	void Should_canonicalize_to_e164_when_input_carries_separators(string input, string expected)
	{
		PhoneNumber.Parse(input).TryGetValue(out Success<PhoneNumber> success).ShouldBeTrue();
		success.Value.WireValue.ShouldBe(expected);
		success.Value.Normalized.ShouldBe(expected);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	void Should_fail_with_empty_reason_when_input_is_blank(string input)
	{
		PhoneNumber.Parse(input).TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Empty);
	}

	[Theory]
	[InlineData("5551234567")]        // no leading +
	[InlineData("+05551234567")]      // leading zero country code
	[InlineData("+1234567")]          // 7 digits — below floor
	[InlineData("+1234567890123456")] // 16 digits — above E.164 max
	[InlineData("+1555ABC4567")]      // letters
	void Should_fail_with_malformed_reason_when_shape_is_invalid(string input)
	{
		PhoneNumber.Parse(input).TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
	}

	[Fact]
	void Should_mask_to_last_four_digits_when_rendered()
	{
		PhoneNumber.Parse("+15551234567").TryGetValue(out Success<PhoneNumber> success).ShouldBeTrue();
		success.Value.Masked.ShouldBe("***4567");
		success.Value.ToMasked(new DateOnly(2026, 8, 3)).ShouldBe("***4567");
		success.Value.ToString().ShouldBe("***4567");
	}

	[Fact]
	void Should_throw_when_default_instance_is_accessed()
	{
		PhoneNumber malformed = default;
		Should.Throw<InvalidOperationException>(() => malformed.WireValue);
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.PhoneNumberTests"`
Expected: FAIL — `PhoneNumber` does not exist.

- [ ] **Step 3: Write the implementation**

`src/Primitives/Pii/PhoneNumber.cs`:

```csharp
namespace Norse.Primitives;

/// <summary>
/// A phone number as PII, canonicalized to E.164 — shape validation only (leading <c>+</c>, first
/// digit 1–9, 8–15 digits); regional validity is a service concern. <see cref="Normalized"/> equals
/// <see cref="WireValue"/> because E.164 is already the canonical blind-index form. Mask: last four
/// digits (<c>***4567</c> — country-code-agnostic, no region leak).
/// </summary>
/// <remarks><c>default(PhoneNumber)</c> is malformed by construction; members throw on it.</remarks>
public readonly record struct PhoneNumber : IPiiScalar<PhoneNumber>
{
	const int MinDigits = 8, MaxDigits = 15;

	readonly string _value;

	PhoneNumber(string value) => _value = value;

	/// <summary>The canonical E.164 wire string. Deliberate egress only.</summary>
	public string WireValue =>
		_value ?? throw new InvalidOperationException("default(PhoneNumber) is malformed — construct via Parse.");

	/// <summary>The blind-index input — identical to <see cref="WireValue"/> for E.164.</summary>
	public string Normalized =>
		WireValue;

	/// <inheritdoc />
	public string Masked =>
		$"***{WireValue[^4..]}";

	/// <inheritdoc />
	public string ToMasked(DateOnly asOf) =>
		Masked;

	/// <inheritdoc />
	public override string ToString() =>
		Masked;

	/// <summary>Parses to E.164, stripping common separators (space, hyphen, dot, parentheses).</summary>
	public static Result<PhoneNumber> Parse(ReadOnlySpan<char> value)
	{
		var trimmed = value.Trim();
		if (trimmed.IsEmpty)
			return new(new Failure(ParseFailure.Empty, trimmed, nameof(PhoneNumber)));
		if (trimmed[0] != '+')
			return new(new Failure(ParseFailure.Malformed, trimmed, nameof(PhoneNumber), format: "+15551234567"));

		Span<char> digits = stackalloc char[MaxDigits + 1];
		var count = 0;
		foreach (var c in trimmed[1..])
		{
			if (c is ' ' or '-' or '.' or '(' or ')')
				continue;
			if (!char.IsAsciiDigit(c) || count == MaxDigits)
				return new(new Failure(ParseFailure.Malformed, trimmed, nameof(PhoneNumber), format: "+15551234567"));
			digits[count++] = c;
		}
		if (count < MinDigits || digits[0] == '0')
			return new(new Failure(ParseFailure.Malformed, trimmed, nameof(PhoneNumber), format: "+15551234567"));

		return new(new Success<PhoneNumber>(new($"+{digits[..count]}")));
	}

	/// <summary>String overload forwarding to the span parser.</summary>
	public static Result<PhoneNumber> Parse(string? value) =>
		Parse(value.AsSpan());

	/// <summary>Try-pattern over <see cref="Parse(ReadOnlySpan{char})"/>; <c>false</c> leaves default.</summary>
	public static bool TryParse(ReadOnlySpan<char> value, out PhoneNumber phone)
	{
		if (Parse(value).TryGetValue(out Success<PhoneNumber> success))
		{
			phone = success.Value;
			return true;
		}
		phone = default;
		return false;
	}
}
```

Note: `$"+{digits[..count]}"` interpolates a `ReadOnlySpan<char>` — legal in C# interpolation handlers, no intermediate string.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.PhoneNumberTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Primitives/Pii/PhoneNumber.cs tests/Primitives.Tests/Pii/PhoneNumberTests.cs
git commit -m "feat: PhoneNumber PII primitive with E.164 canonicalization"
```

### Task 4: `PersonalName`

**Files:**
- Create: `src/Primitives/Pii/PersonalName.cs`
- Test: `tests/Primitives.Tests/Pii/PersonalNameTests.cs`

**Interfaces:**
- Consumes: Task 1's `IPiiScalar<TSelf>`.
- Produces: `readonly record struct PersonalName : IPiiScalar<PersonalName>` — **a single name component** (a composing entity declares `GivenName`/`MiddleName`/`FamilyName` each as its own `PersonalName` field; component count and cultural ordering are the consumer's concern, spec §1.1). `WireValue` = trimmed, Unicode NFC. `Normalized` = NFC + uppercase invariant. `Masked` = first character uppercased + `.` (`"B."`). Max length 128; rejects control characters; requires at least one letter.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace Norse.Primitives.Tests.Pii;

public sealed class PersonalNameTests
{
	[Fact]
	void Should_parse_and_normalize_when_input_is_valid()
	{
		PersonalName.Parse("  Buvinghausen ").TryGetValue(out Success<PersonalName> success).ShouldBeTrue();
		success.Value.WireValue.ShouldBe("Buvinghausen");
		success.Value.Normalized.ShouldBe("BUVINGHAUSEN");
	}

	[Fact]
	void Should_apply_form_c_normalization_when_input_is_decomposed()
	{
		// "é" as 'e' + combining acute accent (decomposed, Form D)
		PersonalName.Parse("Réne").TryGetValue(out Success<PersonalName> success).ShouldBeTrue();
		success.Value.WireValue.ShouldBe("Réne");
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	void Should_fail_with_empty_reason_when_input_is_blank(string input)
	{
		PersonalName.Parse(input).TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Empty);
	}

	[Theory]
	[InlineData("123")]
	[InlineData("---")]
	[InlineData("tab\there")]
	void Should_fail_with_malformed_reason_when_shape_is_invalid(string input)
	{
		PersonalName.Parse(input).TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
	}

	[Fact]
	void Should_fail_with_malformed_reason_when_input_exceeds_max_length()
	{
		PersonalName.Parse(new string('a', 129)).TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
	}

	[Theory]
	[InlineData("Buvinghausen", "B.")]
	[InlineData("van der Berg", "V.")]
	[InlineData("Ólafsson", "Ó.")]
	void Should_mask_to_single_uppercased_initial_when_rendered(string input, string expected)
	{
		PersonalName.Parse(input).TryGetValue(out Success<PersonalName> success).ShouldBeTrue();
		success.Value.Masked.ShouldBe(expected);
		success.Value.ToMasked(new DateOnly(2026, 8, 3)).ShouldBe(expected);
		success.Value.ToString().ShouldBe(expected);
	}

	[Fact]
	void Should_throw_when_default_instance_is_accessed()
	{
		PersonalName malformed = default;
		Should.Throw<InvalidOperationException>(() => malformed.WireValue);
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.PersonalNameTests"`
Expected: FAIL — `PersonalName` does not exist.

- [ ] **Step 3: Write the implementation**

`src/Primitives/Pii/PersonalName.cs`:

```csharp
using System.Text;
namespace Norse.Primitives;

/// <summary>
/// A single personal-name component as PII. Deliberately not a composite: an entity declares
/// <c>GivenName</c>/<c>MiddleName</c>/<c>FamilyName</c> each as its own <see cref="PersonalName"/>
/// field — component count and cultural ordering are the consumer's rendering concern, never the
/// primitive's. Mask: single uppercased initial with a period (<c>B.</c>); a grouped rendering
/// (<c>B.B.</c>) is display-layer composition over N masked components.
/// </summary>
/// <remarks><c>default(PersonalName)</c> is malformed by construction; members throw on it.</remarks>
public readonly record struct PersonalName : IPiiScalar<PersonalName>
{
	/// <summary>Component length bound.</summary>
	public const int MaxLength = 128;

	readonly string _value;

	PersonalName(string value) => _value = value;

	/// <summary>The canonical wire string (trimmed, Unicode NFC). Deliberate egress only.</summary>
	public string WireValue =>
		_value ?? throw new InvalidOperationException("default(PersonalName) is malformed — construct via Parse.");

	/// <summary>The search-normalization form: NFC, uppercase invariant. Not blind-indexed in this scope.</summary>
	public string Normalized =>
		WireValue.ToUpperInvariant();

	/// <inheritdoc />
	public string Masked =>
		$"{char.ToUpperInvariant(WireValue[0])}.";

	/// <inheritdoc />
	public string ToMasked(DateOnly asOf) =>
		Masked;

	/// <inheritdoc />
	public override string ToString() =>
		Masked;

	/// <summary>Parses one name component: 1–128 chars, no control characters, at least one letter.</summary>
	public static Result<PersonalName> Parse(ReadOnlySpan<char> value)
	{
		var trimmed = value.Trim();
		if (trimmed.IsEmpty)
			return new(new Failure(ParseFailure.Empty, trimmed, nameof(PersonalName)));
		if (trimmed.Length > MaxLength || !HasValidShape(trimmed))
			return new(new Failure(ParseFailure.Malformed, trimmed, nameof(PersonalName)));
		var canonical = trimmed.ToString();
		if (!canonical.IsNormalized(NormalizationForm.FormC))
			canonical = canonical.Normalize(NormalizationForm.FormC);
		return new(new Success<PersonalName>(new(canonical)));
	}

	/// <summary>String overload forwarding to the span parser.</summary>
	public static Result<PersonalName> Parse(string? value) =>
		Parse(value.AsSpan());

	/// <summary>Try-pattern over <see cref="Parse(ReadOnlySpan{char})"/>; <c>false</c> leaves default.</summary>
	public static bool TryParse(ReadOnlySpan<char> value, out PersonalName name)
	{
		if (Parse(value).TryGetValue(out Success<PersonalName> success))
		{
			name = success.Value;
			return true;
		}
		name = default;
		return false;
	}

	static bool HasValidShape(ReadOnlySpan<char> value)
	{
		var hasLetter = false;
		foreach (var c in value)
		{
			if (char.IsControl(c) || char.IsDigit(c))
				return false;
			hasLetter |= char.IsLetter(c);
		}
		return hasLetter;
	}
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.PersonalNameTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Primitives/Pii/PersonalName.cs tests/Primitives.Tests/Pii/PersonalNameTests.cs
git commit -m "feat: PersonalName PII primitive as single name component"
```

### Task 5: `BirthDate`

**Files:**
- Create: `src/Primitives/Pii/BirthDate.cs`
- Test: `tests/Primitives.Tests/Pii/BirthDateTests.cs`

**Interfaces:**
- Consumes: Task 1's `IPiiScalar<TSelf>`.
- Produces: `readonly record struct BirthDate : IPiiScalar<BirthDate>` — wraps `DateOnly Value`; wire form strict ISO 8601 `yyyy-MM-dd`; `Masked` = `"****-**-**"` (zero-information redaction — a log line has no business knowing an age); `ToMasked(asOf)` = exact current age as an invariant string (`"38"`, clamped at 0), computed at disclosure time, never stored. No `Over18`-style predicates (spec §1.4).

- [ ] **Step 1: Write the failing tests**

```csharp
namespace Norse.Primitives.Tests.Pii;

public sealed class BirthDateTests
{
	[Fact]
	void Should_parse_strict_iso_when_input_is_valid()
	{
		BirthDate.Parse("1988-04-12").TryGetValue(out Success<BirthDate> success).ShouldBeTrue();
		success.Value.Value.ShouldBe(new DateOnly(1988, 4, 12));
		success.Value.WireValue.ShouldBe("1988-04-12");
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	void Should_fail_with_empty_reason_when_input_is_blank(string input)
	{
		BirthDate.Parse(input).TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Empty);
	}

	[Theory]
	[InlineData("04/12/1988")]
	[InlineData("1988-4-12")]
	[InlineData("1988-13-01")]
	[InlineData("19880412")]
	[InlineData("not-a-date")]
	void Should_fail_with_malformed_reason_when_format_is_not_strict_iso(string input)
	{
		BirthDate.Parse(input).TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
	}

	[Fact]
	void Should_redact_completely_when_pure_mask_is_rendered()
	{
		BirthDate.Parse("1988-04-12").TryGetValue(out Success<BirthDate> success).ShouldBeTrue();
		success.Value.Masked.ShouldBe("****-**-**");
		success.Value.ToString().ShouldBe("****-**-**");
	}

	[Theory]
	[InlineData("1988-04-12", 2026, 8, 3, "38")]   // birthday passed this year
	[InlineData("1988-09-12", 2026, 8, 3, "37")]   // birthday not yet reached
	[InlineData("1988-08-03", 2026, 8, 3, "38")]   // birthday is today
	[InlineData("2027-01-01", 2026, 8, 3, "0")]    // future date clamps to zero
	void Should_compute_exact_age_when_disclosure_mask_is_requested(string birth, int y, int m, int d, string expected)
	{
		BirthDate.Parse(birth).TryGetValue(out Success<BirthDate> success).ShouldBeTrue();
		success.Value.ToMasked(new DateOnly(y, m, d)).ShouldBe(expected);
	}

	[Fact]
	void Should_test_the_leap_day_boundary_when_computing_age()
	{
		// Born Feb 29; on Feb 28 of a non-leap year the birthday has not occurred yet.
		BirthDate.Parse("2000-02-29").TryGetValue(out Success<BirthDate> success).ShouldBeTrue();
		success.Value.ToMasked(new DateOnly(2026, 2, 28)).ShouldBe("25");
		success.Value.ToMasked(new DateOnly(2026, 3, 1)).ShouldBe("26");
	}

	[Fact]
	void Should_throw_when_default_instance_is_accessed()
	{
		BirthDate malformed = default;
		Should.Throw<InvalidOperationException>(() => malformed.WireValue);
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.BirthDateTests"`
Expected: FAIL — `BirthDate` does not exist.

- [ ] **Step 3: Write the implementation**

`src/Primitives/Pii/BirthDate.cs`:

```csharp
using System.Globalization;
namespace Norse.Primitives;

/// <summary>
/// A birth date as PII — not a <see cref="DateOnly"/> alias: the type is what the analyzer keys on.
/// The pure mask is a zero-information redaction (<c>****-**-**</c>); the disclosure mask is the
/// exact current age as of a caller-supplied date — computed at disclosure time, never stored, no
/// clock in the primitive. No <c>Over18</c>-style predicates ship: threshold consumers compute from
/// the disclosed age; a no-disclosure threshold check is a purpose-built endpoint if ever needed.
/// </summary>
/// <remarks><c>default(BirthDate)</c> is malformed by construction; members throw on it.</remarks>
public readonly record struct BirthDate : IPiiScalar<BirthDate>
{
	const string WireFormat = "yyyy-MM-dd";

	readonly DateOnly _value;
	readonly bool _initialized;

	BirthDate(DateOnly value) => (_value, _initialized) = (value, true);

	/// <summary>The birth date.</summary>
	public DateOnly Value =>
		_initialized ? _value : throw new InvalidOperationException("default(BirthDate) is malformed — construct via Parse.");

	/// <summary>The canonical ISO 8601 wire string. Deliberate egress only.</summary>
	public string WireValue =>
		Value.ToString(WireFormat, CultureInfo.InvariantCulture);

	/// <inheritdoc />
	public string Masked
	{
		get
		{
			_ = Value;
			return "****-**-**";
		}
	}

	/// <summary>The exact age in whole years as of <paramref name="asOf"/>, clamped at zero.</summary>
	public string ToMasked(DateOnly asOf)
	{
		var age = asOf.Year - Value.Year;
		if (asOf < Value.AddYears(age))
			age--;
		return Math.Max(age, 0).ToString(CultureInfo.InvariantCulture);
	}

	/// <inheritdoc />
	public override string ToString() =>
		Masked;

	/// <summary>Parses strict ISO 8601 (<c>yyyy-MM-dd</c>) only — no culture inference, ever.</summary>
	public static Result<BirthDate> Parse(ReadOnlySpan<char> value)
	{
		var trimmed = value.Trim();
		if (trimmed.IsEmpty)
			return new(new Failure(ParseFailure.Empty, trimmed, nameof(BirthDate)));
		return DateOnly.TryParseExact(trimmed, WireFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ?
			new(new Success<BirthDate>(new(date))) :
			new(new Failure(ParseFailure.Malformed, trimmed, nameof(BirthDate), format: WireFormat));
	}

	/// <summary>String overload forwarding to the span parser.</summary>
	public static Result<BirthDate> Parse(string? value) =>
		Parse(value.AsSpan());

	/// <summary>Try-pattern over <see cref="Parse(ReadOnlySpan{char})"/>; <c>false</c> leaves default.</summary>
	public static bool TryParse(ReadOnlySpan<char> value, out BirthDate birthDate)
	{
		if (Parse(value).TryGetValue(out Success<BirthDate> success))
		{
			birthDate = success.Value;
			return true;
		}
		birthDate = default;
		return false;
	}
}
```

Note `Value.AddYears(age)` on a Feb 29 birth date lands on Feb 28 in non-leap years (`DateOnly.AddYears` clamps), so the Feb 28 boundary test asserts the clamped behavior: the person is already 25+1 on Feb 28? No — clamping makes `2000-02-29.AddYears(26)` = `2026-02-28`, so `asOf 2026-02-28 < 2026-02-28` is false and age stays 26. The test above pins **25 on Feb 28 / 26 on Mar 1**, so the implementation must compare against the *un-clamped* logical birthday: use `asOf.DayOfYear`-independent comparison — compute `age = asOf.Year - Value.Year; if (asOf.Month < Value.Month || (asOf.Month == Value.Month && asOf.Day < Value.Day)) age--;`. Implement with the month/day comparison, not `AddYears`, and keep the test as the authority (bit-boundary law: test the actual boundary).

```csharp
	/// <summary>The exact age in whole years as of <paramref name="asOf"/>, clamped at zero.</summary>
	public string ToMasked(DateOnly asOf)
	{
		var value = Value;
		var age = asOf.Year - value.Year;
		if (asOf.Month < value.Month || (asOf.Month == value.Month && asOf.Day < value.Day))
			age--;
		return Math.Max(age, 0).ToString(CultureInfo.InvariantCulture);
	}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.BirthDateTests"`
Expected: PASS (including both leap-day boundary assertions).

- [ ] **Step 5: Commit**

```bash
git add src/Primitives/Pii/BirthDate.cs tests/Primitives.Tests/Pii/BirthDateTests.cs
git commit -m "feat: BirthDate PII primitive with redacted mask and disclosure-time age"
```

### Task 6: NORSE061/NORSE062 — the retention analyzer

**Files:**
- Modify: `gen/Primitives.Analyzers/Diagnostics.cs` (ledger + two descriptors)
- Modify: `gen/Primitives.Analyzers/WellKnownTypes.cs` (add `IMaskedValue`, `RetentionPolicyAttribute`, `INorseEntity`)
- Create: `gen/Primitives.Analyzers/RetentionPolicyAnalyzer.cs`, `gen/Primitives.Analyzers/PiiCompositionWalker.cs`
- Test: `tests/Primitives.Analyzers.Tests/RetentionPolicyAnalyzerTests.cs`

**Interfaces:**
- Consumes: `AnalyzerTestHarness` (existing — compile-clean-first assertion, stub-source pattern), `WellKnownTypes.Resolve` pattern (metadata-name resolution, null → analyzer self-disables **except** here: `IMaskedValue`/`RetentionPolicyAttribute` are same-package symbols and always resolve; only `INorseEntity` may be absent, and its absence disables the analyzer because no persisted roots can exist).
- Produces: build errors NORSE061 (persisted root has an `IMaskedValue`-typed property with no `[RetentionPolicy]`) and NORSE062 (`IMaskedValue` implementer reachable through anything other than a direct scalar property — nested composition, collection element, or array). The Himinbjörg removal-fails-build fixture lives here as a test (spec §4.5's honesty note: the in-realm proof lands with the first struct-typed PII property).

**Law recap for the implementer (spec §5):** roots are types implementing `Norse.Persistence.EntityFramework.INorseEntity<TSelf>` (either tier — direct interface implementation is how brownfield entities join). Per public instance property on the root: unwrap `Nullable<T>`; if the unwrapped type implements `IMaskedValue` → direct scalar → needs `[RetentionPolicy]` on that property (NORSE061 if absent). Otherwise BFS the property's type closure (public instance properties, collection/array element types, cycle-safe, skip `string` and framework special types); any `IMaskedValue` implementer found → NORSE062 on the root property, no attribute cures it. A collection whose element type implements `IMaskedValue` is NORSE062 directly.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace Norse.Primitives.Analyzers.Tests;

public sealed class RetentionPolicyAnalyzerTests
{
	// Svartálfheim cannot reference Urðarbrunnr — stub INorseEntity with the identical metadata name,
	// following the harness's existing OutcomeStub pattern.
	const string EntityStub =
		"""
		namespace Norse.Persistence.EntityFramework
		{
			public interface INorseEntity<TSelf> where TSelf : class, INorseEntity<TSelf>
			{
			}
		}
		""";

	const string PiiFixture =
		"""
		using Norse.Primitives.Pii;
		namespace Fixtures
		{
			public readonly record struct TestEmail : IMaskedValue
			{
				public string Masked => "***";
				public string ToMasked(System.DateOnly asOf) => Masked;
			}
		}
		""";

	[Fact]
	async Task Reports_norse061_when_pii_property_has_no_retention_policy()
	{
		var source =
			"""
			using Fixtures;
			using Norse.Persistence.EntityFramework;
			namespace App
			{
				public sealed class Person : INorseEntity<Person>
				{
					public TestEmail Email { get; init; }
				}
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(new RetentionPolicyAnalyzer(), EntityStub, PiiFixture, source);
		diagnostics.ShouldContain(d => d.Id == "NORSE061");
	}

	[Fact]
	async Task Reports_nothing_when_pii_property_declares_retention_policy()
	{
		var source =
			"""
			using Fixtures;
			using Norse.Persistence.EntityFramework;
			using Norse.Primitives.Pii;
			namespace App
			{
				public sealed class Person : INorseEntity<Person>
				{
					[RetentionPolicy(RetentionBasis.SubjectKey)]
					public TestEmail Email { get; init; }
				}
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(new RetentionPolicyAnalyzer(), EntityStub, PiiFixture, source);
		diagnostics.ShouldBeEmpty();
	}

	[Fact]
	async Task Reports_norse061_when_nullable_pii_property_has_no_retention_policy()
	{
		var source =
			"""
			using Fixtures;
			using Norse.Persistence.EntityFramework;
			namespace App
			{
				public sealed class Person : INorseEntity<Person>
				{
					public TestEmail? Email { get; init; }
				}
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(new RetentionPolicyAnalyzer(), EntityStub, PiiFixture, source);
		diagnostics.ShouldContain(d => d.Id == "NORSE061");
	}

	[Fact]
	async Task Reports_norse062_when_pii_hides_inside_a_composed_type()
	{
		var source =
			"""
			using Fixtures;
			using Norse.Persistence.EntityFramework;
			using Norse.Primitives.Pii;
			namespace App
			{
				public sealed class ContactCard
				{
					public TestEmail Email { get; init; }
				}
				public sealed class Person : INorseEntity<Person>
				{
					[RetentionPolicy(RetentionBasis.SubjectKey)]
					public ContactCard Contact { get; init; } = null!;
				}
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(new RetentionPolicyAnalyzer(), EntityStub, PiiFixture, source);
		diagnostics.ShouldContain(d => d.Id == "NORSE062");
	}

	[Fact]
	async Task Reports_norse062_when_pii_is_a_collection_element()
	{
		var source =
			"""
			using System.Collections.Generic;
			using Fixtures;
			using Norse.Persistence.EntityFramework;
			namespace App
			{
				public sealed class Person : INorseEntity<Person>
				{
					public ICollection<TestEmail> Emails { get; init; } = [];
				}
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(new RetentionPolicyAnalyzer(), EntityStub, PiiFixture, source);
		diagnostics.ShouldContain(d => d.Id == "NORSE062");
	}

	[Fact]
	async Task Reports_norse062_not_norse061_when_pii_is_an_array_element()
	{
		// Arrays route through IArrayTypeSymbol, not INamedTypeSymbol — a named-type-only collection
		// guard misroutes this to the attribute-curable NORSE061. Banned means banned: NORSE062.
		var source =
			"""
			using Fixtures;
			using Norse.Persistence.EntityFramework;
			using Norse.Primitives.Pii;
			namespace App
			{
				public sealed class Person : INorseEntity<Person>
				{
					[RetentionPolicy(RetentionBasis.SubjectKey)]
					public TestEmail[] Emails { get; init; } = [];
				}
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(new RetentionPolicyAnalyzer(), EntityStub, PiiFixture, source);
		var diagnostic = diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("NORSE062"); // the attribute on the property does NOT cure it
	}

	[Fact]
	async Task Reports_nothing_when_pii_lives_on_a_type_that_is_not_a_persisted_root()
	{
		// Retention is a storage concern — a wire DTO holding PII transiently needs no basis.
		var source =
			"""
			using Fixtures;
			namespace App
			{
				public sealed class LoginRequest
				{
					public TestEmail Email { get; init; }
				}
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(new RetentionPolicyAnalyzer(), EntityStub, PiiFixture, source);
		diagnostics.ShouldBeEmpty();
	}

	[Fact]
	async Task Removing_the_attribute_from_a_declared_entity_fails_the_build()
	{
		// The spec §2a "wired, not just designed" fixture: same entity, attribute stripped → error.
		var source =
			"""
			using Fixtures;
			using Norse.Persistence.EntityFramework;
			namespace App
			{
				public sealed class NorseUserProfile : INorseEntity<NorseUserProfile>
				{
					public TestEmail RecoveryEmail { get; init; }
				}
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(new RetentionPolicyAnalyzer(), EntityStub, PiiFixture, source);
		var diagnostic = diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("NORSE061");
		diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
	}
}
```

Harness note: if the existing `AnalyzerTestHarness.GetDiagnosticsAsync` signature takes only sources (constructing `ResultInServiceResponseAnalyzer` internally), add an overload taking the analyzer instance — do not change the existing call sites.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Primitives.Analyzers.Tests -- --filter-class "*.RetentionPolicyAnalyzerTests"`
Expected: FAIL — `RetentionPolicyAnalyzer` does not exist.

- [ ] **Step 3: Write the implementation**

`gen/Primitives.Analyzers/Diagnostics.cs` — replace the header doc and append two descriptors:

```csharp
/// <summary>
/// NORSE060 opened this decade for Svartálfheim; NORSE061/NORSE062 extend it (NORSE063 reserved for
/// a future generic decrypted-PII query surface, per the 2026-08-03 PII spec §4.1). The platform's
/// per-block convention: NORSE010 Asgard, NORSE011 Yggdrasil, NORSE020-021/NORSE022-029 Midgard,
/// NORSE030-034 Urðarbrunnr, NORSE040-049 reserved on paper for the well-seam-midgard-excision plan,
/// NORSE050-051 Mímisbrunnr, NORSE060-069 Svartálfheim. A fresh platform-wide grep at authoring time
/// confirmed NORSE061-NORSE069 clean.
/// </summary>
static class Diagnostics
{
	public static readonly DiagnosticDescriptor ResultInServiceResponse = new(
		"NORSE060", "Result<T> reachable in a [ServiceContract] response",
		"Member '{0}' on '{1}' is typed Result<T>, reachable from the response of '{2}.{3}' — Result<T> is deserialization-only and must never appear on a service response payload", "Norse.Primitives",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	public static readonly DiagnosticDescriptor PiiWithoutRetentionPolicy = new(
		"NORSE061", "PII property has no [RetentionPolicy] declaration",
		"PII property '{0}' on persisted entity '{1}' has no [RetentionPolicy] declaration — every persisted PII field names its retention basis at compile time", "Norse.Primitives",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	public static readonly DiagnosticDescriptor PiiNotDirectScalar = new(
		"NORSE062", "PII must be a direct scalar property of the persisted entity",
		"PII type '{0}' is reachable through member '{1}' on persisted entity '{2}' but is not a direct scalar property — PII persists only in direct scalar columns where the encrypting converter reaches; project the masked string instead", "Norse.Primitives",
		DiagnosticSeverity.Error, isEnabledByDefault: true);
}
```

`gen/Primitives.Analyzers/WellKnownTypes.cs` — add three resolved symbols following the existing shape: `IMaskedValue` (`Norse.Primitives.Pii.IMaskedValue`), `RetentionPolicyAttribute` (`Norse.Primitives.Pii.RetentionPolicyAttribute`), `NorseEntity` (`` Norse.Persistence.EntityFramework.INorseEntity`1 ``). Keep the existing members untouched; if `Resolve` currently returns null when any symbol is missing, split it: the NORSE060 symbol set and the NORSE061/062 symbol set resolve independently so a compilation without `ServiceContractAttribute` still runs the retention analyzer and vice versa (two nested nullable structs or two `Resolve` methods — implementer's choice, existing tests must stay green).

`gen/Primitives.Analyzers/PiiCompositionWalker.cs`:

```csharp
using Microsoft.CodeAnalysis;

namespace Norse.Primitives.Analyzers;

/// <summary>
/// BFS over the composition closure of a property type — public instance properties, collection and
/// array element types, cycle-safe — answering one question: is an <c>IMaskedValue</c> implementer
/// reachable anywhere inside? Mirrors <see cref="ResponseClosureWalker"/>; skips <c>string</c>,
/// primitives, and framework special types.
/// </summary>
static class PiiCompositionWalker
{
	public static INamedTypeSymbol? FindReachablePii(ITypeSymbol root, INamedTypeSymbol maskedValue)
	{
		HashSet<ITypeSymbol> visited = new(SymbolEqualityComparer.Default);
		Queue<ITypeSymbol> queue = new();
		queue.Enqueue(root);
		while (queue.Count > 0)
		{
			var current = Unwrap(queue.Dequeue());
			if (!visited.Add(current) || current.SpecialType != SpecialType.None)
				continue;
			if (Implements(current, maskedValue))
				return current as INamedTypeSymbol;
			foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
			{
				if (property is { IsStatic: false, DeclaredAccessibility: Accessibility.Public })
					queue.Enqueue(property.Type);
			}
		}
		return null;
	}

	public static bool Implements(ITypeSymbol type, INamedTypeSymbol maskedValue) =>
		type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, maskedValue));

	public static ITypeSymbol Unwrap(ITypeSymbol type)
	{
		if (type is IArrayTypeSymbol array)
			return Unwrap(array.ElementType);
		if (type is INamedTypeSymbol named)
		{
			if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
				return Unwrap(named.TypeArguments[0]);
			var enumerable = named.AllInterfaces
				.FirstOrDefault(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
			if (enumerable is not null && named.SpecialType != SpecialType.System_String)
				return Unwrap(enumerable.TypeArguments[0]);
		}
		return type;
	}
}
```

`gen/Primitives.Analyzers/RetentionPolicyAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Norse.Primitives.Analyzers;

/// <summary>
/// NORSE061/NORSE062 — the compile-time retention gate (2026-08-03 PII spec §5). Roots are types
/// implementing <c>INorseEntity&lt;TSelf&gt;</c>. A direct property whose (nullable-unwrapped) type
/// implements <c>IMaskedValue</c> must carry <c>[RetentionPolicy]</c> (NORSE061). An
/// <c>IMaskedValue</c> implementer reachable any other way — nested composition, collection element,
/// array — is banned outright (NORSE062): the encrypting value converter operates per scalar column,
/// so nested PII would serialize into JSON documents as plaintext, a shredder escape no attribute cures.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RetentionPolicyAnalyzer : DiagnosticAnalyzer
{
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		[Diagnostics.PiiWithoutRetentionPolicy, Diagnostics.PiiNotDirectScalar];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static start =>
		{
			var maskedValue = start.Compilation.GetTypeByMetadataName("Norse.Primitives.Pii.IMaskedValue");
			var retentionPolicy = start.Compilation.GetTypeByMetadataName("Norse.Primitives.Pii.RetentionPolicyAttribute");
			var norseEntity = start.Compilation.GetTypeByMetadataName("Norse.Persistence.EntityFramework.INorseEntity`1");
			if (maskedValue is null || retentionPolicy is null || norseEntity is null)
				return;
			start.RegisterSymbolAction(
				context => AnalyzeType(context, maskedValue, retentionPolicy, norseEntity),
				SymbolKind.NamedType);
		});
	}

	static void AnalyzeType(SymbolAnalysisContext context, INamedTypeSymbol maskedValue,
		INamedTypeSymbol retentionPolicy, INamedTypeSymbol norseEntity)
	{
		var type = (INamedTypeSymbol)context.Symbol;
		if (!type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, norseEntity)))
			return;

		foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
		{
			if (property is not { IsStatic: false, DeclaredAccessibility: Accessibility.Public })
				continue;

			// Explicit three-way, in law order. (1) Nullable<T> unwraps to a DIRECT scalar — the
			// only wrapper that stays scalar; arrays route through IArrayTypeSymbol and collections
			// through IEnumerable<T>, so neither can sneak into this branch.
			var scalarType = property.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ?
				nullable.TypeArguments[0] :
				property.Type;
			if (PiiCompositionWalker.Implements(scalarType, maskedValue))
			{
				if (!HasRetentionPolicy(property, retentionPolicy))
					Report(context, Diagnostics.PiiWithoutRetentionPolicy, property, property.Name, type.Name);
				continue;
			}

			// (2) Array/collection whose element (transitively unwrapped) is PII — banned, no cure.
			var element = PiiCompositionWalker.Unwrap(scalarType);
			if (!SymbolEqualityComparer.Default.Equals(element, scalarType) &&
				PiiCompositionWalker.Implements(element, maskedValue))
			{
				Report(context, Diagnostics.PiiNotDirectScalar, property, element.Name, property.Name, type.Name);
				continue;
			}

			// (3) PII hiding anywhere inside the composed type's closure — banned, no cure.
			if (PiiCompositionWalker.FindReachablePii(element, maskedValue) is { } nested)
				Report(context, Diagnostics.PiiNotDirectScalar, property, nested.Name, property.Name, type.Name);
		}
	}

	static bool HasRetentionPolicy(IPropertySymbol property, INamedTypeSymbol retentionPolicy) =>
		property.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, retentionPolicy));

	static void Report(SymbolAnalysisContext context, DiagnosticDescriptor descriptor,
		IPropertySymbol property, params object[] args) =>
		context.ReportDiagnostic(Diagnostic.Create(descriptor, property.Locations[0], args));
}
```

Implementer note: the three-way above is deliberate law order — `TestEmail? Email` is direct scalar (NORSE061 territory); `TestEmail[]`/`ICollection<TestEmail>` is NORSE062 with no attribute cure (the array fixture test exists precisely because `IArrayTypeSymbol` is not an `INamedTypeSymbol` and a named-type-only guard misroutes it). The tests are the authority.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Primitives.Analyzers.Tests` (full project — NORSE060 tests must stay green too)
Expected: PASS, all.

- [ ] **Step 5: Run the full realm build and test suite**

Run: `dotnet build Svartalfheim.slnx && dotnet test Svartalfheim.slnx`
Expected: zero warnings (warnings are errors), all green.

- [ ] **Step 6: Commit**

```bash
git add gen/Primitives.Analyzers/Diagnostics.cs gen/Primitives.Analyzers/WellKnownTypes.cs gen/Primitives.Analyzers/RetentionPolicyAnalyzer.cs gen/Primitives.Analyzers/PiiCompositionWalker.cs tests/Primitives.Analyzers.Tests/RetentionPolicyAnalyzerTests.cs
git commit -m "feat: NORSE061/NORSE062 retention-policy analyzer"
```

**SHIP GATE (human): Svartálfheim** — PR, CI green, tag, publish `Norse.Primitives`. Update `Svartalfheim/CLAUDE.md` + `README.md` (PII increment landed) in the PR per boy-scout law.

---

## Phase B — Asgard (`feature/erased-taxonomy-and-keys`)

### Task 7: `ErasureReceipt`, `ErrorCategory.Erased`, `Problem.Receipt`

**Files:**
- Create: `src/Abstractions.Contracts/ErasureReceipt.cs`
- Modify: `src/Abstractions.Contracts/ErrorCategory.cs`, `src/Abstractions.Contracts/Problem.cs`
- Test: `tests/Abstractions.Contracts.Tests/ProblemTests.cs` (extend existing file if present, else create)

**Interfaces (Produces):**
- `sealed record ErasureReceipt(Guid ReceiptId, DateTimeOffset SeveredAt)` — positional, in `Norse.Abstractions.Contracts`.
- `ErrorCategory.Erased = 11` — producer-agnostic: crypto-shred populates the receipt; a content tombstone carries none (spec §2.3).
- `Problem.Receipt` — `ErasureReceipt?`, init-only, default null.

- [ ] **Step 1: Write the failing test**

```csharp
namespace Norse.Abstractions.Contracts.Tests;

public sealed class ProblemTests
{
	[Fact]
	void Erased_category_claims_the_next_explicit_value()
	{
		((byte)ErrorCategory.Erased).ShouldBe((byte)11);
	}

	[Fact]
	void Problem_carries_an_optional_erasure_receipt()
	{
		ErasureReceipt receipt = new(Guid.NewGuid(), DateTimeOffset.UtcNow);
		Problem problem = new() { Category = ErrorCategory.Erased, Receipt = receipt };
		problem.Receipt.ShouldBe(receipt);
	}

	[Fact]
	void Receipt_defaults_to_null_for_every_other_category()
	{
		Problem problem = new() { Category = ErrorCategory.NotFound };
		problem.Receipt.ShouldBeNull();
	}
}
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `Asgard/`): `dotnet test tests/Abstractions.Contracts.Tests -- --filter-class "*.ProblemTests"`
Expected: FAIL — `Erased`/`ErasureReceipt`/`Receipt` do not exist.

- [ ] **Step 3: Write the implementation**

`src/Abstractions.Contracts/ErasureReceipt.cs`:

```csharp
namespace Norse.Abstractions.Contracts;

/// <summary>
/// The self-auditing proof an <see cref="ErrorCategory.Erased"/> answer carries when the producer is
/// crypto-shredding: the Syn ledger reference ("severed on X, receipt Y"). A content tombstone —
/// the other legitimate producer of <see cref="ErrorCategory.Erased"/> — carries no receipt.
/// </summary>
/// <param name="ReceiptId">The permanent Syn ledger entry identifier.</param>
/// <param name="SeveredAt">When the subject was severed.</param>
public sealed record ErasureReceipt(Guid ReceiptId, DateTimeOffset SeveredAt);
```

`src/Abstractions.Contracts/ErrorCategory.cs` — append after `MultipleMatches = 10`:

```csharp
	/// <summary>
	/// Intentionally gone: the record existed, the content was deliberately retired — the system
	/// working as designed, neither <see cref="NotFound"/> nor an incident. Producer-agnostic:
	/// crypto-shredding (per-subject key destroyed; <see cref="Problem.Receipt"/> populated) and
	/// content tombstoning (retired into temporal history; no receipt) both answer with this
	/// category, and both fold to 410 Gone at the REST edge.
	/// </summary>
	Erased = 11
```

`src/Abstractions.Contracts/Problem.cs` — append member:

```csharp
	/// <summary>
	/// The erasure proof, populated only when <see cref="Category"/> is
	/// <see cref="ErrorCategory.Erased"/> and a ledger entry exists (crypto-shred producer);
	/// <see langword="null"/> for tombstone producers and every other category.
	/// </summary>
	public ErasureReceipt? Receipt { get; init; }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Abstractions.Contracts.Tests`
Expected: PASS (existing suite green too).

- [ ] **Step 5: Commit**

```bash
git checkout -b feature/erased-taxonomy-and-keys
git add src/Abstractions.Contracts/ErasureReceipt.cs src/Abstractions.Contracts/ErrorCategory.cs src/Abstractions.Contracts/Problem.cs tests/Abstractions.Contracts.Tests/ProblemTests.cs
git commit -m "feat: ErrorCategory.Erased with optional ErasureReceipt on Problem"
```

### Task 8: The key seam contracts — `Abstractions.Backend/Keys/` (CURATED: no new assembly — the no-functional-group-packages ruling)

**Files:**
- Create: `src/Abstractions.Backend/Keys/SubjectKeyResult.cs`, `.../Keys/ISubjectKeyStore.cs`, `.../Keys/ILookupKeyRing.cs`, `.../Keys/KeyDestroyedException.cs`, `.../Keys/KeyMissingException.cs`, `.../Keys/SubjectCryptoScope.cs` — `namespace Norse.Abstractions.Backend.Keys;` (path law)
- Test: `tests/Abstractions.Backend.Tests/Keys/SubjectKeyResultTests.cs` + `SubjectCryptoScopeTests.cs` (existing project, new folder — `namespace Norse.Abstractions.Backend.Tests.Keys;`)
- Modify: `Asgard/CLAUDE.md`, `Asgard/README.md` (Backend gains the key seam; one line each). Verify `Abstractions.Backend` already references `Abstractions.Contracts` (for `ErasureReceipt`) — add the ProjectReference only if the build proves it missing (transitive-first; note the check).

**Interfaces (Produces):**

```csharp
public readonly record struct SubjectKeyResult          // seam-local closed union — NOT Outcome, NOT Result
// cases: Available(byte[] key) | Destroyed(ErasureReceipt receipt) | Missing
// default(SubjectKeyResult) is malformed → Match throws SwitchExpressionException (Result<T> precedent)

public interface ISubjectKeyStore
{
	ValueTask<SubjectKeyResult> GetAsync(Guid subjectId, CancellationToken cancellationToken = default);
	ValueTask<byte[]> GetOrCreateAsync(Guid subjectId, CancellationToken cancellationToken = default);   // throws KeyDestroyedException on a destroyed subject — a burned id NEVER re-keys
	ValueTask<ErasureReceipt> DestroyAsync(Guid subjectId, CancellationToken cancellationToken = default); // idempotent: destroying twice returns the original receipt
}

public interface ILookupKeyRing
{
	string CurrentKeyId { get; }
	IEnumerable<string> KeyIds { get; }
	byte[] GetKey(string keyId);   // throws KeyNotFoundException on an unknown id — fail loud
}

public sealed class KeyDestroyedException(ErasureReceipt receipt) : Exception { ErasureReceipt Receipt; }
public sealed class KeyMissingException(Guid subjectId) : Exception { Guid SubjectId; }

public static class SubjectCryptoScope
{
	public static Guid? CurrentSubject { get; }          // AsyncLocal-backed
	public static IDisposable Begin(Guid subjectId);     // nesting allowed; Dispose restores the prior value
}
```

`SubjectCryptoScope` exists because `IPersonalDataProtector.Protect(string)` carries no subject parameter: writers (the user store, the shred ceremony) establish the ambient subject; readers never need it (ciphertext is self-describing — Himinbjörg's envelope carries the subject id). No csproj scaffolding — the folder lands in `Abstractions.Backend`; verify its reference to `Abstractions.Contracts` (for `ErasureReceipt`) and add the ProjectReference only if the build proves it missing.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace Norse.Abstractions.Backend.Tests.Keys;

public sealed class SubjectKeyResultTests
{
	[Fact]
	void Match_routes_the_available_case()
	{
		var result = SubjectKeyResult.Available([1, 2, 3]);
		result.Match(key => key.Length, _ => -1, () => -2).ShouldBe(3);
	}

	[Fact]
	void Match_routes_the_destroyed_case_with_its_receipt()
	{
		ErasureReceipt receipt = new(Guid.NewGuid(), DateTimeOffset.UtcNow);
		var result = SubjectKeyResult.Destroyed(receipt);
		result.Match(_ => Guid.Empty, r => r.ReceiptId, () => Guid.Empty).ShouldBe(receipt.ReceiptId);
	}

	[Fact]
	void Match_routes_the_missing_case()
	{
		SubjectKeyResult.Missing.Match(_ => "available", _ => "destroyed", () => "missing").ShouldBe("missing");
	}

	[Fact]
	void Match_throws_on_the_malformed_default()
	{
		SubjectKeyResult malformed = default;
		Should.Throw<SwitchExpressionException>(() => malformed.Match(_ => 0, _ => 0, () => 0));
	}

	[Fact]
	void Available_rejects_an_empty_key()
	{
		Should.Throw<ArgumentException>(() => SubjectKeyResult.Available([]));
	}
}

public sealed class SubjectCryptoScopeTests
{
	[Fact]
	void Current_subject_is_null_outside_any_scope() =>
		SubjectCryptoScope.CurrentSubject.ShouldBeNull();

	[Fact]
	void Begin_establishes_and_dispose_restores_the_ambient_subject()
	{
		var outer = Guid.NewGuid();
		var inner = Guid.NewGuid();
		using (SubjectCryptoScope.Begin(outer))
		{
			SubjectCryptoScope.CurrentSubject.ShouldBe(outer);
			using (SubjectCryptoScope.Begin(inner))
				SubjectCryptoScope.CurrentSubject.ShouldBe(inner);
			SubjectCryptoScope.CurrentSubject.ShouldBe(outer);
		}
		SubjectCryptoScope.CurrentSubject.ShouldBeNull();
	}

	[Fact]
	async Task Ambient_subject_flows_across_await()
	{
		var subject = Guid.NewGuid();
		using (SubjectCryptoScope.Begin(subject))
		{
			await Task.Yield();
			SubjectCryptoScope.CurrentSubject.ShouldBe(subject);
		}
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Abstractions.Backend.Tests -- --filter-class "*.Keys.*"`
Expected: FAIL — the types do not exist (both projects already exist; the new `Keys/` folders are all that lands).

- [ ] **Step 3: Write the implementation**

`src/Abstractions.Backend/Keys/SubjectKeyResult.cs`:

```csharp
namespace Norse.Abstractions.Backend.Keys;

/// <summary>
/// The key seam's honesty contract, a seam-local closed three-state union: the repository's honesty
/// depends on the vault's honesty (2026-08-03 PII spec §3.1). <c>Available</c> carries the unwrapped
/// DEK; <c>Destroyed</c> carries the Syn receipt — the erased path; <c>Missing</c> — no key and no
/// receipt — is the incident path, never erasure. Deliberately neither <c>Result&lt;T&gt;</c> nor
/// <c>Outcome&lt;T&gt;</c>: the two unions never grow domain-specific arms.
/// </summary>
/// <remarks><c>default(SubjectKeyResult)</c> is malformed; <see cref="Match"/> throws on it.</remarks>
public readonly record struct SubjectKeyResult
{
	enum State : byte
	{
		Unspecified = 0,
		Available = 1,
		Destroyed = 2,
		Missing = 3
	}

	readonly byte[]? _key;
	readonly ErasureReceipt? _receipt;
	readonly State _state;

	SubjectKeyResult(byte[]? key, ErasureReceipt? receipt, State state) =>
		(_key, _receipt, _state) = (key, receipt, state);

	/// <summary>The key exists and is unwrapped.</summary>
	/// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
	public static SubjectKeyResult Available(byte[] key)
	{
		ArgumentNullException.ThrowIfNull(key);
		return key.Length == 0 ?
			throw new ArgumentException("An available key cannot be empty.", nameof(key)) :
			new(key, null, State.Available);
	}

	/// <summary>The key was deliberately destroyed; the receipt proves it.</summary>
	public static SubjectKeyResult Destroyed(ErasureReceipt receipt)
	{
		ArgumentNullException.ThrowIfNull(receipt);
		return new(null, receipt, State.Destroyed);
	}

	/// <summary>No key and no receipt — the incident path.</summary>
	public static SubjectKeyResult Missing { get; } = new(null, null, State.Missing);

	/// <summary>The single consumption door — three arms, exhaustive.</summary>
	/// <exception cref="SwitchExpressionException">The malformed <c>default</c> instance.</exception>
	public TResult Match<TResult>(Func<byte[], TResult> available, Func<ErasureReceipt, TResult> destroyed, Func<TResult> missing) =>
		_state switch
		{
			State.Available => available(_key!),
			State.Destroyed => destroyed(_receipt!),
			State.Missing => missing(),
			_ => throw new SwitchExpressionException(_state)
		};
}
```

(Add `using System.Runtime.CompilerServices;` for `SwitchExpressionException`.)

`src/Abstractions.Backend/Keys/ISubjectKeyStore.cs`:

```csharp
namespace Norse.Abstractions.Backend.Keys;

/// <summary>
/// The payload-plane key seam: custody, wrap/unwrap, and scheduled destruction of per-subject DEKs.
/// Algorithm choices are the platform's (AES-256-GCM), never the provider's — the seam is custody.
/// </summary>
public interface ISubjectKeyStore
{
	/// <summary>The three-state honest read: available, destroyed-with-receipt, or missing.</summary>
	ValueTask<SubjectKeyResult> GetAsync(Guid subjectId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Returns the subject's DEK, minting one for a new subject. A destroyed subject never re-keys —
	/// re-registration is a new subject id, so this throws rather than resurrect.
	/// </summary>
	/// <exception cref="KeyDestroyedException">The subject's key was deliberately destroyed.</exception>
	ValueTask<byte[]> GetOrCreateAsync(Guid subjectId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Destroys the subject's key and returns the receipt. Idempotent: a second destruction returns
	/// the original receipt — the ledger records one severance.
	/// </summary>
	ValueTask<ErasureReceipt> DestroyAsync(Guid subjectId, CancellationToken cancellationToken = default);
}
```

`src/Abstractions.Backend/Keys/ILookupKeyRing.cs`:

```csharp
namespace Norse.Abstractions.Backend.Keys;

/// <summary>
/// The lookup-plane keyring: service-level, rotatable, producing the keys blind indexes are HMAC'd
/// under. Deliberately not per-subject — you must find the user before you know whose key to use.
/// Rotation is a re-hash ceremony over all current rows, never a config flip.
/// </summary>
public interface ILookupKeyRing
{
	/// <summary>The key id new blind indexes are written under.</summary>
	string CurrentKeyId { get; }

	/// <summary>Every key id the ring can still answer for (rotation window).</summary>
	IEnumerable<string> KeyIds { get; }

	/// <summary>Resolves a key by id.</summary>
	/// <exception cref="KeyNotFoundException">The id is not on the ring.</exception>
	byte[] GetKey(string keyId);
}
```

`src/Abstractions.Backend/Keys/KeyDestroyedException.cs`:

```csharp
namespace Norse.Abstractions.Backend.Keys;

/// <summary>
/// Thrown when decryption meets a deliberately destroyed key — the materialization channel for the
/// seam's <c>Destroyed</c> state (an EF value converter has no return path for a union). Machinery-
/// internal: the disclosure repository's fold catches it and answers <c>ErrorCategory.Erased</c>
/// with the receipt; one that escapes lands in the unhandled-exception interceptor as an honest Fault.
/// </summary>
public sealed class KeyDestroyedException(ErasureReceipt receipt) :
	Exception($"Subject key deliberately destroyed at {receipt.SeveredAt:O}; receipt {receipt.ReceiptId}.")
{
	/// <summary>The Syn ledger proof.</summary>
	public ErasureReceipt Receipt { get; } = receipt;
}
```

`src/Abstractions.Backend/Keys/KeyMissingException.cs`:

```csharp
namespace Norse.Abstractions.Backend.Keys;

/// <summary>
/// Thrown when decryption meets a key that should exist and does not — no key, no receipt. An
/// incident: it pages someone; it never masquerades as erasure. Deliberately uncaught by the
/// disclosure fold so the exception-translation behavior renders it a Fault with a correlation id.
/// </summary>
public sealed class KeyMissingException(Guid subjectId) :
	Exception($"Subject key for {subjectId} is missing with no destruction receipt — incident, not erasure.")
{
	/// <summary>The subject whose key is missing.</summary>
	public Guid SubjectId { get; } = subjectId;
}
```

`src/Abstractions.Backend/Keys/SubjectCryptoScope.cs`:

```csharp
namespace Norse.Abstractions.Backend.Keys;

/// <summary>
/// The ambient write-subject for payload encryption. Exists because
/// <c>IPersonalDataProtector.Protect(string)</c> carries no subject parameter: writers (the user
/// store, the shred ceremony) establish the subject around the operation; readers never need it —
/// ciphertext is self-describing. A protector asked to encrypt with no ambient subject fails loudly.
/// </summary>
public static class SubjectCryptoScope
{
	static readonly AsyncLocal<Guid?> Ambient = new();

	/// <summary>The ambient subject, or <see langword="null"/> outside any scope.</summary>
	public static Guid? CurrentSubject =>
		Ambient.Value;

	/// <summary>Establishes the ambient subject; disposing restores the prior value (nesting allowed).</summary>
	public static IDisposable Begin(Guid subjectId)
	{
		var prior = Ambient.Value;
		Ambient.Value = subjectId;
		return new Scope(prior);
	}

	sealed class Scope(Guid? prior) : IDisposable
	{
		public void Dispose() =>
			Ambient.Value = prior;
	}
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Abstractions.Backend.Tests -- --filter-class "*.Keys.*"`
Expected: PASS.

- [ ] **Step 5: Update realm docs and commit**

Update `Asgard/CLAUDE.md` §1 and `README.md`: `Abstractions.Backend` gains the key seam under `Keys/` (one line each, matching doc voice).

```bash
git add src/Abstractions.Backend/Keys tests/Abstractions.Backend.Tests/Keys CLAUDE.md README.md
git commit -m "feat: the key seam contracts land in Abstractions.Backend/Keys — three-state honesty union"
```

### Task 9: REST fold — Erased → 410 Gone with receipt extensions

**Files:**
- Modify: `src/Abstractions.Web.Server/Facade/GrpcControllerBase.cs`
- Test: `tests/Abstractions.Web.Server.Tests/GrpcControllerBaseTests.cs` (extend the existing fold tests)

**Interfaces:**
- Consumes: Task 7's `ErrorCategory.Erased`, `Problem.Receipt`.
- Produces: HTTP 410 with problem extensions `receipt` (the `Guid`) and `severedAt` (ISO round-trip `"O"` string) — matching the shapes `ProblemXmlWriter`'s scalar default already renders, so no XML writer change is needed (Task 11 proves it).

- [ ] **Step 1: Write the failing test** (mirror the existing fold-test arrangement in that file — a derived test controller invoking `FoldAsync`; follow the file's current fixture pattern exactly)

```csharp
	[Fact]
	async Task Erased_folds_to_410_gone_with_receipt_extensions()
	{
		ErasureReceipt receipt = new(Guid.NewGuid(), new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero));
		Outcome<string> outcome = new(new Failed(new Problem { Category = ErrorCategory.Erased, Receipt = receipt }));
		var result = await InvokeFoldAsync(outcome);
		var problemResult = result.Result.ShouldBeOfType<ObjectResult>();
		problemResult.StatusCode.ShouldBe(StatusCodes.Status410Gone);
		var details = problemResult.Value.ShouldBeOfType<ProblemDetails>();
		details.Extensions["receipt"].ShouldBe(receipt.ReceiptId);
		details.Extensions["severedAt"].ShouldBe("2026-08-03T12:00:00.0000000+00:00");
	}

	[Fact]
	async Task Erased_without_a_receipt_still_folds_to_410_gone()
	{
		Outcome<string> outcome = new(new Failed(new Problem { Category = ErrorCategory.Erased }));
		var result = await InvokeFoldAsync(outcome);
		var problemResult = result.Result.ShouldBeOfType<ObjectResult>();
		problemResult.StatusCode.ShouldBe(StatusCodes.Status410Gone);
		problemResult.Value.ShouldBeOfType<ProblemDetails>().Extensions.ShouldNotContainKey("receipt");
	}
```

(`InvokeFoldAsync` = whatever helper the existing tests use to call the protected fold on a test controller; reuse it.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Abstractions.Web.Server.Tests -- --filter-class "*.GrpcControllerBaseTests"`
Expected: FAIL — Erased currently falls into the `_ => 500` arm.

- [ ] **Step 3: Implement**

In `ToProblemResult`, add the switch arm after `MultipleMatches`:

```csharp
				ErrorCategory.Erased => StatusCodes.Status410Gone,                         // gRPC NotFound — ErrorInfo.Reason carries the authoritative "Erased"
```

and after the `CorrelationId` block:

```csharp
			if (problem.Receipt is { } receipt)
			{
				extensions ??= [];
				extensions["receipt"] = receipt.ReceiptId;
				extensions["severedAt"] = receipt.SeveredAt.ToString("O", CultureInfo.InvariantCulture);
			}
```

(hoist `using System.Globalization;`). Update the class-level doc comment's mapping sentence to note Erased → 410/NotFound and that the two folds still agree state-for-state.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Abstractions.Web.Server.Tests`
Expected: PASS, full project.

- [ ] **Step 5: Commit**

```bash
git add src/Abstractions.Web.Server/Facade/GrpcControllerBase.cs tests/Abstractions.Web.Server.Tests/GrpcControllerBaseTests.cs
git commit -m "feat: fold ErrorCategory.Erased to 410 Gone with receipt extensions"
```

**SHIP GATE (human): Asgard** — PR, CI, tag, publish (`Abstractions.Contracts`, `Abstractions.Backend`, `Abstractions.Web.Server`).

---

## Phase C — Midgard (`feature/erased-edges-and-dev-keys`)

### Task 10: gRPC edge — Erased across the wire, both directions

**Files:**
- Modify: `src/Infrastructure.Web.Server/Mediator/Grpc/ProblemExtensions.cs`, `src/Infrastructure.Web.Client/Grpc/RpcExceptionExtensions.cs`
- Test: extend `tests/Infrastructure.Web.Server.Tests/.../ProblemExtensionsTests.cs` and `tests/Infrastructure.Web.Client.Tests/.../RpcExceptionExtensionsTests.cs` (locate the existing test files for these types and follow their fixture patterns)

**Interfaces:**
- Produces: server — `Erased` → `StatusCode.NotFound` with `ErrorInfo.Reason = "Erased"` and, when a receipt exists, `ErrorInfo.Metadata["receipt"]` (Guid `"D"` format) + `ErrorInfo.Metadata["severedAt"]` (`"O"` format). Client — `DecodeProblem` rehydrates `Problem.Receipt` from those two metadata entries; absent entries → null receipt (tombstone producer).

- [ ] **Step 1: Write the failing tests**

Server side:

```csharp
	[Fact]
	void Erased_maps_to_not_found_status_with_receipt_metadata()
	{
		ErasureReceipt receipt = new(Guid.NewGuid(), new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero));
		Problem problem = new() { Category = ErrorCategory.Erased, Receipt = receipt };
		var exception = problem.ToRpcException();
		exception.StatusCode.ShouldBe(StatusCode.NotFound);
		var errorInfo = DecodeErrorInfo(exception); // existing test helper reading grpc-status-details-bin
		errorInfo.Reason.ShouldBe("Erased");
		errorInfo.Metadata["receipt"].ShouldBe(receipt.ReceiptId.ToString("D"));
		errorInfo.Metadata["severedAt"].ShouldBe("2026-08-03T12:00:00.0000000+00:00");
	}

	[Fact]
	void Erased_without_a_receipt_carries_no_receipt_metadata()
	{
		Problem problem = new() { Category = ErrorCategory.Erased };
		var errorInfo = DecodeErrorInfo(problem.ToRpcException());
		errorInfo.Reason.ShouldBe("Erased");
		errorInfo.Metadata.ShouldNotContainKey("receipt");
	}
```

Client side:

```csharp
	[Fact]
	void Decode_rehydrates_the_erasure_receipt_from_error_info_metadata()
	{
		// Build the trailer exactly as the server does (reuse/extend the existing round-trip helper).
		ErasureReceipt receipt = new(Guid.NewGuid(), new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero));
		var problem = new Problem { Category = ErrorCategory.Erased, Receipt = receipt }
			.ToRpcException()
			.DecodeProblem();
		problem.Category.ShouldBe(ErrorCategory.Erased);
		problem.Receipt.ShouldBe(receipt);
	}

	[Fact]
	void Decode_leaves_receipt_null_when_metadata_is_absent()
	{
		new Problem { Category = ErrorCategory.Erased }
			.ToRpcException()
			.DecodeProblem()
			.Receipt.ShouldBeNull();
	}
```

Note: the client test project already references the server project's `ProblemExtensions` for round-trip tests, or constructs trailers by hand — follow whichever the existing round-trip tests do; if neither exists, construct the `RpcException` via `ProblemExtensions` (add the server project reference to the client *test* project only).

- [ ] **Step 2: Run tests to verify they fail**

Run (from `Midgard/`): `dotnet test tests/Infrastructure.Web.Server.Tests` and `dotnet test tests/Infrastructure.Web.Client.Tests`
Expected: FAIL — Erased falls into `_ => StatusCode.Unknown`; no metadata; receipt never rehydrated.

- [ ] **Step 3: Implement**

`ProblemExtensions.cs` — add the switch arm:

```csharp
					ErrorCategory.Erased => StatusCode.NotFound,
```

and replace the `ErrorInfo` construction with:

```csharp
				ErrorInfo errorInfo = new()
				{
					Reason = problem.Category.ToString(),
					Domain = ErrorInfoDomain
				};
				if (problem.Receipt is { } receipt)
				{
					errorInfo.Metadata.Add("receipt", receipt.ReceiptId.ToString("D"));
					errorInfo.Metadata.Add("severedAt", receipt.SeveredAt.ToString("O", CultureInfo.InvariantCulture));
				}
				richStatus.Details.Add(Any.Pack(errorInfo));
```

(hoist `using System.Globalization;`).

`RpcExceptionExtensions.cs` — in the `ErrorInfo` branch, after `category = parsed;`:

```csharp
						if (errorInfo.Metadata.TryGetValue("receipt", out var receiptId) &&
							errorInfo.Metadata.TryGetValue("severedAt", out var severedAt) &&
							Guid.TryParse(receiptId, out var parsedReceiptId) &&
							DateTimeOffset.TryParse(severedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedSeveredAt))
						{
							receipt = new ErasureReceipt(parsedReceiptId, parsedSeveredAt);
						}
```

declaring `ErasureReceipt? receipt = null;` beside `correlationId` and adding `Receipt = receipt` to the returned `Problem` (hoist `using System.Globalization;`). Update both files' doc comments (the "nine members" count line in the client doc is stale either way — make it "across all members").

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Infrastructure.Web.Server.Tests && dotnet test tests/Infrastructure.Web.Client.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git checkout -b feature/erased-edges-and-dev-keys
git add src/Infrastructure.Web.Server/Mediator/Grpc/ProblemExtensions.cs src/Infrastructure.Web.Client/Grpc/RpcExceptionExtensions.cs tests/Infrastructure.Web.Server.Tests tests/Infrastructure.Web.Client.Tests
git commit -m "feat: Erased category crosses the gRPC edge with receipt metadata"
```

### Task 11: XML edge — prove the receipt extensions render; retire the stale promise

**Files:**
- Modify: `src/Infrastructure.Web.Server/Xml/ProblemXmlWriter.cs` (remarks comment only — the fold emits `receipt` as a `Guid` scalar and `severedAt` as a pre-formatted string, both handled by the existing scalar default; the promised "own case" turned out unnecessary)
- Test: extend `tests/Infrastructure.Web.Server.Tests/.../ProblemXmlWriterTests.cs`

- [ ] **Step 1: Write the failing-or-green test** (this may pass immediately — that is the point being proven; the remarks edit is the deliverable either way)

```csharp
	[Fact]
	void Writes_receipt_and_severed_at_extension_scalars()
	{
		var receiptId = Guid.NewGuid();
		ProblemDetails problem = new()
		{
			Title = "Erased",
			Status = 410,
			Extensions =
			{
				["receipt"] = receiptId,
				["severedAt"] = "2026-08-03T12:00:00.0000000+00:00"
			}
		};
		var xml = WriteToString(problem); // existing test helper
		xml.ShouldContain($"<receipt>{receiptId}</receipt>");
		xml.ShouldContain("<severedAt>2026-08-03T12:00:00.0000000+00:00</severedAt>");
	}
```

- [ ] **Step 2: Run, confirm green; update the remarks**

Replace the `<remarks>` sentence "A future extension member (the `Erased` 410 fold's Syn receipt reference, spec §4.3) gets its own case added here when it ships…" with: "The `Erased` 410 fold's receipt extensions (2026-08-03 PII spec §2.4) ship as two scalars — a `Guid` and a pre-formatted round-trip timestamp string — deliberately, so the scalar default renders them and no bespoke case was ever needed."

- [ ] **Step 3: Run tests, commit**

Run: `dotnet test tests/Infrastructure.Web.Server.Tests`

```bash
git add src/Infrastructure.Web.Server/Xml/ProblemXmlWriter.cs tests/Infrastructure.Web.Server.Tests
git commit -m "test: prove Erased receipt extensions render as RFC 9457 XML scalars"
```

### Task 12: The dev-grade key provider — `Infrastructure.Backend/Keys/` (CURATED: no new project — the no-functional-group-packages ruling)

**Files:**
- Create: `src/Infrastructure.Backend/Keys/DevelopmentSubjectKeyStore.cs`, `src/Infrastructure.Backend/Keys/ServiceCollectionExtensions.cs` — `namespace Norse.Infrastructure.Backend.Keys;` (path law)
- Test: `tests/Infrastructure.Backend.Tests/Keys/DevelopmentSubjectKeyStoreTests.cs` (existing project, new folder)
- Modify: `Midgard/CLAUDE.md`, `Midgard/README.md`

**Interfaces:**
- Consumes: Asgard's `ISubjectKeyStore`, `ILookupKeyRing`, `SubjectKeyResult`, `KeyDestroyedException`, `ErasureReceipt` — `Infrastructure.Backend` already NorseRefs `Abstractions.Backend`; no csproj change (verify `Abstractions.Contracts` flows transitively for `ErasureReceipt`; note the check).
- Produces: `sealed class DevelopmentSubjectKeyStore : ISubjectKeyStore, ILookupKeyRing` — file-backed under a root directory so local identities survive restarts; **dev-grade, never a production path** (keys at rest unwrapped — the class doc says so loudly). Layout: `{root}/{subjectId:N}.key` (32 random bytes via `RandomNumberGenerator`), `{root}/{subjectId:N}.receipt` (JSON `{"receiptId":"...","severedAt":"..."}`), `{root}/lookup.json` (`{"current":"k1","keys":{"k1":"<base64 32 bytes>"}}`, auto-minted on first touch). Destroy = **delete the key file** (unrecoverable from current state) + write the receipt file; `GetAsync` on receipt-only → `Destroyed`; `GetOrCreateAsync` on receipt-only → `throw KeyDestroyedException`; destroy twice → original receipt (idempotent). `AddNorseDevelopmentKeys(this IServiceCollection services, string rootPath)` registers the singleton under both interfaces.

- [ ] **Step 1: Write the failing tests** (each test gets its own temp directory under the test's scratch; delete in `Dispose`)

```csharp
namespace Norse.Infrastructure.Backend.Tests.Keys;

public sealed class DevelopmentSubjectKeyStoreTests : IDisposable
{
	readonly string _root = Path.Combine(Path.GetTempPath(), $"norse-keys-{Guid.NewGuid():N}");
	// Deliberately mints a fresh instance per access: correct ONLY because the store is file-backed,
	// so state lives on disk, not in the instance. An in-memory refactor would silently break every
	// multi-access test here — this comment is the tripwire.
	DevelopmentSubjectKeyStore Store => new(_root);

	public void Dispose() =>
		Directory.Delete(_root, recursive: true);

	[Fact]
	async Task Get_or_create_mints_a_32_byte_key_and_get_returns_it()
	{
		var subject = Guid.NewGuid();
		var key = await Store.GetOrCreateAsync(subject, TestContext.Current.CancellationToken);
		key.Length.ShouldBe(32);
		var result = await Store.GetAsync(subject, TestContext.Current.CancellationToken);
		result.Match(k => k, _ => null!, () => null!).ShouldBe(key);
	}

	[Fact]
	async Task Get_returns_missing_for_an_unknown_subject()
	{
		var result = await Store.GetAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
		result.Match(_ => "available", _ => "destroyed", () => "missing").ShouldBe("missing");
	}

	[Fact]
	async Task Keys_survive_a_store_recreate()
	{
		var subject = Guid.NewGuid();
		var key = await Store.GetOrCreateAsync(subject, TestContext.Current.CancellationToken);
		DevelopmentSubjectKeyStore reopened = new(_root);
		var result = await reopened.GetAsync(subject, TestContext.Current.CancellationToken);
		result.Match(k => k, _ => null!, () => null!).ShouldBe(key);
	}

	[Fact]
	async Task Destroy_deletes_the_key_material_and_answers_destroyed_with_the_receipt()
	{
		var subject = Guid.NewGuid();
		await Store.GetOrCreateAsync(subject, TestContext.Current.CancellationToken);
		var receipt = await Store.DestroyAsync(subject, TestContext.Current.CancellationToken);
		File.Exists(Path.Combine(_root, $"{subject:N}.key")).ShouldBeFalse(); // unrecoverable from current state
		var result = await Store.GetAsync(subject, TestContext.Current.CancellationToken);
		result.Match(_ => Guid.Empty, r => r.ReceiptId, () => Guid.Empty).ShouldBe(receipt.ReceiptId);
	}

	[Fact]
	async Task Destroy_is_idempotent_and_returns_the_original_receipt()
	{
		var subject = Guid.NewGuid();
		await Store.GetOrCreateAsync(subject, TestContext.Current.CancellationToken);
		var first = await Store.DestroyAsync(subject, TestContext.Current.CancellationToken);
		var second = await Store.DestroyAsync(subject, TestContext.Current.CancellationToken);
		second.ShouldBe(first);
	}

	[Fact]
	async Task Destruction_survives_a_store_recreate_and_a_destroyed_subject_never_rekeys()
	{
		// Verify item 9 at dev-store scope: the receipt is durable, the key is gone, and
		// GetOrCreate refuses resurrection. The production provider owns the backup-window SLA.
		var subject = Guid.NewGuid();
		await Store.GetOrCreateAsync(subject, TestContext.Current.CancellationToken);
		var receipt = await Store.DestroyAsync(subject, TestContext.Current.CancellationToken);
		DevelopmentSubjectKeyStore reopened = new(_root);
		var exception = await Should.ThrowAsync<KeyDestroyedException>(
			async () => await reopened.GetOrCreateAsync(subject, TestContext.Current.CancellationToken));
		exception.Receipt.ShouldBe(receipt);
	}

	[Fact]
	async Task Lookup_ring_mints_a_current_key_and_answers_by_id()
	{
		var store = Store;
		_ = await store.GetOrCreateAsync(Guid.NewGuid(), TestContext.Current.CancellationToken); // touch → init
		store.CurrentKeyId.ShouldNotBeNullOrWhiteSpace();
		store.KeyIds.ShouldContain(store.CurrentKeyId);
		store.GetKey(store.CurrentKeyId).Length.ShouldBe(32);
	}

	[Fact]
	void Lookup_ring_throws_on_an_unknown_key_id() =>
		Should.Throw<KeyNotFoundException>(() => Store.GetKey("no-such-key"));
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test tests/Infrastructure.Backend.Tests -- --filter-class "*.Keys.*"` (no project scaffolding — the folders land in the existing Backend pair; the store class is the missing piece). `Microsoft.Extensions.DependencyInjection.Abstractions` flows transitively — do not add a direct reference (NU1510 precedent).

- [ ] **Step 3: Implement** — `DevelopmentSubjectKeyStore`: primary constructor `(string rootPath)`, `Directory.CreateDirectory` up front; lookup ring lazy-initialized on first access (mint `"k1"` + 32 random bytes, write `lookup.json`); all file I/O async where the interface is async; JSON via `System.Text.Json` with a small `sealed record ReceiptDocument(Guid ReceiptId, DateTimeOffset SeveredAt)` and `sealed record LookupDocument(string Current, Dictionary<string, string> Keys)`. Class XML doc opens with: "Dev-grade only: key material rests unwrapped on local disk. Never a production path — the production seam is a vault-backed provider." `ServiceCollectionExtensions.AddNorseDevelopmentKeys(this IServiceCollection services, string rootPath)` registers one singleton instance under both `ISubjectKeyStore` and `ILookupKeyRing`.

- [ ] **Step 4: Run tests to verify they pass** — `dotnet test tests/Infrastructure.Backend.Tests -- --filter-class "*.Keys.*"`

- [ ] **Step 5: Update realm docs and commit**

```bash
git add src/Infrastructure.Backend/Keys tests/Infrastructure.Backend.Tests/Keys CLAUDE.md README.md
git commit -m "feat: file-backed dev-grade subject key store lands in Infrastructure.Backend/Keys"
```

### Task 12b: The masked-serialization defense, relocated (CURATED: spec §1.5 layer 2 — evicted from the forge by NORSE070, lands where encodings are legal)

**Files:**
- Create: `src/Infrastructure.Web.Server/Json/MaskedValueJsonConverterFactory.cs`
- Modify: `src/Infrastructure.Web.Server/Json/MvcBuilderExtensions.cs` (register the factory beside the existing converter registrations — read the file, follow its shape exactly)
- Modify: `src/Infrastructure.Backend/Serialization/SystemTextJsonSerializer.cs` (the seam masks too: add the factory to every options variant minted in `Build`)
- Test: `tests/Infrastructure.Web.Server.Tests/.../MaskedValueJsonConverterFactoryTests.cs` (locate the existing Json test folder and follow its fixture patterns); extend `tests/Infrastructure.Backend.Tests/Serialization/SystemTextJsonSerializerTests.cs` with the seam-masking case

**Interfaces:**
- Consumes: Svartálfheim's `IMaskedValue` (`Norse.Primitives.Pii`) — foundation reference, legal everywhere. Transitive-first: verify `Norse.Primitives` flows to `Infrastructure.Web.Server` (it should, via the wire-law project) and to `Infrastructure.Backend` (likely NOT — add `<NorseRef Include="Primitives"><Repo>Svartalfheim</Repo></NorseRef>` mirroring an existing entry if the build proves it missing; note the check).
- Produces: any `IMaskedValue` value struct entering Midgard-owned JSON — the MVC pipeline or the serialization seam — renders as its `Masked` string and refuses to deserialize. Accidental egress is masked by construction; deliberate egress stays what it always was (wire contracts carry plain strings filled at the disclosure edge).

- [ ] **Step 1: Write the failing tests**

```csharp
namespace Norse.Infrastructure.Web.Server.Tests.Json;

public sealed class MaskedValueJsonConverterFactoryTests
{
	readonly record struct FakePii(string Secret) : IMaskedValue
	{
		public string Masked => "***";
		public string ToMasked(DateOnly asOf) => Masked;
	}

	static readonly JsonSerializerOptions _options = BuildOptions();

	static JsonSerializerOptions BuildOptions()
	{
		JsonSerializerOptions options = new();
		options.Converters.Add(new MaskedValueJsonConverterFactory());
		return options;
	}

	[Fact]
	void Writes_the_masked_rendering_for_any_masked_value_struct() =>
		JsonSerializer.Serialize(new FakePii("buvy@example.com"), _options).ShouldBe("\"***\"");

	[Fact]
	void Refuses_to_deserialize_because_masked_forms_can_be_valid_inputs() =>
		Should.Throw<NotSupportedException>(() => JsonSerializer.Deserialize<FakePii>("\"***\"", _options));

	[Fact]
	void Leaves_non_masked_types_untouched() =>
		JsonSerializer.Serialize(new { Name = "plain" }, _options).ShouldBe("{\"Name\":\"plain\"}");
}
```

(Hoist `using System.Text.Json;` + `using Norse.Primitives.Pii;` — this is Midgard test code; STJ is legal here and tests are law-exempt besides.) Seam case, appended to `SystemTextJsonSerializerTests`:

```csharp
	[Fact]
	void Masks_masked_value_structs_on_the_seam()
	{
		// Spec §1.5 layer 2, relocated: accidental egress through the seam renders the mask, never
		// the wire value. Deliberate egress never routes PII structs through a serializer at all.
		var json = _provider[NamingStrategy.CamelCase].Serialize(new MaskedPayload { Email = new() });
		json.ShouldContain("\"***\"");
	}
```

(with a small file-local `MaskedPayload` record carrying a `FakePii`-style `IMaskedValue` struct member — mirror the factory test's fixture.)

- [ ] **Step 2: Run to verify failure** — `dotnet test tests/Infrastructure.Web.Server.Tests -- --filter-class "*.MaskedValueJsonConverterFactoryTests"`; expected: compile error, factory missing.

- [ ] **Step 3: Implement**

`src/Infrastructure.Web.Server/Json/MaskedValueJsonConverterFactory.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Norse.Primitives.Pii;

namespace Norse.Infrastructure.Web.Server.Json;

/// <summary>
/// Defense-in-depth for serialization paths no analyzer can see (spec §1.5 layer 2, relocated here
/// from the forge by NORSE070 — encodings live inside the wire border): any <see cref="IMaskedValue"/>
/// value struct writes its masked rendering and refuses to read. Reading is refused because masked
/// forms can be syntactically valid inputs (<c>j***@d***.com</c> parses as an email address) — a
/// lossy round-trip that succeeds would fabricate a well-formed value that silently is not the
/// person's data. Wire contracts are unaffected: transports carry plain strings filled explicitly
/// at the disclosure edge.
/// </summary>
sealed class MaskedValueJsonConverterFactory : JsonConverterFactory
{
	public override bool CanConvert(Type typeToConvert) =>
		typeToConvert.IsValueType && typeof(IMaskedValue).IsAssignableFrom(typeToConvert);

	public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
		(JsonConverter)Activator.CreateInstance(typeof(MaskedValueJsonConverter<>).MakeGenericType(typeToConvert))!;

	sealed class MaskedValueJsonConverter<T> : JsonConverter<T> where T : struct, IMaskedValue
	{
		public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
			throw new NotSupportedException($"{typeToConvert.Name} is masked-write-only JSON; PII never rehydrates from JSON — parse the wire string at the boundary instead.");

		public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
			writer.WriteStringValue(value.Masked);
	}
}
```

`MvcBuilderExtensions.cs`: add the factory to the JSON options exactly where the existing converters register (one line, matching the file's chain/shape). `SystemTextJsonSerializer.Build(...)`: each minted `JsonSerializerOptions` gains `Converters = { new MaskedValueJsonConverterFactory() }`-equivalent in the file's idiom — note the factory must be reachable from `Infrastructure.Backend`; if the factory's home makes that awkward (Web.Server → Backend is the wrong direction), the factory itself moves to `Infrastructure.Backend/Serialization/` and Web.Server consumes it from there — the implementer resolves placement by the dependency direction and records the choice (Backend is the safer home: both consumers reach it).

- [ ] **Step 4: Run tests** — both touched test projects, then `dotnet build Midgard.slnx && dotnet test Midgard.slnx` zero warnings.
- [ ] **Step 5: Commit**

```bash
git add src/Infrastructure.Backend tests/Infrastructure.Backend.Tests src/Infrastructure.Web.Server/Json tests/Infrastructure.Web.Server.Tests CLAUDE.md README.md
git commit -m "feat: masked-serialization defense lands inside the wire border — IMaskedValue masks on every Midgard JSON path"
```

(Adjust the `git add` to the exact files touched once placement is resolved; realm docs gain one line if the doc voice warrants it.)

**SHIP GATE (human): Midgard** — PR, CI, tag, publish (`Infrastructure.Web.Server`, `Infrastructure.Web.Client`, `Infrastructure.Backend`).

---

## Phase D — Urðarbrunnr (`feature/protected-pii-converter`)

### Task 13: `ProtectedPiiValueConverter<TPii>` + model wiring extension

**Files:**
- Create: `src/Persistence.EntityFramework/ProtectedPiiValueConverter.cs`, `src/Persistence.EntityFramework/PiiProtectionModelExtensions.cs`
- Modify: `src/Persistence.EntityFramework/Persistence.EntityFramework.csproj` (add `<PackageReference Include="Microsoft.Extensions.Identity.Core" Version="11.*-*" />` — featherweight, carries `IPersonalDataProtector`; noted here deliberately since transitive-first would otherwise flag it)
- Test: `tests/Persistence.EntityFramework.Tests/ProtectedPiiValueConverterTests.cs`

**Interfaces:**
- Consumes: Svartálfheim's `IPiiScalar<TSelf>` (published `Norse.Primitives`; in-repo via existing `NorseRef Include="Primitives"`), `Microsoft.AspNetCore.Identity.IPersonalDataProtector`.
- Produces:
  - `sealed class ProtectedPiiValueConverter<TPii>(IPersonalDataProtector protector) : ValueConverter<TPii, string> where TPii : struct, IPiiScalar<TPii>` — to-provider: `pii => protector.Protect(pii.WireValue)`; from-provider: unprotect then `TPii.Parse`; a parse failure of *decrypted stored data* is storage corruption → `InvalidOperationException`, fail loud. Converter lambdas are expression trees: no `is` patterns inside (CS8122 — the `IdentityValueConverters` precedent); route the from-provider side through a static helper.
  - `static ModelBuilder ProtectPiiScalars(this ModelBuilder builder, IPersonalDataProtector protector)` — walks every entity type's scalar properties; any property whose (nullable-unwrapped) CLR type implements `IMaskedValue` and `IPiiScalar<>` gets the closed converter instance (`Activator.CreateInstance` on the closed generic — one-time model-build wiring, sanctioned reflection). Called by the consumer **after** `base.OnModelCreating`. Captured-protector caveat (spec verify item 4): EF caches the model per context type, so the protector instance the converter captures must be registration-shape-stable — register `IPersonalDataProtector` and its seam dependencies as **singletons** (this is exactly Identity's own `PersonalDataConverter` capture pattern; the doc comment states the constraint).

- [ ] **Step 1: Write the failing tests** (converter-level — value conversion is not database semantics, so no real DB needed here)

```csharp
namespace Norse.Persistence.EntityFramework.Tests;

public sealed class ProtectedPiiValueConverterTests
{
	// Deterministic fake: "P:" prefix marks protected payloads.
	sealed class FakeProtector : IPersonalDataProtector
	{
		public string Protect(string data) => $"P:{data}";
		public string Unprotect(string data) => data.StartsWith("P:", StringComparison.Ordinal) ?
			data[2..] :
			throw new InvalidOperationException("Not protected.");
	}

	[Fact]
	void To_provider_protects_the_wire_value()
	{
		ProtectedPiiValueConverter<EmailAddress> converter = new(new FakeProtector());
		EmailAddress.TryParse("buvy@example.com", out var email).ShouldBeTrue();
		converter.ConvertToProvider(email).ShouldBe("P:buvy@example.com");
	}

	[Fact]
	void From_provider_unprotects_and_parses_the_wire_value()
	{
		ProtectedPiiValueConverter<EmailAddress> converter = new(new FakeProtector());
		var email = (EmailAddress)converter.ConvertFromProvider("P:buvy@example.com")!;
		email.WireValue.ShouldBe("buvy@example.com");
	}

	[Fact]
	void From_provider_throws_loudly_when_decrypted_data_no_longer_parses()
	{
		ProtectedPiiValueConverter<EmailAddress> converter = new(new FakeProtector());
		Should.Throw<InvalidOperationException>(() => converter.ConvertFromProvider("P:not-an-email"));
	}

	[Fact]
	void Protect_pii_scalars_assigns_the_converter_to_direct_pii_properties()
	{
		// Model-level: a minimal context whose entity carries an EmailAddress scalar.
		var options = new DbContextOptionsBuilder<PiiFixtureContext>()
			.UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
			.Options;
		using PiiFixtureContext context = new(options, new FakeProtector());
		var property = context.Model.FindEntityType(typeof(PiiFixtureEntity))!.FindProperty(nameof(PiiFixtureEntity.Email))!;
		property.GetValueConverter().ShouldBeOfType<ProtectedPiiValueConverter<EmailAddress>>();
	}
}
```

(`PiiFixtureContext`/`PiiFixtureEntity` — small test-local context: entity with `Guid Id` + `EmailAddress Email` `[MaxLength(400)]`, `OnModelCreating` calling `modelBuilder.ProtectPiiScalars(_protector)`. If `Microsoft.EntityFrameworkCore.InMemory` isn't referenced by the test project, model inspection also works via `DbContextOptionsBuilder` + any relational provider's design-time model build — use whatever the existing tests in this project use for model-level assertions.)

- [ ] **Step 2: Run to verify failure** — `dotnet test tests/Persistence.EntityFramework.Tests -- --filter-class "*.ProtectedPiiValueConverterTests"`

- [ ] **Step 3: Implement**

`src/Persistence.EntityFramework/ProtectedPiiValueConverter.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Norse.Primitives;

namespace Norse.Persistence.EntityFramework;

/// <summary>
/// The one composed converter for a struct-typed PII scalar: canonical wire string ∘ protect on
/// write; unprotect ∘ parse on read. There is no converter ordering problem because there is no
/// second converter — the protector composes inside this one. The captured
/// <see cref="IPersonalDataProtector"/> must be a singleton over singleton seam dependencies: EF
/// caches the model per context type, so the first-resolved instance serves every request
/// (Identity's own <c>PersonalDataConverter</c> capture pattern).
/// </summary>
sealed class ProtectedPiiValueConverter<TPii>(IPersonalDataProtector protector) :
	ValueConverter<TPii, string>(
		pii => protector.Protect(pii.WireValue),
		stored => FromStore(protector, stored))
	where TPii : struct, IPiiScalar<TPii>
{
	static TPii FromStore(IPersonalDataProtector protector, string stored)
	{
		var wire = protector.Unprotect(stored);
		if (TPii.Parse(wire).TryGetValue(out Success<TPii> success))
			return success.Value;
		throw new InvalidOperationException($"Decrypted {typeof(TPii).Name} no longer parses — storage corruption; failing loudly.");
	}
}
```

`src/Persistence.EntityFramework/PiiProtectionModelExtensions.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Norse.Primitives;

namespace Norse.Persistence.EntityFramework;

/// <summary>
/// Wires <see cref="ProtectedPiiValueConverter{TPii}"/> onto every scalar property whose CLR type is
/// an <see cref="IPiiScalar{TSelf}"/> implementer. Call after <c>base.OnModelCreating</c>. One-time
/// model-build reflection — the sanctioned kind.
/// </summary>
public static class PiiProtectionModelExtensions
{
	/// <summary>Assigns the protecting converter to every direct PII scalar in the model.</summary>
	public static ModelBuilder ProtectPiiScalars(this ModelBuilder builder, IPersonalDataProtector protector)
	{
		ArgumentNullException.ThrowIfNull(protector);
		foreach (var entityType in builder.Model.GetEntityTypes())
		{
			foreach (var property in entityType.GetProperties())
			{
				var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
				if (!typeof(IMaskedValue).IsAssignableFrom(clrType) || !clrType.IsValueType)
					continue;
				var converterType = typeof(ProtectedPiiValueConverter<>).MakeGenericType(clrType);
				property.SetValueConverter((ValueConverter)Activator.CreateInstance(converterType, protector)!);
			}
		}
		return builder;
	}
}
```

(`using Microsoft.EntityFrameworkCore.Storage.ValueConversion;` for the cast. Note the guard: the filter keys on `IMaskedValue` + value type — a non-`IPiiScalar` `IMaskedValue` struct would fail `MakeGenericType`'s constraint check loudly, which is correct: implementing the marker without the round-trip contract on a persisted scalar is a design error surfacing at model build.)

- [ ] **Step 4: Run tests** — `dotnet test tests/Persistence.EntityFramework.Tests`; then full realm: `dotnet build Urdarbrunnr.slnx && dotnet test Urdarbrunnr.slnx`.

- [ ] **Step 5: Commit**

```bash
git checkout -b feature/protected-pii-converter
git add src/Persistence.EntityFramework/ProtectedPiiValueConverter.cs src/Persistence.EntityFramework/PiiProtectionModelExtensions.cs src/Persistence.EntityFramework/Persistence.EntityFramework.csproj tests/Persistence.EntityFramework.Tests/ProtectedPiiValueConverterTests.cs
git commit -m "feat: ProtectedPiiValueConverter — composed protect/parse converter for PII scalars"
```

**SHIP GATE (human): Urðarbrunnr** — PR, CI, tag, publish `Norse.Persistence.EntityFramework`.

---

## Phase E — Himinbjörg (`feature/pii-identity-erasure`)

> **COORDINATION GATE — read before Task 14.** A parallel session is landing the lockout-column `SplitToTable` structure (and the Postgres/SQL Server migrations shape that sets the table for Postgres temporality). **First step of this phase is a rebaseline: `git fetch && git rebase origin/master` (or fresh branch off updated master) and re-read `NorseUser.cs`/`NorseIdentityDbContext.cs`.** If the lockout split has landed: Task 14's split proof and Task 15's split configuration are already done — consume the landed shape, keep only what remains (subject_keys entity, nullable `NormalizedUserName` + filtered unique index, temporal sweep if absent, migrations regen). If it has not landed: execute Tasks 14–15 as written. Do not author a competing split shape either way.

### Task 14: Verify-item pinning tests (spec §8 items 1–3)

**Files:**
- Create: `tests/Identity.EntityFramework.Tests/IdentitySeamPinningTests.cs`, `tests/Identity.EntityFramework.Tests/TemporalSplitProofTests.cs`

No production code. These tests pin the platform's assumptions about Microsoft's machinery; a failure here is a **HALT** — report to Buvy, do not improvise around it (the failing assumption reshapes Tasks 15–16).

- [ ] **Step 1: Write the pinning tests**

```csharp
using Microsoft.AspNetCore.Identity;

namespace Norse.Identity.EntityFramework.Tests;

public sealed class IdentitySeamPinningTests
{
	// Spec §8 verify item 1: which columns Identity's converter path claims ([ProtectedPersonalData]
	// strings) vs the store's lookup-protector path (Normalized* — deliberately unattributed).
	[Theory]
	[InlineData(nameof(IdentityUser<Guid>.UserName))]
	[InlineData(nameof(IdentityUser<Guid>.Email))]
	[InlineData(nameof(IdentityUser<Guid>.PhoneNumber))]
	void Protected_personal_data_marks_the_payload_strings(string property) =>
		typeof(IdentityUser<Guid>).GetProperty(property)!
			.IsDefined(typeof(ProtectedPersonalDataAttribute), inherit: true).ShouldBeTrue();

	[Theory]
	[InlineData(nameof(IdentityUser<Guid>.NormalizedUserName))]
	[InlineData(nameof(IdentityUser<Guid>.NormalizedEmail))]
	void Normalized_columns_are_not_converter_protected(string property) =>
		typeof(IdentityUser<Guid>).GetProperty(property)!
			.IsDefined(typeof(ProtectedPersonalDataAttribute), inherit: true).ShouldBeFalse();
}
```

```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.Identity.EntityFramework.Tests;

public sealed class TemporalSplitProofTests
{
	// Spec §8 verify item 2: IsTemporal + SplitToTable compose on one entity in EF 11 preview.
	// Standalone scratch context — deliberately NOT NorseIdentityDbContext, so this proof holds
	// even before Task 15 wires the real mapping.
	sealed class ProofUser
	{
		public Guid Id { get; set; }
		public string? Name { get; set; }
		public int AccessFailedCount { get; set; }
		public DateTimeOffset? LockoutEnd { get; set; }
	}

	sealed class ProofContext : DbContext
	{
		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
			optionsBuilder.UseSqlServer("Server=design-time-only;Database=proof;Encrypt=false");

		protected override void OnModelCreating(ModelBuilder modelBuilder) =>
			modelBuilder.Entity<ProofUser>(entity =>
			{
				entity.Property(u => u.Name).HasMaxLength(64);
				entity.ToTable("Users", table => table.IsTemporal());
				entity.SplitToTable("UserLockout", split =>
				{
					split.Property(u => u.AccessFailedCount);
					split.Property(u => u.LockoutEnd);
				});
			});
	}

	[Fact]
	void Temporal_main_table_composes_with_a_lockout_split_table()
	{
		using ProofContext context = new();
		var entity = context.Model.FindEntityType(typeof(ProofUser))!;
		entity.IsTemporal().ShouldBeTrue();
		var lockoutMapping = entity.GetTableMappings()
			.Select(m => m.Table.Name)
			.ShouldContain("UserLockout");
		entity.FindProperty(nameof(ProofUser.AccessFailedCount))!
			.GetColumnName(Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table("UserLockout", null))
			.ShouldNotBeNull();
	}

	[Fact]
	void Split_table_is_not_itself_temporal()
	{
		// The point of the split: lockout churn mints no history rows. If EF marks the split table
		// temporal too, the design premise fails → HALT and report.
		using ProofContext context = new();
		var entity = context.Model.FindEntityType(typeof(ProofUser))!;
		// Assert via the relational annotations on the split table mapping — the temporal annotation
		// must be scoped to the main table only. Exact assertion shape depends on EF 11 preview's
		// annotation surface; the invariant to prove: the generated migration creates history
		// tracking for "Users" and none for "UserLockout".
		entity.GetTableMappings().Count().ShouldBe(2);
	}
}
```

(The second test's final assertion is deliberately the weakest true statement; the *real* proof of "no history on the split table" is the generated DDL read in Task 15's migration regen — the implementer eyeballs `norse_identity.sql` for `SYSTEM_VERSIONING` on the users table only. If `SplitToTable` + `IsTemporal` throws at model build, that throw fails these tests — that is the halt signal.)

- [ ] **Step 2: Run** — `dotnet test tests/Identity.EntityFramework.Tests -- --filter-class "*.IdentitySeamPinningTests"` and `"*.TemporalSplitProofTests"`.
Expected: PASS. **Any failure = HALT, report findings, wait.**

- [ ] **Step 3: Commit**

```bash
git checkout -b feature/pii-identity-erasure   # after the rebaseline described in the phase header
git add tests/Identity.EntityFramework.Tests/IdentitySeamPinningTests.cs tests/Identity.EntityFramework.Tests/TemporalSplitProofTests.cs
git commit -m "test: pin Identity attribute seam and temporal+split composition (spec verify items 1-3)"
```

(Verify item 3 — whether failed passkey assertions touch `AccessFailedCount` — is answered by reading `SignInManager.CheckPasskeySignInAsync` sources during this task and recording the answer in the commit message or a doc note; either answer is fine because the split table absorbs the churn regardless. Do not write a test against Microsoft internals for it.)

### Task 15: `SubjectKey` entity, schema changes, temporal sweep

> **AMENDED 2026-08-04 (Buvy's ruling: temporal is deferred out of this effort — implementation surfaced an EF Core 11 preview defect).** `IsTemporal()` + `SplitToTable()` compose at model-build time (Task 14's pinning tests pass honestly) but the SQL Server migrations SQL generator NREs on the split table: `UserLockout`'s `CreateTableOperation` inherits `SqlServer:IsTemporal=True` from `NorseUser` with null period-column names (temporal annotations are per-entity-type, not per-table) and `EscapeIdentifier(null)` throws. Bisected and localized in the Task 15 implementation report. **Ruling:** this effort builds the PII framework, turns on the ASP.NET Identity protection flags, and solves the personal-data story — nothing else. Read this task WITHOUT the temporal sweep and WITHOUT the lockout split: no `IsTemporal()`, no `SplitToTable()`, anywhere. What remains: `SubjectKey` entity (its "non-temporal, excluded from the sweep" clause is moot — nothing is temporal now), nullable `NormalizedUserName`, provider-aware filtered unique index at the context, `UserName` stays required, `NormalizedEmail` non-unique tripwire test, migrations regen under the squash law (both providers — SQL Server regen works once temporal/split are gone). Model tests: drop the three temporal facts (`Subject_keys_table_exists_and_is_not_temporal`, `Users_table_is_temporal…`, `Every_identity_entity_is_temporal…`); a plain `SubjectKey`-exists/shape assertion replaces the first. Task 14's `TemporalSplitProofTests.cs` is DELETED in this task's commit — it pins machinery this plan no longer uses; the fold-in effort re-proves at DDL level, where the defect actually lives. The lockout-churn worry that motivated the split dies with the history table (no temporal → no history rows to churn).
>
> **Fold-in trigger (recorded, per the pre-release-tracking doctrine — run ahead only with an exit condition):** when EF's temporal+split composition generates clean SQL Server DDL (upstream fix), temporal + the lockout split return as their own effort, folded into the then-current schema, and the full e2e tie-out begins in earnest — including riding the custody seam across all realms against BOTH local vault containers (Vault/OpenBao Transit + Azure Key Vault emulator, the Bifröst §9 dual-provider fitness test). **Upstream:** logged 2026-08-04 on dotnet/efcore#26457 (comment 5186526882); .NET 11 hits RC1/feature lockdown in September, so expect a platform-side workaround rather than an upstream fix inside this cycle — escape-hatch exploration of the .NET 11 codebase is Buvy's open thread.

**Files:**
- Create: `src/Identity.EntityFramework/SubjectKey.cs`
- Modify: `src/Identity.EntityFramework/NorseUser.cs` (drop `NormalizedUserName` `IsRequired`, filtered-unique index moves to context; lockout split — **if not already landed**, see coordination gate)
- Modify: `src/Identity.EntityFramework/NorseIdentityDbContext.cs` (register `SubjectKey`, provider-aware index filter, temporal sweep)
- Test: `tests/Identity.EntityFramework.Tests/NorseIdentityModelTests.cs` (extend or create)

**Interfaces:**
- Produces: `sealed record SubjectKey : NorseEntityBase<SubjectKey>, INorseEntity<SubjectKey>` — `SubjectId` (PK, `Guid`), `WrappedKey` (`byte[]`, `[MaxLength(64)]`), `WrappingKeyId` (`string`, `[MaxLength(128)]`), `CreatedAt` (`DateTimeOffset`). **Non-temporal, excluded from the sweep** — the wrapped-DEK row has no legitimate history question, per the envelope law. (Schema lands now; the vault-backed production store that reads it is a later slice — the live local path is Midgard's dev store. Deliberate, per spec §3.2/§3.3.)
- Schema law changes (spec §4.2): `NormalizedUserName` nullable; unique index filtered on SQL Server (`[NormalizedUserName] IS NOT NULL`), unfiltered on Postgres (NULLS DISTINCT default); `UserName` stays required — payload columns darken, they don't null.
- Temporal (spec §4.3): SQL Server only — every entity temporal **except** `SubjectKey` and the lockout split table; Postgres has no native system-versioning (rides Norns later; per the coordination gate, the parallel session may land the shape that changes this — consume, don't compete).

- [ ] **Step 1: Write the failing model tests**

```csharp
namespace Norse.Identity.EntityFramework.Tests;

public sealed class NorseIdentityModelTests
{
	// Build the model per provider the way the design-time factories do; reuse any existing
	// model-building helper in this test project.
	[Fact]
	void Normalized_user_name_is_nullable_and_its_unique_index_is_filtered_on_sql_server()
	{
		var entity = SqlServerModel.FindEntityType(typeof(NorseUser))!;
		entity.FindProperty(nameof(NorseUser.NormalizedUserName))!.IsNullable.ShouldBeTrue();
		var index = entity.GetIndexes().Single(i =>
			i.Properties.Single().Name == nameof(NorseUser.NormalizedUserName));
		index.IsUnique.ShouldBeTrue();
		index.GetFilter().ShouldBe("[NormalizedUserName] IS NOT NULL");
	}

	[Fact]
	void Normalized_user_name_unique_index_is_unfiltered_on_postgres()
	{
		var entity = PostgresModel.FindEntityType(typeof(NorseUser))!;
		var index = entity.GetIndexes().Single(i =>
			i.Properties.Single().Name == nameof(NorseUser.NormalizedUserName));
		index.IsUnique.ShouldBeTrue();
		index.GetFilter().ShouldBeNull();
	}

	[Fact]
	void Normalized_email_index_stays_non_unique_on_both_providers()
	{
		// The existing config indexes NormalizedEmail non-uniquely — nullable hashes coexist freely.
		// If this index is EVER made unique, it needs the identical filtered treatment as
		// NormalizedUserName or the second shred-ever violates it on SQL Server. This test is the
		// tripwire that forces that conversation.
		foreach (var model in new[] { SqlServerModel, PostgresModel })
		{
			var index = model.FindEntityType(typeof(NorseUser))!.GetIndexes()
				.Single(i => i.Properties.Single().Name == nameof(NorseUser.NormalizedEmail));
			index.IsUnique.ShouldBeFalse();
		}
	}

	[Fact]
	void User_name_stays_required()
	{
		SqlServerModel.FindEntityType(typeof(NorseUser))!
			.FindProperty(nameof(NorseUser.UserName))!.IsNullable.ShouldBeFalse();
	}

	[Fact]
	void Subject_keys_table_exists_and_is_not_temporal()
	{
		var entity = SqlServerModel.FindEntityType(typeof(SubjectKey))!;
		entity.IsTemporal().ShouldBeFalse();
	}

	[Fact]
	void Users_table_is_temporal_on_sql_server_and_lockout_columns_split_to_a_non_temporal_side_table()
	{
		var entity = SqlServerModel.FindEntityType(typeof(NorseUser))!;
		entity.IsTemporal().ShouldBeTrue();
		entity.GetTableMappings().Select(m => m.Table.Name).Distinct().Count().ShouldBe(2);
	}

	[Fact]
	void Every_identity_entity_is_temporal_on_sql_server_except_subject_keys()
	{
		var exceptions = new[] { typeof(SubjectKey) };
		foreach (var entity in SqlServerModel.GetEntityTypes().Where(e => !e.IsOwned()))
		{
			if (exceptions.Contains(entity.ClrType))
				continue;
			entity.IsTemporal().ShouldBeTrue($"{entity.ClrType.Name} should be temporal");
		}
	}
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test tests/Identity.EntityFramework.Tests -- --filter-class "*.NorseIdentityModelTests"`.

- [ ] **Step 3: Implement**

`SubjectKey.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norse.Persistence.EntityFramework;

namespace Norse.Identity.EntityFramework;

/// <summary>
/// The per-subject wrapped-DEK row (2026-08-03 PII spec §3.2): one wrap per subject regardless of
/// row count, one re-wrap point on rotation. The shred point is NOT this row — it is the wrapping
/// key in the platform key store; after destruction this row is permanent garbage, which the
/// envelope law permits. Deliberately non-temporal: a wrapped key has no history question.
/// </summary>
public sealed record SubjectKey : NorseEntityBase<SubjectKey>, INorseEntity<SubjectKey>
{
	/// <summary>The subject (user) identifier.</summary>
	public required Guid SubjectId { get; init; }
	/// <summary>The subject's DEK, wrapped under <see cref="WrappingKeyId"/>.</summary>
	[MaxLength(64)]
	public required byte[] WrappedKey { get; init; }
	/// <summary>The wrapping-key reference in the platform key store.</summary>
	[MaxLength(128)]
	public required string WrappingKeyId { get; init; }
	/// <summary>When the wrap was minted.</summary>
	public required DateTimeOffset CreatedAt { get; init; }

	/// <summary>Configures the EF entity mapping.</summary>
	public static void Configure(EntityTypeBuilder<SubjectKey> builder) =>
		builder.HasKey(k => k.SubjectId);
}
```

`NorseUser.cs` — in `Configure`: delete the `builder.Property(u => u.NormalizedUserName).IsRequired();` line and the existing `builder.HasIndex(u => u.NormalizedUserName).IsUnique();` line (the index moves to the context where the provider is known). `UserName`'s `IsRequired()` stays.

`NorseIdentityDbContext.OnModelCreating` — after the existing `ApplyNorseConfigurations()` call:

```csharp
		var isSqlServer = Database.ProviderName == NorseDbContextOptionsExtensions.SqlServerProviderName;
		builder.Entity<NorseUser>(entity =>
		{
			entity.HasIndex(u => u.NormalizedUserName)
				.IsUnique()
				.HasFilter(isSqlServer ? "[NormalizedUserName] IS NOT NULL" : null);
		});
		if (isSqlServer)
		{
			builder.Entity<NorseUser>(entity =>
			{
				entity.ToTable(table => table.IsTemporal());
				entity.SplitToTable("UserLockout", split =>
				{
					split.Property(u => u.AccessFailedCount);
					split.Property(u => u.LockoutEnd);
				});
			});
			foreach (var entityType in builder.Model.GetEntityTypes())
			{
				if (entityType.ClrType == typeof(SubjectKey) || entityType.ClrType == typeof(NorseUser) || entityType.IsOwned())
					continue;
				builder.Entity(entityType.ClrType).ToTable(table => table.IsTemporal());
			}
		}
```

(**If the parallel session already landed a split/temporal shape, use its shape verbatim and only add what's missing.** Table/column names shown pre-rewrite; snake_case applies on Postgres only, where none of the SQL Server block runs. OpenIddict token/authorization churn inside temporal tables is a known bloat trade the spec accepted — if it looks wrong during DDL review, raise it, don't silently exempt.)

- [ ] **Step 4: Regenerate migrations (squash law) and eyeball the DDL**

```bash
rm -rf src/Identity.Migrations.PostgreSQL/Migrations src/Identity.Migrations.SqlServer/Migrations
dotnet ef migrations add InitialCreate --project src/Identity.Migrations.PostgreSQL --startup-project src/Identity.Migrations.PostgreSQL
dotnet ef migrations add InitialCreate --project src/Identity.Migrations.SqlServer --startup-project src/Identity.Migrations.SqlServer
```

Verify in the regenerated `schema/norse_identity.sql`: `SYSTEM_VERSIONING` on users + every entity except `SubjectKeys`/`UserLockout`; filtered unique index present (SQL Server) and plain unique (Postgres); `subject_keys` table shaped as declared. Deviation → fix model, regen again.

- [ ] **Step 5: Run tests, commit**

Run: `dotnet test tests/Identity.EntityFramework.Tests`

```bash
git add src/Identity.EntityFramework/SubjectKey.cs src/Identity.EntityFramework/NorseUser.cs src/Identity.EntityFramework/NorseIdentityDbContext.cs src/Identity.Migrations.PostgreSQL src/Identity.Migrations.SqlServer tests/Identity.EntityFramework.Tests/NorseIdentityModelTests.cs
git commit -m "feat: subject_keys table, nullable filtered lookup index, temporal sweep with lockout split"
```

### Task 16: Protectors, keyring, envelope — the seam goes live

**Files:**
- Create: `src/Identity.Web.Server/NorsePersonalDataProtector.cs`, `src/Identity.Web.Server/NorseLookupProtector.cs`, `src/Identity.Web.Server/NorseLookupProtectorKeyRing.cs`, `src/Identity.Web.Server/NorseUserManager.cs`
- Modify: `src/Identity.Web.Server/IdentityBuilderExtensions.cs` (`ProtectPersonalData = true` + registrations + `.AddUserManager<NorseUserManager>()`)
- Modify: `src/Identity.Migrations.PostgreSQL/NorseIdentityDbContextFactory.cs` + SqlServer twin (design-time no-op protector registrations so model build succeeds with the flag on; migrations never decrypt)
- Test: `tests/Identity.Web.Server.Tests/NorsePersonalDataProtectorTests.cs`, `tests/Identity.Web.Server.Tests/NorseLookupProtectorTests.cs`

**Interfaces:**
- Consumes: `ISubjectKeyStore`, `ILookupKeyRing`, `SubjectCryptoScope`, `KeyDestroyedException`/`KeyMissingException` (Asgard `Abstractions.Backend.Keys`), `AesGcm` (BCL).
- Produces:
  - `sealed class NorsePersonalDataProtector(ISubjectKeyStore keyStore) : IPersonalDataProtector` — **envelope format `v1:{subjectId:D}:{base64(nonce ∥ ciphertext ∥ tag)}`**, AES-256-GCM, 12-byte nonce via `RandomNumberGenerator`, 16-byte tag. `Protect`: ambient subject from `SubjectCryptoScope.CurrentSubject` (null → `InvalidOperationException` — fail loud, never encrypt to nobody); DEK via `GetOrCreateAsync`. `Unprotect`: subject id read from the envelope (self-describing — no ambient needed); key via `GetAsync` → `Match`: `Available` → decrypt; `Destroyed` → `throw KeyDestroyedException(receipt)`; `Missing` → `throw KeyMissingException(subjectId)`. The interface is sync; bridge with `.AsTask().GetAwaiter().GetResult()` and a doc comment naming the constraint (Identity's seam is sync; the dev store is file-backed; the production provider caches unwrapped DEKs in memory under the TTL law, so sync-over-async is bounded).
  - `sealed class NorseLookupProtector(ILookupKeyRing keyRing) : ILookupProtector` — `Protect(keyId, data)` = Base64(HMAC-SHA256(ring[keyId], UTF8(data))); null/empty data passes through unchanged (Identity contract). `Unprotect` throws `NotSupportedException` — a blind index is one-way by definition.
  - `sealed class NorseLookupProtectorKeyRing(ILookupKeyRing keyRing) : ILookupProtectorKeyRing` — delegates `CurrentKeyId`/`GetAllKeyIds()`/indexer to the seam ring (the indexer returns the key id itself, not material — check Identity's contract: `this[string keyId]` returns the *key* used by the protector's keyId parameter; delegate as pass-through of the id).
  - `IdentityBuilderExtensions.AddNorseIdentity()` gains: `options.Stores.ProtectPersonalData = true`; singleton registrations for the three implementations (protector/lookup protector/keyring — singletons over singleton seam deps, per the model-cache capture law).
  - **`sealed class NorseUserManager : UserManager<NorseUser>` — the scope chokepoint. THIS IS THE WIRED-NOT-DESIGNED LINE OF THE WHOLE SEAM.** `SubjectCryptoScope` without a production caller is `OutcomeServerInterceptor` sitting unregistered, again. Every real write — Heimdall registration, email-change, phone-change, all of them — traverses `UserManager.CreateAsync`/the protected `UpdateUserAsync`; those two overrides are the one chokepoint that gives every current and future caller the ambient subject without touching Heimdall at all:

```csharp
/// <summary>
/// The one chokepoint every identity write traverses, establishing the ambient crypto subject
/// around the base call so the protector always knows whose DEK to use — Heimdall and every future
/// caller inherit the scope for free. Create assigns the id first when the caller didn't: the
/// subject must exist before the store encrypts.
/// </summary>
public sealed class NorseUserManager(
	IUserStore<NorseUser> store, IOptions<IdentityOptions> optionsAccessor,
	IPasswordHasher<NorseUser> passwordHasher, IEnumerable<IUserValidator<NorseUser>> userValidators,
	IEnumerable<IPasswordValidator<NorseUser>> passwordValidators, ILookupNormalizer keyNormalizer,
	IdentityErrorDescriber errors, IServiceProvider services, ILogger<UserManager<NorseUser>> logger) :
	UserManager<NorseUser>(store, optionsAccessor, passwordHasher, userValidators, passwordValidators,
		keyNormalizer, errors, services, logger)
{
	/// <inheritdoc />
	public override async Task<IdentityResult> CreateAsync(NorseUser user)
	{
		ArgumentNullException.ThrowIfNull(user);
		if (user.Id == Guid.Empty)
			user.Id = Guid.CreateVersion7();
		using (SubjectCryptoScope.Begin(user.Id))
			return await base.CreateAsync(user).ConfigureAwait(false);
	}

	/// <inheritdoc />
	protected override async Task<IdentityResult> UpdateUserAsync(NorseUser user)
	{
		using (SubjectCryptoScope.Begin(user.Id))
			return await base.UpdateUserAsync(user).ConfigureAwait(false);
	}
}
```

  (`CreateAsync(user, password)` funnels into `CreateAsync(user)`, and every `Set*Async` mutation funnels into `UpdateUserAsync` — the two overrides cover the write surface. Verify the id-generation premise while implementing: if EF value generation is what assigns `NorseUser.Id` today, the explicit `CreateVersion7` assignment above becomes load-bearing, since the scope must capture the real id *before* the store encrypts. Registration: `.AddUserManager<NorseUserManager>()` on the `AddNorseIdentity()` chain — note `AddIdentity` registers the manager scoped; the scope is ambient AsyncLocal, not captured state, so a scoped manager over singleton protector is correct.)
- **Written law restated in the wiring's doc comment:** email is the username, so `NormalizedEmail` and `NormalizedUserName` hold the same blind-index HMAC — correct and expected; do not "fix" the duplication.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace Norse.Identity.Web.Server.Tests;

public sealed class NorsePersonalDataProtectorTests
{
	// In-memory seam fake (three-state).
	sealed class FakeKeyStore : ISubjectKeyStore
	{
		readonly Dictionary<Guid, byte[]> _keys = [];
		readonly Dictionary<Guid, ErasureReceipt> _destroyed = [];

		public ValueTask<SubjectKeyResult> GetAsync(Guid subjectId, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(
				_keys.TryGetValue(subjectId, out var key) ? SubjectKeyResult.Available(key) :
				_destroyed.TryGetValue(subjectId, out var receipt) ? SubjectKeyResult.Destroyed(receipt) :
				SubjectKeyResult.Missing);

		public ValueTask<byte[]> GetOrCreateAsync(Guid subjectId, CancellationToken cancellationToken = default)
		{
			if (_destroyed.TryGetValue(subjectId, out var receipt))
				throw new KeyDestroyedException(receipt);
			if (!_keys.TryGetValue(subjectId, out var key))
			{
				key = new byte[32];
				RandomNumberGenerator.Fill(key);
				_keys[subjectId] = key;
			}
			return ValueTask.FromResult(key);
		}

		public ValueTask<ErasureReceipt> DestroyAsync(Guid subjectId, CancellationToken cancellationToken = default)
		{
			_keys.Remove(subjectId);
			if (!_destroyed.TryGetValue(subjectId, out var receipt))
			{
				receipt = new(Guid.NewGuid(), DateTimeOffset.UtcNow);
				_destroyed[subjectId] = receipt;
			}
			return ValueTask.FromResult(receipt);
		}
	}

	[Fact]
	void Protect_then_unprotect_round_trips_under_the_ambient_subject()
	{
		NorsePersonalDataProtector protector = new(new FakeKeyStore());
		var subject = Guid.NewGuid();
		string protectedValue;
		using (SubjectCryptoScope.Begin(subject))
			protectedValue = protector.Protect("buvy@example.com");
		protectedValue.ShouldStartWith($"v1:{subject:D}:");
		protector.Unprotect(protectedValue).ShouldBe("buvy@example.com"); // no ambient needed — self-describing
	}

	[Fact]
	void Protect_fails_loudly_with_no_ambient_subject()
	{
		NorsePersonalDataProtector protector = new(new FakeKeyStore());
		Should.Throw<InvalidOperationException>(() => protector.Protect("data"));
	}

	[Fact]
	void Unprotect_throws_key_destroyed_with_the_receipt_when_the_subject_is_shredded()
	{
		FakeKeyStore store = new();
		NorsePersonalDataProtector protector = new(store);
		var subject = Guid.NewGuid();
		string protectedValue;
		using (SubjectCryptoScope.Begin(subject))
			protectedValue = protector.Protect("buvy@example.com");
		var receipt = store.DestroyAsync(subject).AsTask().GetAwaiter().GetResult();
		var exception = Should.Throw<KeyDestroyedException>(() => protector.Unprotect(protectedValue));
		exception.Receipt.ShouldBe(receipt); // spec §8 verify item 8: Destroyed(receipt) vs Missing
	}

	[Fact]
	void Unprotect_throws_key_missing_when_no_key_and_no_receipt_exist()
	{
		NorsePersonalDataProtector protector = new(new FakeKeyStore());
		var orphan = $"v1:{Guid.NewGuid():D}:{Convert.ToBase64String(new byte[44])}";
		Should.Throw<KeyMissingException>(() => protector.Unprotect(orphan));
	}

	[Fact]
	void Tampered_ciphertext_fails_loudly()
	{
		NorsePersonalDataProtector protector = new(new FakeKeyStore());
		var subject = Guid.NewGuid();
		string protectedValue;
		using (SubjectCryptoScope.Begin(subject))
			protectedValue = protector.Protect("buvy@example.com");
		var tampered = $"{protectedValue[..^4]}AAAA";
		Should.Throw<CryptographicException>(() => protector.Unprotect(tampered));
	}
}

public sealed class NorseUserManagerTests
{
	// The wired-not-designed test for the scope chokepoint: NO manual SubjectCryptoScope anywhere in
	// this test. If the manager fails to establish the ambient subject, the protector's fail-loud
	// law makes CreateAsync throw — a green test IS the proof the seam is wired end to end.
	[Fact]
	async Task Create_through_the_manager_establishes_the_scope_without_any_manual_begin()
	{
		// Arrange a real NorseUserManager over an NSubstitute IUserStore whose CreateAsync captures
		// SubjectCryptoScope.CurrentSubject at invocation time (the moment Identity's store would
		// call the protector). Reuse/extend the project's manager-construction helper.
		Guid? observed = null;
		var store = Substitute.For<IUserStore<NorseUser>>();
		store.CreateAsync(Arg.Any<NorseUser>(), Arg.Any<CancellationToken>())
			.Returns(_ =>
			{
				observed = SubjectCryptoScope.CurrentSubject;
				return IdentityResult.Success;
			});
		var manager = TestUserManager.Create(store); // helper: real NorseUserManager, substituted collaborators

		NorseUser user = new() { UserName = "buvy@example.com", Email = "buvy@example.com" };
		var result = await manager.CreateAsync(user);

		result.Succeeded.ShouldBeTrue();
		user.Id.ShouldNotBe(Guid.Empty);          // id assigned before the store ran
		observed.ShouldBe(user.Id);               // ambient subject was live inside the store call
		SubjectCryptoScope.CurrentSubject.ShouldBeNull(); // and restored after
	}

	[Fact]
	async Task Update_through_the_manager_establishes_the_scope_around_the_store_write()
	{
		Guid? observed = null;
		var store = Substitute.For<IUserStore<NorseUser>>();
		store.UpdateAsync(Arg.Any<NorseUser>(), Arg.Any<CancellationToken>())
			.Returns(_ =>
			{
				observed = SubjectCryptoScope.CurrentSubject;
				return IdentityResult.Success;
			});
		store.GetUserIdAsync(Arg.Any<NorseUser>(), Arg.Any<CancellationToken>())
			.Returns(call => call.Arg<NorseUser>().Id.ToString());
		var manager = TestUserManager.Create(store);

		NorseUser user = new() { Id = Guid.NewGuid(), UserName = "buvy@example.com" };
		var result = await manager.UpdateAsync(user);

		result.Succeeded.ShouldBeTrue();
		observed.ShouldBe(user.Id);
	}
}
```

```csharp
namespace Norse.Identity.Web.Server.Tests;

public sealed class NorseLookupProtectorTests
{
	sealed class FakeRing : ILookupKeyRing
	{
		public byte[] Key { get; } = RandomNumberGenerator.GetBytes(32);
		public string CurrentKeyId => "k1";
		public IEnumerable<string> KeyIds => ["k1"];
		public byte[] GetKey(string keyId) => keyId == "k1" ? Key : throw new KeyNotFoundException(keyId);
	}

	[Fact]
	void Protect_is_a_deterministic_keyed_hmac_of_the_normalized_value()
	{
		FakeRing ring = new();
		NorseLookupProtector protector = new(ring);
		var first = protector.Protect("k1", "buvy@example.com");
		var second = protector.Protect("k1", "buvy@example.com");
		second.ShouldBe(first); // determinism IS the blind index
		using HMACSHA256 hmac = new(ring.Key);
		first.ShouldBe(Convert.ToBase64String(hmac.ComputeHash("buvy@example.com"u8.ToArray())));
	}

	[Fact]
	void Protect_passes_null_and_empty_through_unchanged()
	{
		NorseLookupProtector protector = new(new FakeRing());
		protector.Protect("k1", null).ShouldBeNull();
		protector.Protect("k1", "").ShouldBe("");
	}

	[Fact]
	void Unprotect_is_refused_because_a_blind_index_is_one_way()
	{
		NorseLookupProtector protector = new(new FakeRing());
		Should.Throw<NotSupportedException>(() => protector.Unprotect("k1", "hash"));
	}
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test tests/Identity.Web.Server.Tests -- --filter-class "*.NorsePersonalDataProtectorTests"`.

- [ ] **Step 3: Implement** the three classes per the Interfaces block (AES-GCM: `nonce(12) ∥ ciphertext ∥ tag(16)` concatenated then base64; decrypt slices accordingly). Wire `IdentityBuilderExtensions.AddNorseIdentity()`:

```csharp
		services.Configure<IdentityOptions>(o =>
		{
			o.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
			o.Stores.ProtectPersonalData = true;
		});
		services
			.AddSingleton<IPersonalDataProtector, NorsePersonalDataProtector>()
			.AddSingleton<ILookupProtector, NorseLookupProtector>()
			.AddSingleton<ILookupProtectorKeyRing, NorseLookupProtectorKeyRing>();
```

Design-time factories (both providers): register a `sealed class DesignTimePersonalDataProtector : IPersonalDataProtector` (both methods throw `NotSupportedException("Design time never touches plaintext.")`) plus equivalent lookup no-ops in the factory's fallback service provider — model build demands the services exist; migrations never invoke them. Also call `modelBuilder.ProtectPiiScalars(...)` — **not yet**: no struct-typed PII property exists on the schema today; the call site lands with the first profile property (spec §4.5 honesty note). Note this in `NorseIdentityDbContext`'s doc comment so the future property knows where to wire.

- [ ] **Step 4: Run tests** — `dotnet test tests/Identity.Web.Server.Tests`, then full realm build.

- [ ] **Step 5: Commit**

```bash
git add src/Identity.Web.Server/NorsePersonalDataProtector.cs src/Identity.Web.Server/NorseLookupProtector.cs src/Identity.Web.Server/NorseLookupProtectorKeyRing.cs src/Identity.Web.Server/NorseUserManager.cs src/Identity.Web.Server/IdentityBuilderExtensions.cs src/Identity.Migrations.PostgreSQL/NorseIdentityDbContextFactory.cs src/Identity.Migrations.SqlServer/NorseIdentityDbContextFactory.cs tests/Identity.Web.Server.Tests
git commit -m "feat: per-subject envelope protector, HMAC lookup protector, scope chokepoint manager, ProtectPersonalData on"
```

### Task 17: Claims factory — allowlist, not strip-list

**Files:**
- Create: `src/Identity.Web.Server/NorseUserClaimsPrincipalFactory.cs`
- Modify: `src/Identity.Web.Server/IdentityBuilderExtensions.cs` (chain `.AddClaimsPrincipalFactory<NorseUserClaimsPrincipalFactory>()`)
- Test: `tests/Identity.Web.Server.Tests/NorseUserClaimsPrincipalFactoryTests.cs`

**Interfaces:**
- Produces: `sealed class NorseUserClaimsPrincipalFactory(UserManager<NorseUser> userManager, RoleManager<NorseRole> roleManager, IOptions<IdentityOptions> options) : UserClaimsPrincipalFactory<NorseUser, NorseRole>(userManager, roleManager, options)` — overrides `CreateAsync`: base builds, then every claim whose type is not in the closed allowlist is removed. Allowlist (from `Options.ClaimsIdentity`): `UserIdClaimType`, `RoleClaimType`, `SecurityStampClaimType`. Dropped by construction: `UserNameClaimType` (`Name` — `User.Identity.Name` goes null, display names come from the disclosure surface), `EmailClaimType`, and stored user/role claims (none exist today; they return by declared need only). `SignInManager`-appended claims (`amr` etc.) are added after the factory runs and are unaffected. Spec §8 verify item 7 is discharged by the exact-set test below — if a future .NET release adds a claim, the allowlist drops it and the test stays green by construction.

- [ ] **Step 1: Write the failing test**

```csharp
namespace Norse.Identity.Web.Server.Tests;

public sealed class NorseUserClaimsPrincipalFactoryTests
{
	static NorseUserClaimsPrincipalFactory CreateFactory(NorseUser user, string[] roles, Claim[] storedClaims)
	{
		var userManager = MockUserManager.Create<NorseUser>(); // reuse the project's existing manager-mock helper (MockSignInManager precedent); create one if absent
		userManager.GetUserIdAsync(user).Returns(user.Id.ToString());
		userManager.GetUserNameAsync(user).Returns(user.UserName);
		userManager.GetEmailAsync(user).Returns(user.Email);
		userManager.SupportsUserEmail.Returns(true);
		userManager.SupportsUserSecurityStamp.Returns(true);
		userManager.GetSecurityStampAsync(user).Returns(user.SecurityStamp);
		userManager.SupportsUserClaim.Returns(true);
		userManager.GetClaimsAsync(user).Returns(storedClaims);
		userManager.SupportsUserRole.Returns(true);
		userManager.GetRolesAsync(user).Returns(roles);
		var roleManager = MockRoleManager.Create<NorseRole>();
		roleManager.SupportsRoleClaims.Returns(false);
		var options = Microsoft.Extensions.Options.Options.Create(new IdentityOptions());
		return new(userManager, roleManager, options);
	}

	[Fact]
	async Task Principal_carries_exactly_the_closed_claim_set_and_nothing_else()
	{
		NorseUser user = new()
		{
			Id = Guid.NewGuid(),
			UserName = "buvy@example.com",
			Email = "buvy@example.com",
			PhoneNumber = "+15551234567",
			SecurityStamp = Guid.NewGuid().ToString("N")
		};
		var factory = CreateFactory(user, ["admin"], [new("favorite_color", "green")]);
		var principal = await factory.CreateAsync(user);
		var options = new IdentityOptions().ClaimsIdentity;
		principal.Claims
			.Select(c => c.Type)
			.Distinct()
			.Order()
			.ShouldBe(new[] { options.RoleClaimType, options.SecurityStampClaimType, options.UserIdClaimType }.Order(),
				ignoreOrder: true); // EXACT closed set — any surplus claim fails this test
		principal.Identity!.Name.ShouldBeNull(); // Name claim deliberately dropped
		principal.FindFirst(options.UserIdClaimType)!.Value.ShouldBe(user.Id.ToString());
		principal.FindFirst(options.SecurityStampClaimType)!.Value.ShouldBe(user.SecurityStamp);
		principal.IsInRole("admin").ShouldBeTrue();
	}
}
```

- [ ] **Step 2: Run to verify failure** — the base factory emits Name + Email + the stored claim, so the exact-set assertion fails.

- [ ] **Step 3: Implement**

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Norse.Identity.Web.Server;

/// <summary>
/// The claims allowlist (2026-08-03 PII spec §4.4): the base factory builds, then everything outside
/// the closed set — opaque GUID, roles, security stamp — is dropped. Allowlist, not strip-list: a
/// claim Microsoft adds in a future release is dropped by construction, never leaked by omission.
/// The security-stamp claim stays because <c>SecurityStampValidator</c> revalidation is the
/// mechanism that kills a dead user's live sessions after the shred ceremony rotates the stamp.
/// <c>ClaimTypes.Name</c> is omitted: display names come from the disclosure surface, masked.
/// </summary>
public sealed class NorseUserClaimsPrincipalFactory(
	UserManager<NorseUser> userManager, RoleManager<NorseRole> roleManager, IOptions<IdentityOptions> options) :
	UserClaimsPrincipalFactory<NorseUser, NorseRole>(userManager, roleManager, options)
{
	/// <inheritdoc />
	public override async Task<ClaimsPrincipal> CreateAsync(NorseUser user)
	{
		var principal = await base.CreateAsync(user).ConfigureAwait(false);
		var identity = (ClaimsIdentity)principal.Identity!;
		var claims = Options.ClaimsIdentity;
		string[] allowed = [claims.UserIdClaimType, claims.RoleClaimType, claims.SecurityStampClaimType];
		foreach (var claim in identity.Claims.Where(c => !allowed.Contains(c.Type, StringComparer.Ordinal)).ToArray())
			identity.RemoveClaim(claim);
		return principal;
	}
}
```

Wire in `AddNorseIdentity()`: `.AddClaimsPrincipalFactory<NorseUserClaimsPrincipalFactory>()` on the `IdentityBuilder` chain.

- [ ] **Step 4: Run tests** — `dotnet test tests/Identity.Web.Server.Tests`.

- [ ] **Step 5: Commit**

```bash
git add src/Identity.Web.Server/NorseUserClaimsPrincipalFactory.cs src/Identity.Web.Server/IdentityBuilderExtensions.cs tests/Identity.Web.Server.Tests/NorseUserClaimsPrincipalFactoryTests.cs
git commit -m "feat: allowlist claims factory — GUID, roles, security stamp; nothing else"
```

### Task 18: The shred ceremony — three acts

**Files:**
- Create: `src/Identity.Web.Server/ErasureService.cs`
- Test: `tests/Identity.Web.Server.Tests/ErasureServiceTests.cs` (real database — DB-semantics law; use the platform's existing Testcontainers Postgres fixture pattern; if this test project has none, mirror the SQL Server Testcontainers fixture noted in the platform memory/Urðarbrunnr tests)

**Interfaces:**
- Consumes: `NorseIdentityDbContext`, `ISubjectKeyStore`, `SignInManager<NorseUser>`-adjacent revalidation (`SecurityStampValidator` behavior is asserted via `UserManager` stamp comparison — see test).
- Produces: `sealed class ErasureService(NorseIdentityDbContext context, ISubjectKeyStore keyStore)` with:

```csharp
public async Task<Outcome<ErasureReceipt>> ShredAsync(Guid subjectId, CancellationToken cancellationToken = default)
```

Three acts, database acts committing **before** key destruction (spec §4.2 — the ceremony ordering): one `ExecuteUpdateAsync` nulls `NormalizedUserName` + `NormalizedEmail` and rotates `SecurityStamp` to a fresh value; zero rows updated → `Outcome.Err(ErrorCategory.NotFound)` and **no key destruction** (never burn a key for a row that doesn't exist); then `keyStore.DestroyAsync` returns the receipt → `Outcome.Ok(receipt)`. `SecurityStamp` on `NorseUser` rides the `Stamp` string↔Guid converter, so the rotation writes `Guid.NewGuid().ToString("N")`-shaped… **no** — the converter maps string→Guid storage; `SetProperty(u => u.SecurityStamp, Guid.NewGuid().ToString())` with the standard `"D"` format is what the converter round-trips; verify against `IdentityValueConverters.Stamp` while implementing and match its format exactly.

- [ ] **Step 1: Write the failing tests** (shape below; adapt the fixture to the project's real-DB pattern)

```csharp
namespace Norse.Identity.Web.Server.Tests;

public sealed class ErasureServiceTests(PostgresIdentityFixture fixture) : IClassFixture<PostgresIdentityFixture>
{
	[Fact]
	async Task Shred_nulls_lookup_hashes_rotates_the_stamp_and_destroys_the_key()
	{
		var (context, keyStore) = await fixture.CreateScopeAsync();
		var user = await fixture.SeedUserAsync("shred-me@example.com");
		var stampBefore = user.SecurityStamp;

		ErasureService service = new(context, keyStore);
		var outcome = await service.ShredAsync(user.Id, TestContext.Current.CancellationToken);

		outcome.TryGetValue(out Success<ErasureReceipt> success).ShouldBeTrue();
		var reloaded = await context.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id, TestContext.Current.CancellationToken);
		reloaded.NormalizedUserName.ShouldBeNull();
		reloaded.NormalizedEmail.ShouldBeNull();
		reloaded.SecurityStamp.ShouldNotBe(stampBefore);           // spec §8 verify item 10's trigger
		var keyResult = await keyStore.GetAsync(user.Id, TestContext.Current.CancellationToken);
		keyResult.Match(_ => "available", _ => "destroyed", () => "missing").ShouldBe("destroyed");
	}

	[Fact]
	async Task Session_authenticated_before_shred_dies_at_the_next_revalidation()
	{
		// Verify item 10, closed through the REAL validator path: the pre-shred principal is built by
		// the real NorseUserClaimsPrincipalFactory (so this test also interlocks with Task 17 — if
		// the allowlist ever drops or renames the stamp claim, the dead-session mechanism breaks HERE,
		// not silently in production), and the post-shred verdict comes from
		// SignInManager.ValidateSecurityStampAsync — the exact comparison cookie revalidation runs.
		var (context, keyStore) = await fixture.CreateScopeAsync();
		var user = await fixture.SeedUserAsync("session@example.com");
		var signInManager = fixture.CreateSignInManager();          // real SignInManager over the real factory (fixture wires IHttpContextAccessor with a bare DefaultHttpContext)
		var principal = await signInManager.CreateUserPrincipalAsync(user);   // "the cookie" as issued pre-shred

		(await signInManager.ValidateSecurityStampAsync(principal)).ShouldNotBeNull(); // sanity arm: live before shred

		await new ErasureService(context, keyStore).ShredAsync(user.Id, TestContext.Current.CancellationToken);

		(await signInManager.ValidateSecurityStampAsync(principal)).ShouldBeNull();    // dead within one revalidation interval
	}

	[Fact]
	async Task Destruction_failure_leaves_a_retryable_half_severed_state_and_a_rerun_completes()
	{
		// The ceremony's partial-failure contract: acts 1–2 committed, act 3 threw → the subject is
		// half-severed (unfindable, unsigninable, still decryptable, no receipt). Legal because
		// retryable: the re-run matches the row again, re-rotates harmlessly, and completes the
		// destruction. The retry obligation is the future DSAR machinery's contract.
		var (context, keyStore) = await fixture.CreateScopeAsync();
		var user = await fixture.SeedUserAsync("flaky@example.com");
		ThrowOnceKeyStore flaky = new(keyStore);                    // decorator: first DestroyAsync throws, rest delegate
		ErasureService service = new(context, flaky);

		await Should.ThrowAsync<InvalidOperationException>(
			async () => await service.ShredAsync(user.Id, TestContext.Current.CancellationToken)); // fault propagates — no swallow

		var half = await context.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id, TestContext.Current.CancellationToken);
		half.NormalizedUserName.ShouldBeNull();                     // acts 1–2 committed
		(await keyStore.GetAsync(user.Id, TestContext.Current.CancellationToken))
			.Match(_ => "available", _ => "destroyed", () => "missing").ShouldBe("available"); // key intact, no receipt

		var retry = await service.ShredAsync(user.Id, TestContext.Current.CancellationToken);
		retry.TryGetValue(out Success<ErasureReceipt> receipt).ShouldBeTrue(); // re-run completes with the receipt
		_ = receipt;
	}

	[Fact]
	async Task Shred_of_an_unknown_subject_is_not_found_and_burns_no_key()
	{
		var (context, keyStore) = await fixture.CreateScopeAsync();
		var ghost = Guid.NewGuid();
		var outcome = await new ErasureService(context, keyStore).ShredAsync(ghost, TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.NotFound);
		(await keyStore.GetAsync(ghost, TestContext.Current.CancellationToken))
			.Match(_ => "available", _ => "destroyed", () => "missing").ShouldBe("missing");
	}

	[Fact]
	async Task Reregistration_with_the_same_email_succeeds_because_the_hashes_were_nulled()
	{
		// Spec §4.2: re-registration works via nulling, not key movement — same HMAC, fresh row.
		var (context, keyStore) = await fixture.CreateScopeAsync();
		var first = await fixture.SeedUserAsync("round-two@example.com");
		await new ErasureService(context, keyStore).ShredAsync(first.Id, TestContext.Current.CancellationToken);
		var second = await fixture.SeedUserAsync("round-two@example.com"); // same email, same blind index value
		second.Id.ShouldNotBe(first.Id);
		var live = await context.Users.AsNoTracking()
			.CountAsync(u => u.NormalizedUserName != null && u.NormalizedUserName == second.NormalizedUserName, TestContext.Current.CancellationToken);
		live.ShouldBe(1); // exactly one live row answers the lookup
	}
}
```

(`PostgresIdentityFixture`: Testcontainers Postgres + migrated `norse_identity` schema + `AddNorseIdentity()` DI with the Midgard dev key store rooted in a temp dir. Seeding goes through the real `NorseUserManager.CreateAsync` with **no manual scope** — Task 16's chokepoint provides it, and seeding through the real manager is what proves the wiring end to end: the seeded row's `Email` column must be `v1:`-prefixed ciphertext and `NormalizedEmail` a base64 HMAC; add one fixture-level smoke assertion for exactly that. The fixture also exposes `CreateSignInManager()` — a real `SignInManager<NorseUser>` from the DI scope, with `IHttpContextAccessor` carrying a bare `DefaultHttpContext` — and the test file carries `sealed class ThrowOnceKeyStore(ISubjectKeyStore inner) : ISubjectKeyStore`: `GetAsync`/`GetOrCreateAsync` delegate; the first `DestroyAsync` throws `InvalidOperationException("simulated vault outage")`, subsequent calls delegate.)

- [ ] **Step 2: Run to verify failure** — `ErasureService` does not exist.

- [ ] **Step 3: Implement**

```csharp
using Microsoft.EntityFrameworkCore;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Backend.Keys;
using Norse.Identity.EntityFramework;

namespace Norse.Identity.Web.Server;

/// <summary>
/// The shred ceremony, three acts in law order (2026-08-03 PII spec §4.2): null the current-row
/// lookup hashes, rotate the security stamp (arming <c>SecurityStampValidator</c> to kill live
/// sessions within one revalidation interval), destroy the per-subject wrapping key. Database acts
/// commit before the destruction. Partial-failure contract: a failure in acts 1–2 aborts with the
/// key intact and the row untouched-or-not per the single UPDATE's atomicity; a failure in act 3
/// leaves a <b>half-severed, retryable</b> state — hashes nulled, stamp rotated, sessions dying,
/// key intact, no receipt. The re-run matches the row again, re-rotates harmlessly, and completes
/// the destruction; retry-until-receipt is the caller's obligation (recorded as the future Syn DSAR
/// machinery's contract). Payload ciphertext stays in place, dark. This is the ceremony, not the
/// trigger.
/// </summary>
public sealed class ErasureService(NorseIdentityDbContext context, ISubjectKeyStore keyStore)
{
	/// <summary>Severs the subject. NotFound when no row exists — no key is burned for a ghost.</summary>
	public async Task<Outcome<ErasureReceipt>> ShredAsync(Guid subjectId, CancellationToken cancellationToken = default)
	{
		var stamp = Guid.NewGuid().ToString("D");
		var updated = await context.Users
			.Where(u => u.Id == subjectId)
			.ExecuteUpdateAsync(setters => setters
				.SetProperty(u => u.NormalizedUserName, (string?)null)
				.SetProperty(u => u.NormalizedEmail, (string?)null)
				.SetProperty(u => u.SecurityStamp, stamp), cancellationToken)
			.ConfigureAwait(false);
		if (updated == 0)
			return Outcome<ErasureReceipt>.Err(ErrorCategory.NotFound);
		var receipt = await keyStore.DestroyAsync(subjectId, cancellationToken).ConfigureAwait(false);
		return Outcome<ErasureReceipt>.Ok(receipt);
	}
}
```

(Stamp format: match `IdentityValueConverters.Stamp` — if it round-trips `"N"`, use `"N"`; the fixture test catches a mismatch as a save/convert failure.)

- [ ] **Step 4: Run tests** — `dotnet test tests/Identity.Web.Server.Tests`.

- [ ] **Step 5: Commit**

```bash
git add src/Identity.Web.Server/ErasureService.cs tests/Identity.Web.Server.Tests/ErasureServiceTests.cs tests/Identity.Web.Server.Tests/PostgresIdentityFixture.cs
git commit -m "feat: three-act shred ceremony — null hashes, rotate stamp, destroy key"
```

### Task 19a: The disclosure contract — Heimdall (`feature/pii-disclosure-contract`)

> **AMENDED 2026-08-04 (ruling: the wire tier rides Heimdall, matching `IAuthenticationService`).** The original Task 19 minted a new `Identity.Services` project in Himinbjörg for the `[ServiceContract]` + wire records. Wrong realm: Himinbjörg is sealed server-side — nothing it ships crosses to WASM or MAUI — so a client-consumable disclosure contract could never live there without breaking the seal. The precedent already on the books is `IAuthenticationService`: contract + wire records + policy-name constants in Heimdall's `AuthN.Services` (WASM-lean, references nothing above Asgard's `Abstractions.Contracts`), validators in `AuthN.Components`, concrete hydrate-and-send host in Himinbjörg's `Identity.Web.Server`. The disclosure surface follows it exactly. Himinbjörg's `Identity.Services` project is **never created**.
>
> **Sequencing this introduces:** Himinbjörg consumes Heimdall via `NorseRef` → floating `PackageReference` (`Norse.AuthN.Services`, `Version="*"`), so this task carries its **own ship gate** — Task 19b cannot compile until the amended package is on the feed.
>
> **Razor components assessed 2026-08-04, amended same day — `PersonalData.razor` moves, `DeletePersonalData.razor` stays.** Both pages carry backend injections today (`UserManager<NorseUser>`, delete also takes `SignInManager<NorseUser>`, `HttpContext` cascade, `IdentityRedirectManager`, server form-POST). But the manager work is not what blocks a move — the sweep mechanism is exactly the Login/Register precedent: push the server work into handlers behind the service contract, and the page needs only the injected `I{Context}Service`. Ruled 2026-08-04: **download personal data becomes a gRPC call** — `PersonalData.razor`'s only real server dependency was the `DownloadPersonalData` form-POST endpoint, which `GetMyPersonalDataAsync` replaces outright, so the page ports to Heimdall in this task (inject `IIdentityService` + `NavigationManager`, materialize the download client-side from `PersonalDataResponse`; exact save mechanism — JS-interop anchor/data URI — decided at implementation). `DeletePersonalData.razor` stays in Himinbjörg: its `UserManager.DeleteAsync` semantics are superseded by the shred ceremony (Task 18), and the wire-exposed shred trigger is a recorded deliberate deferral (spec §7, Syn DSAR trigger) — designing that contract method (password re-confirmation, `CheckPasswordAsync` + `ErasureService` + `SignOutAsync` in a handler) is the validation-work round, not this one.

**Files:**
- Create: `src/AuthN.Services/IIdentityService.cs`, `GetMyPersonalDataRequest.cs`, `GetMaskedPersonalDataRequest.cs`, `PersonalDataResponse.cs`, `MaskedPersonalDataResponse.cs`, `IdentityPolicies.cs`
- Create: `src/AuthN.Components/GetMaskedPersonalDataRequestValidator.cs`
- Create: `src/AuthN.Components.FluentUI/PersonalData.razor` — port of Himinbjörg's page, injection-clean of server types (`IIdentityService` + `NavigationManager` + `IJSRuntime` — the third is the client-side file-save mechanism, no server coupling; Login.razor precedent otherwise); Download button calls `GetMyPersonalDataAsync` and saves client-side; the delete link keeps pointing at Himinbjörg's still-hosted `/Account/Manage/DeletePersonalData` route (plain relative link — the cross-host resolution question is owned by the page-by-page migration sweep, same as every other scaffold route)
- Test: extend `tests/AuthN.Services.Tests/RequestContractTests.cs` (the purity lock's record inventory covers the four new wire shapes — no `[Authorize]`, no mediator marker, and the assembly still never references `Norse.Abstractions.Web.Server`); create `tests/AuthN.Components.Tests/GetMaskedPersonalDataRequestValidatorTests.cs`; create `tests/AuthN.Components.FluentUI.Tests/PersonalDataTests.cs` (LoginTests precedent — fake `IIdentityService`, assert render + call)

**Interfaces** (namespace `Norse.AuthN.Services`, brand-injected):

```csharp
[ServiceContract(Name = "grpc.identity.v1.IdentityService")]
public interface IIdentityService
{
	[OperationContract] Task<Outcome<PersonalDataResponse>> GetMyPersonalDataAsync(GetMyPersonalDataRequest request, CancellationToken cancellationToken = default);
	[OperationContract] Task<Outcome<MaskedPersonalDataResponse>> GetMaskedPersonalDataAsync(GetMaskedPersonalDataRequest request, CancellationToken cancellationToken = default);
}

[DataContract] public sealed record GetMyPersonalDataRequest;              // EMPTY — no subject id field exists, so asking about someone else is unrepresentable (spec §6.1)
[DataContract] public sealed record GetMaskedPersonalDataRequest { [DataMember(Order = 1)] public required Guid SubjectId { get; set; } }
[DataContract] public sealed record PersonalDataResponse { [DataMember(Order = 1)] public required string Email …; [DataMember(Order = 2)] public required string PhoneNumber …; }   // full wire strings; empty string when the user has no phone
[DataContract] public sealed record MaskedPersonalDataResponse { … same two members, masked … }
```

- `IdentityPolicies` rides the contract assembly exactly as `AuthNPolicies` does — constants only (`Self`, `MaskedDisclosure`, `SystemRole`); the `RequireRole`/policy **registration** stays server-side (Task 19b). The names are wire-adjacent metadata the concrete host mirrors onto its methods for gRPC endpoint discovery, same as `AuthNPolicies.Public` today.
- Validator: `GetMaskedPersonalDataRequestValidator` — `RuleFor(x => x.SubjectId).NotEmpty()` — lands beside `LoginRequestValidator`/`RegisterRequestValidator` in `AuthN.Components` and gets the same dual run: Blazilla client-side against the wire type, and server-side through Asgard's generated `CommandRequestValidator<TCommand,TRequest,TResponse>` adapter reaching through Task 19b's command wrapper. `GetMyPersonalDataRequest` is an empty record by design — no validator exists; nothing to validate **is** the point.

- [ ] **Step 1: Write the failing tests** — the `RequestContractTests` extension and the validator tests.
- [ ] **Step 2: Run to verify failure** — the types do not exist.
- [ ] **Step 3: Implement** per the Interfaces block, then `dotnet build Heimdall.slnx && dotnet test Heimdall.slnx`.
- [ ] **Step 4: Update realm docs and stage** — `Heimdall/CLAUDE.md` + `README.md` (boy-scout law: `AuthN.Services` now carries the disclosure contract alongside the issuance contract), `git add src/AuthN.Services src/AuthN.Components tests CLAUDE.md README.md`.

**SHIP GATE (human): Heimdall** — PR, CI, tag, publish. **Blocking:** Task 19b's `NorseRef` floats on `Version="*"` — do not start 19b until the package restores from the feed.

### Task 19b: The disclosure surface — Himinbjörg (`feature/pii-identity-erasure`, continued)

**Files:**
- Create: `src/Identity.Web.Server/Disclosure/GetMyPersonalDataHandler.cs`, `Disclosure/GetMaskedPersonalDataHandler.cs`, `Disclosure/IdentityService.cs` (+ the `CommandRequest` wrapper types the mediator generator expects — mirror the Mímir/Heimdall handler registration shape exactly, including `[Authorize(Policy = ...)]` so NORSE011 passes). The contract, wire records, and `IdentityPolicies` come from Heimdall's `Norse.AuthN.Services` (Task 19a) — `using Norse.AuthN.Services;`, no new project, no slnx change.
- Delete: `src/Identity.Web.Server/Components/Pages/Manage/PersonalData.razor` (ported to Heimdall in Task 19a) and the `DownloadPersonalData` endpoint mapping in `IdentityComponentsEndpointRouteBuilderExtensions.cs` (superseded by `GetMyPersonalDataAsync` — the 2026-08-04 download-is-a-gRPC-call ruling; the seam-restored scaffold endpoint dies here, no deprecation period)
- Modify: `src/Identity.Web.Server/ServiceCollectionExtensions.cs` — register the concrete `IdentityService` as `IIdentityService` for the Blazor Server in-process path (`AuthenticationService` precedent), alongside the generated gRPC server wiring's automatic `I{Context}Service` discovery
- Test: `tests/Identity.Web.Server.Tests/DisclosureHandlerTests.cs`

- Policies: `IdentityPolicies.Self` = authenticated user (the handler discloses only the principal's own row — authorization is decidable from the principal alone); `IdentityPolicies.MaskedDisclosure` = system-role requirement (`RequireRole(IdentityPolicies.SystemRole)`); both registered where the realm's existing policies register (follow Heimdall's `AuthNPolicies` registration site), using the constants from Task 19a.
- **The repository fold (spec §3.1):** each handler wraps its query in `try { … } catch (KeyDestroyedException e) { return new(new Failed(new Problem { Category = ErrorCategory.Erased, Receipt = e.Receipt })); }` — `KeyMissingException` is deliberately **not** caught (falls to `ExceptionTranslationBehavior` → Fault + correlation id + telemetry: the incident path with zero code). This try/catch **is** the subject-singular fold: both queries are single-subject by construction (spec §4.1 — no list-shaped decrypted read exists on this surface).
- Masked handler masks **through the structs, never by hand**: `EmailAddress.Parse(decrypted)` → `.Masked`; `PhoneNumber.Parse` → `.Masked`; a parse failure of decrypted data is storage corruption → let it throw (`InvalidOperationException` → Fault). No `BirthDate`/`PersonalName` methods yet — no such columns exist; they arrive with the profile surface.
- Self handler: subject id from `IPrincipalAccessor`'s principal (`UserIdClaimType` claim), **never** from the request. No row → `NotFound` (an authenticated principal whose row vanished — legitimate after shred + reregistration churn? No: a shredded user's session died at revalidation; a live principal with no row is data drift → `NotFound` is the honest answer).
- gRPC host: `sealed class IdentityService(ISender sender) : IIdentityService` passthrough (Mímir `ReferenceService` precedent) — the generated server wiring discovers `I{Context}Service` (`Context = Identity`) automatically.

- [ ] **Step 1: Write the failing handler tests**

```csharp
namespace Norse.Identity.Web.Server.Tests;

public sealed class DisclosureHandlerTests(PostgresIdentityFixture fixture) : IClassFixture<PostgresIdentityFixture>
{
	[Fact]
	async Task Self_disclosure_returns_full_decrypted_wire_strings()
	{
		var (context, keyStore) = await fixture.CreateScopeAsync();
		var user = await fixture.SeedUserAsync("me@example.com", phone: "+15551234567");
		GetMyPersonalDataHandler handler = new(context, FakePrincipal.For(user.Id));
		var outcome = await handler.Handle(new(), TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Success<PersonalDataResponse> success).ShouldBeTrue();
		success.Value.Email.ShouldBe("me@example.com");
		success.Value.PhoneNumber.ShouldBe("+15551234567");
	}

	[Fact]
	async Task Masked_disclosure_returns_the_structs_own_masks()
	{
		var (context, keyStore) = await fixture.CreateScopeAsync();
		var user = await fixture.SeedUserAsync("jane@domain.com", phone: "+15551234567");
		GetMaskedPersonalDataHandler handler = new(context);
		var outcome = await handler.Handle(new() { SubjectId = user.Id }, TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Success<MaskedPersonalDataResponse> success).ShouldBeTrue();
		success.Value.Email.ShouldBe("j***@d***.com");
		success.Value.PhoneNumber.ShouldBe("***4567");
	}

	[Fact]
	async Task Reading_a_shredded_subject_answers_erased_with_the_receipt()
	{
		var (context, keyStore) = await fixture.CreateScopeAsync();
		var user = await fixture.SeedUserAsync("gone@example.com");
		var shred = await new ErasureService(context, keyStore).ShredAsync(user.Id, TestContext.Current.CancellationToken);
		shred.TryGetValue(out Success<ErasureReceipt> receipt).ShouldBeTrue();

		GetMaskedPersonalDataHandler handler = new(context);
		var outcome = await handler.Handle(new() { SubjectId = user.Id }, TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Erased);
		failed.Problem.Receipt.ShouldBe(receipt.Value); // spec §8 verify item 11: the typed exception crossed EF materialization intact
	}

	[Fact]
	async Task Unknown_subject_answers_not_found_not_erased()
	{
		var (context, _) = await fixture.CreateScopeAsync();
		GetMaskedPersonalDataHandler handler = new(context);
		var outcome = await handler.Handle(new() { SubjectId = Guid.NewGuid() }, TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.NotFound);
	}
}
```

The third test **is** spec §8 verify item 11 running against a real database: seed through the real protector, shred, then materialize — `KeyDestroyedException` must surface from inside EF's materializer undistorted. If EF wraps it, the fold's catch adapts (catch the wrap, unwrap via `InnerException` pattern-walk) — adjust implementation, keep the test's assertion untouched, and record the finding in the commit message.

- [ ] **Step 2: Run to verify failure** — handlers do not exist.

- [ ] **Step 3: Implement** per the Interfaces block. Handler shape (masked; self is analogous with the principal-sourced id):

```csharp
sealed class GetMaskedPersonalDataHandler(NorseIdentityDbContext context) :
	IRequestHandler<MaskedPersonalDataCommand, MaskedPersonalDataResponse>   // exact marker/wrapper shape per the realm's existing handlers — mirror, don't invent
{
	public async ValueTask<Outcome<MaskedPersonalDataResponse>> Handle(…)
	{
		try
		{
			var row = await context.Users.AsNoTracking()
				.Where(u => u.Id == request.SubjectId)
				.Select(u => new { u.Email, u.PhoneNumber })
				.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
			if (row is null)
				return Outcome<MaskedPersonalDataResponse>.Err(ErrorCategory.NotFound);
			return Outcome<MaskedPersonalDataResponse>.Ok(new()
			{
				Email = Mask<EmailAddress>(row.Email),
				PhoneNumber = row.PhoneNumber is { Length: > 0 } phone ? Mask<PhoneNumber>(phone) : ""
			});
		}
		catch (KeyDestroyedException e)
		{
			return new(new Failed(new Problem { Category = ErrorCategory.Erased, Receipt = e.Receipt }));
		}
	}

	static string Mask<TPii>(string? wire) where TPii : struct, IPiiScalar<TPii> =>
		TPii.Parse(wire).TryGetValue(out Success<TPii> success) ?
			success.Value.Masked :
			throw new InvalidOperationException($"Decrypted {typeof(TPii).Name} no longer parses — storage corruption.");
}
```

- [ ] **Step 4: Run the full realm suite** — `dotnet build Himinbjorg.slnx && dotnet test Himinbjorg.slnx`.

- [ ] **Step 5: Update realm docs and commit**

Update `Himinbjorg/CLAUDE.md` + `README.md` (protection seam live, disclosure surface, subject_keys, temporal posture, disclosure contract consumed from Heimdall's `AuthN.Services` — Task 19a amendment).

```bash
git add src/Identity.Web.Server/Disclosure tests/Identity.Web.Server.Tests/DisclosureHandlerTests.cs CLAUDE.md README.md
git commit -m "feat: PII disclosure surface — self full, second-party masked, erased honest"
```

**SHIP GATE (human): Himinbjörg** — PR, CI, tag, publish.

---

## Phase F — Yggdrasil (`feature/dev-keys-wiring`)

### Task 20: Composition-root wiring

**Files:**
- Modify: `Directory.Packages.props` (no new pins — `Norse.Abstractions.Backend` and `Norse.Infrastructure.Backend` are already pinned; bump the version variables for every realm package this effort shipped)
- Modify: the web-server and migrations-host bootstrap (`src/Hosting.Web.Server/…`, `src/Hosting.Migrations/…` — locate the existing `AddNorseIdentity()`/`AddNorseMigrations()` call sites and add `AddNorseDevelopmentKeys(...)` beside them, rooted at a content-root-relative `norse-dev-keys/` path so identities survive restarts; never a machine-absolute path)

- [ ] **Step 1: Wire, build, run the existing suites**

Run (from `Yggdrasil/`): `dotnet build Yggdrasil.slnx && dotnet test Yggdrasil.slnx`
Expected: green — the DI graph resolves (`IPersonalDataProtector` → dev store chain) and nothing else moved.

- [ ] **Step 2: End-to-end smoke through Bifröst**

Run (from `Bifrost/`): `dotnet run --project src/Orchestration.AppHost` — migrations service completes against Postgres (`norse_identity` stands up with the new schema); register a user through the running Heimdall flow; confirm in the dashboard's Postgres that the user row's `Email` is `v1:`-prefixed ciphertext and `NormalizedEmail` is a base64 HMAC. This is the whole design breathing.

- [ ] **Step 3: Commit**

```bash
git checkout -b feature/dev-keys-wiring
git add Directory.Packages.props src/Hosting.Web.Server src/Hosting.Migrations
git commit -m "feat: wire dev-grade key seam at the composition root"
```

**SHIP GATE (human): Yggdrasil** — PR, CI, tag.

---

## Self-Review Notes (performed at authoring)

0. **Curation pass (2026-08-03, post-Law-of-the-Realms — see the Global Constraints CURATION block):** `MaskedValueJsonConverter<T>` deleted from Tasks 1–5 (NORSE070) and reborn as Task 12b inside the wire border; the Keys contracts/dev store fold into the `Abstractions.Backend`/`Infrastructure.Backend` pair under `Keys/` folders (no-functional-group-packages ruling) — Tasks 8, 12, 16, 18–20 amended accordingly; Task 6's analyzer metadata names ride `Norse.Primitives.Pii.*`; the resume opens with the strip commit on `feature/pii-primitives`. Where a coverage line below says "§1.5 layer 2 → Task 1", read Task 12b.
1. **Spec coverage:** §1 → Tasks 1–5; §1.5 layer 2 → Task 12b (curated; originally Task 1); §1.6 → Task 1; §2 → Tasks 7, 9, 10, 11; §3.1 → Tasks 8, 16, 19; §3.2 → Tasks 8, 15; §3.3 → Tasks 12, 20; §3.4 TTL/backup laws → dev-scope in Task 12 (production-provider obligations documented, not testable until a vault provider exists — deliberate); §4.1 → Tasks 13, 16; §4.2 → Tasks 15, 18; §4.3 → Tasks 14, 15; §4.4 → Task 17; §4.5 → Tasks 6 (fixture), 15, 16; §5 → Task 6; §6 → Task 19; §8 items 1–3 → Task 14, item 4 → Tasks 13/16 (singleton law), items 5–6 → Tasks 10/15, item 7 → Task 17, items 8–9 → Tasks 12/16, items 10–11 → Tasks 18/19.
2. **Known deliberate deferrals** (spec §7 already blesses them): production vault-backed `ISubjectKeyStore` over the `subject_keys` table; Syn DSAR trigger for `ErasureService`; `ProtectPiiScalars` call site in `NorseIdentityDbContext` (lands with the first struct-typed profile property); lookup-keyring re-hash ceremony tooling. **Added 2026-08-04 (ruled during Phase E):** the temporal sweep + lockout split (spec §4.3) — deferred on an EF Core 11 preview defect (see the Task 15 amendment banner for the diagnosis and fold-in trigger); returns as its own effort when EF's composition generates clean DDL, at which point the full e2e tie-out runs across all realms with the dual local vault containers (Vault/OpenBao Transit + Azure Key Vault emulator) composed in Bifröst.
3. **Type-consistency pass:** `IPiiScalar<TSelf>` (Tasks 1/13/19), `SubjectKeyResult.Match(available, destroyed, missing)` (Tasks 8/12/16/18), `ErasureReceipt(ReceiptId, SeveredAt)` (Tasks 7–19), envelope `v1:{subjectId:D}:{base64}` (Tasks 16/20) — verified consistent.
4. **Coordination:** Phase E opens with the rebaseline gate for the parallel `SplitToTable` session; Tasks 14–15 collapse to consumption if that work lands first.
5. **Amendment (2026-08-04, disclosure contract rides Heimdall — Buvy's ruling):** Task 19 split into **19a** (Heimdall `feature/pii-disclosure-contract`: `IIdentityService` + four wire records + `IdentityPolicies` in `AuthN.Services`, `GetMaskedPersonalDataRequestValidator` in `AuthN.Components`, own ship gate — Himinbjörg's `NorseRef` floats on `Version="*"`, so publish precedes 19b) and **19b** (Himinbjörg: handlers, command wrappers, `IdentityService` passthrough — otherwise unchanged). The Himinbjörg `Identity.Services` project is never created. Where a line elsewhere says "Task 19", read 19a for the wire tier and 19b for the server tier. Assessed in the same pass, second ruling folded same day: **download personal data becomes a gRPC call**, so `PersonalData.razor` ports to Heimdall in 19a (injection-clean — `IIdentityService` + `NavigationManager`; client-side file save) and 19b deletes Himinbjörg's page + the seam-restored `DownloadPersonalData` scaffold endpoint. `DeletePersonalData.razor` alone stays in Himinbjörg — its delete semantics become the shred ceremony, and the wire-exposed shred trigger is the recorded spec-§7 deferral (Syn DSAR trigger), designed in the validation-work round.
6. **Amendment (2026-08-04, shred + session ruling):** implementation proved EF materialization does NOT wrap `KeyDestroyedException` — a shredded subject's surviving cookie made `SecurityStampValidator`'s user-fetch throw (repeating 500 out of the cookie middleware) before any stamp comparison, so Task 18's `ShouldBeNull` session-death test was unreachable as designed. **Ruled: a destroyed key IS a dead session** — `NorseSignInManager.ValidateSecurityStampAsync` folds `KeyDestroyedException` to null (clean rejection/sign-out), restoring the plan's test as written. The fold is deliberately narrow: the throw stays live on every other path — Task 19b's disclosure fold (Erased + receipt) depends on it; `NorseUserStore` and the protector never catch. Recorded alongside: Task 18 found `SecurityStamp` carries no value converter (`IdentityValueConverters.Stamp` is ConcurrencyStamp-only) and is `HasMaxLength(32)` — rotation format is `"N"`, exactly the contingency the task text anticipated.
7. **Forseti corrections folded (2026-08-03 review):** (1) `NorseUserManager` is the production `SubjectCryptoScope` chokepoint — the seam is wired at the one point every write traverses, with a no-manual-scope test proving it (Task 16); (2) session-death test upgraded to the real path — principal built by the real claims factory, verdict from `SignInManager.ValidateSecurityStampAsync`, interlocking with Task 17's allowlist (Task 18); (3) ceremony partial-failure contract documented as half-severed-but-retryable, with a throw-once/retry-completes test (Task 18); (4) analyzer restructured to an explicit three-way with a `TestEmail[]` fixture pinning array-of-PII to NORSE062 (Task 6). Margin notes: `NormalizedEmail` index non-uniqueness tripwire test (Task 15); fresh-instance-per-access comment on the dev-store test property (Task 12).




