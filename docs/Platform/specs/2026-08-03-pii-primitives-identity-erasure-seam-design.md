# PII Primitives, Identity Integration, and the Erasure Seam — Design

**Status:** DRAFT for Forseti review (brainstorm output, 2026-08-03). No plan, no implementation, until this passes the gate.
**Realms touched:** Svartálfheim (primitives, analyzer), Asgard (`ErrorCategory` amendment, key-seam contracts), Himinbjörg (Identity integration, key table, claims factory), Urðarbrunnr (encryption converters), Midgard (edge folds, dev key provider), Glitnir (this verdict).
**Inherits without re-litigation:** the privacy verdicts in `Glitnir/docs/norse-architecture-feature-roadmap.md` — crypto-shredding over tombstoning; the envelope law (wrapped DEKs in rows, wrapping keys never in temporal storage); two key planes (per-subject payload DEKs, service-level lookup keyring); Class A/C residence in Himinbjörg; opaque deterministic GUIDs everywhere else; unwrapped-DEK memory-only TTL = shred-latency SLA; backup-restore-cannot-resurrect; Syn erasure receipts as the one permanent record; NIST SP 800-88 cryptographic-erasure provenance.

---

## 1. Svartálfheim — the PII primitives

Four new `readonly record struct` types in `Norse.Primitives`: **`EmailAddress`**, **`PhoneNumber`**, **`PersonalName`**, **`BirthDate`**.

### 1.1 `PersonalName` is a single name component

Not a composite. `GivenName`, `MiddleName`, `FamilyName` on a composing entity are each a `PersonalName` field; how many components exist and what order they render in is the consumer's concern. Cultural ordering, name count, and display composition never enter the primitive. The struct is smart about exactly one thing: one name, its normalization, its mask.

### 1.2 Construction

`Result<T>`-based, per the Two Unions law — these are untrusted external inputs. Shape follows the `IsoCountryCodes` precedent (`Mimisbrunnr/gen/Reference.Data.Primitives.Generator/IsoCountryCodeEmitter.cs`):

- `static Result<T> Parse(ReadOnlySpan<char> value)` + a `string?` overload forwarding to it.
- `static bool TryParse(...)` implemented over `Parse(...).TryGetValue(...)`.
- Culture-insensitive; **no `IFormatProvider` parameter**. Empty/whitespace → `ParseFailure.Empty`; bad shape → `ParseFailure.Malformed`.
- No throwing constructors on the external-input path.

`PhoneNumber` validates **E.164 shape only** — leading `+`, digit run, length bounds. Regional/cultural validity and rendering are service concerns; E.164 canonical storage is what makes correct regional rendering possible later. Named trigger to revisit: a real consumer needing region-aware validation.

### 1.3 Normalization is a member of the type

`EmailAddress.Normalized` (case-folded canonical form) and `PhoneNumber.Normalized` (E.164) are the exact strings the blind-index HMAC is computed over. One definition, consumed by Identity's lookup protector, Urðarbrunnr, and every future realm — normalization drift between lookup writer and lookup reader becomes unrepresentable. `PersonalName` carries a `Normalized` form (case-folded, Unicode-normalized) for potential future name search; it is not blind-indexed in this scope.

### 1.4 `IMaskedValue` — the masking law and the analyzer marker

Resident in Svartálfheim beside the structs (the forge references nothing; the interface its structs are forced to implement must live beside them or the realm hierarchy inverts). Two members, two jobs:

```csharp
public interface IMaskedValue
{
	string Masked { get; }              // pure, clock-free — what ToString() and the JSON write path emit
	string ToMasked(DateOnly asOf);     // disclosure-time mask — most implementers return Masked, ignoring asOf
}
```

- **Both faces are wire strings.** Unmasked = the canonical wire string the transport contracts already use; masked = also a string. The edge stays string-typed.
- **Masked output is a value, never prose.** `"38"`, never `"Age: 38"` — labels belong to DTO field names. No English inside wire values.
- **No clock in the primitive.** Time-dependent masking is a pure function of *(value, asOf)*; the caller supplies the date. Masked age is computed at disclosure time, never stored.
- **`IMaskedValue` is the PII marker the analyzer keys on** (§5). Implementing it is what makes a type PII in the compiler's eyes; a downstream consumer's custom PII struct joins the governance regime by implementing it and cannot opt into PII status while opting out of masking — they are the same symbol.

Per-struct masking law:

| Struct | `Masked` (pure; logs, `ToString()`, JSON write) | `ToMasked(asOf)` (disclosure) |
|---|---|---|
| `EmailAddress` | first char + `***` each side of `@`, TLD kept: `j***@d***.com` | same |
| `PhoneNumber` | last four digits only: `***1234` (no country-code leak) | same |
| `PersonalName` | single initial with period: `"B."` (grouped `"B.B."` rendering is display-layer composition) | same |
| `BirthDate` | zero-information redaction: `"****-**-**"` (a log line has no business knowing an age) | exact current age: `"38"` — not a bracket; age is the operationally load-bearing datum in insurance and identifies nobody alone |

No `Over18`/`Over21` named predicates. Consumers with a threshold need compute it from the disclosed age; a no-disclosure threshold check ("verify ≥ 18 without learning the age") is a purpose-built endpoint later if a real need arrives.

### 1.5 The three-layer muddle defense

1. **`ToString()` returns `Masked`.** Logs, interpolation, exception messages, debugger (`DebuggerDisplay` likewise) — every accidental rendering path emits muddle. Plaintext egress is a named, deliberate member only.
2. **The System.Text.Json converter writes `Masked` and throws on read.** Catches serialization paths no analyzer can see (telemetry enrichers, ad-hoc `JsonSerializer.Serialize`). Read-side resurrection fails loudly — the same tripwire posture the-two-unions doctrine prescribes. Rationale for read-throw rather than round-trip: masked forms can be *syntactically valid* inputs (`j***@d***.com` parses as an email address); a lossy round-trip that succeeds would fabricate a well-formed value that silently is not the person's data.
3. **The analyzer** (§5) makes the storage-side wrongs uncompilable.

Wire DTOs are unaffected by layer 2: transport contracts carry plain `string` fields filled explicitly at the disclosure edge. `IMaskedValue` implementers remain banned from Futhark closures (already law: `Glitnir/docs/Platform/specs/2026-08-01-opinionated-xml-serialization-design.md`).

### 1.6 `[RetentionPolicy]` — resident in Svartálfheim, property-granular

The brief leaned Asgard; ruled Svartálfheim. The analyzer ships inside the `Norse.Primitives` package — if the attribute lived elsewhere, an assembly referencing only the primitives could see the diagnostic but not satisfy it, or the analyzer would silently self-disable on an unresolvable symbol. Interface, attribute, structs, and analyzer travel as one self-contained package; `[MustConsume]` set the precedent for enforcement attributes living in the forge.

- **Property/field targets only** (`AttributeTargets.Property | AttributeTargets.Field`, mirroring the deliberate restriction on Urðarbrunnr's `MaxLengthAttribute`). No entity-level form: classification is ratified per field, never per table. A mixed-strategy table — a user-can-delete-whenever column beside a statutory-7-year column — is legal and boring: two columns, two declared bases, two key planes, zero per-row key storage.
- Shape: `[RetentionPolicy(RetentionBasis.SubjectKey)]` now; `RetentionBasis.StatutoryEpoch` + optional citation string reserved for Class B downstream. `RetentionBasis` is a closed enum with an explicit `Unspecified = 0` sentinel the attribute constructor rejects.

### 1.7 Out of scope

`PostalAddress`, national IDs — they arrive when a consuming record needs them. The four structs above give the analyzer and Identity integration their full proving surface.

---

## 2. The `Erased` state — `Outcome` taxonomy verdict (doctrine)

**Ratified shape: `ErrorCategory.Erased = 11` — a new category, not a third union arm, not an `Erasable<T>` wrapper.** This section is the explicit Glitnir verdict the brief's hard gate requires.

*(Value note: 11 is the next free explicit value after `MultipleMatches = 10` at authoring; re-verify the slot at implementation.)*

### 2.1 The three answers, only one of them new

| Situation | Answer | New? |
|---|---|---|
| No row | `ErrorCategory.NotFound` | existing, unchanged |
| Cannot decrypt, key *should* exist (missing without receipt) | `ErrorCategory.Fault` + `CorrelationId` + a distinguished error entry — an incident; it pages someone; it never masquerades as erasure | existing category, distinguished payload |
| Row exists, person intentionally severed | **`ErrorCategory.Erased`** — the system working as designed | the single new state |

### 2.2 Why a category, not an arm

- **The wire already decided.** Both transport edges collapse every non-success into problem shape: `OutcomeServerInterceptor` throws `Problem.ToRpcException()` with `ErrorInfo.Reason` authoritative; the client decoder re-envelopes trailers into `Failed(Problem)`. A third in-process arm would degenerate into problem shape at the edge anyway, and rebuilding it client-side means new machinery in `OutcomeFactory`, both interceptors, and the trailer decode — to reconstruct a distinction the category carries for free. The XML serialization spec (2026-08-01) already names the `Outcome<T>` fold as the landing site for `Erased` → 410 Gone with the receipt as a problem extension member; with the category shape, the ratified edge design and the in-process design are the same design.
- **The taxonomy already holds non-incident members.** `NotFound`, `Conflict`, `Forbidden` are not incidents; `Failed` means "no value, and here is the honest reason." `Erased` sits naturally in that family.
- **A third arm is the expensive path**: breaking three-arm `Match` on the platform's only consumption door, arm-specific interceptor patterns, `OutcomeFactory`'s hardcoded `Failed` constructor lookup, and a surrogate with no wire representation by construction. Every touch point for the category — `ErrorCategory`, the two status-mapping switches, `ProblemXmlWriter` — already has a documented default expecting exactly this addition.

### 2.3 The category is producer-agnostic; the receipt is not part of its identity

`Erased` means *intentionally gone, record existed, working as designed*. Two producers from day one:

- **Crypto-shred** (Himinbjörg): receipt populated — the Syn ledger reference makes the answer self-auditing.
- **Content tombstone** (future CMS-shaped consumers): a post retired into temporal history is erased in exactly the sense a search engine should hear — 410 Gone — with no Syn receipt, receipt member null.

`Problem` gains one optional typed member:

```csharp
public sealed record ErasureReceipt(Guid ReceiptId, DateTimeOffset SeveredAt);
// on Problem:
public ErasureReceipt? Receipt { get; init; }   // populated only when Category == Erased and a ledger entry exists
```

### 2.4 Edge mappings

| Edge | Mapping |
|---|---|
| gRPC (`ProblemExtensions.ToRpcException`) | `StatusCode.NotFound` + `ErrorInfo.Reason = "Erased"` (the status mapping is documented non-injective; `Reason` is the authoritative channel); receipt fields ride `ErrorInfo.Metadata` (`receipt`, `severedAt`) |
| gRPC client decode | trusts `Reason` only, per existing law; rehydrates `Receipt` from metadata |
| REST fold (`GrpcControllerBase`) | **410 Gone**, receipt as RFC 9457 extension members (`receipt`, `severedAt`) |
| XML | the extension-member case `ProblemXmlWriter.cs` already promises in its remarks |
| JSON tripwire | recorded adjacent debt: doctrine says `JsonConverter<Outcome<T>>` throws both directions; no such converter exists in the tree. Not this scope's obligation, but the gap is now recorded twice — close it when Midgard is next open |

`the-two-unions.md` gets a short addendum with the implementation: a category addition does not reopen the union; the arms remain two.

---

## 3. The key seam

### 3.1 Contracts — Asgard

New assembly `Norse.Abstractions.Keys` (project `Abstractions.Keys`; name final at Forseti review). Deliberately small: custody, wrap/unwrap, scheduled destruction. Algorithm choices are ours (AES-256-GCM per the standing 2026-06-03 ruling), never the provider's.

**The seam's honesty contract is its own small closed union, and it gets a test:**

```
SubjectKeyResult = Available(dek) | Destroyed(receipt) | Missing
```

- `Destroyed(receipt)` — a Surtr-burned key with its Syn receipt → the repository answers `Erased`.
- `Missing` — no key and no receipt → the repository answers `Fault`; an incident, never erasure.

The repository's honesty depends on the vault's honesty; the `Destroyed`-vs-`Missing` distinction is the load-bearing three-state seam (the public read taxonomy in §2 is its downstream rendering). This is not `Outcome<T>` and not `Result<T>` — it is a seam-local contract, matching the doctrine that the two unions are never siblings and never grow domain-specific arms.

**The materialization channel.** An EF value converter is a pure value→value lambda with no return path for a union, so the seam surfaces through the protector as two distinguished typed exceptions mapped one-to-one from the union — `KeyDestroyedException(ErasureReceipt)` and `KeyMissingException` — thrown during decryption. They are machinery-internal: the disclosure repository's fold catches and translates them (`Destroyed` → `ErrorCategory.Erased` + receipt; `Missing` → `Fault` + incident) — the one place that already speaks `Outcome`. One that escapes uncaught lands in the existing unhandled-exception interceptor as an honest `Fault`. This is fail-loudly, not control-flow-by-exception: a burned key encountered mid-materialization *is* exceptional at the converter's altitude; the typed pair exists so the fold can be exact instead of guessing from a message string.

Also in scope here: the `ILookupProtectorKeyRing` backing contract for the lookup plane (service-level, rotatable).

### 3.2 Wrap topology (Q7)

- **Per-subject key table in Himinbjörg** — `subject_keys`: `subject_id` PK, wrapped DEK, wrapping-key reference, created-at. **Non-temporal** (no legitimate history question; the envelope law permits wrapped DEKs in rows regardless).
- The **shred point is singular**: the per-subject wrapping key in the platform key store (KEK-wrapped, non-temporal, backup window ≤ shred-latency SLA). Key destruction darkens the wrapped DEK everywhere — current rows, temporal history, backups — in one operation. The naming candidate for the destruction mechanism remains **Surtr** per the roadmap; binding happens when the component becomes real, per the codenames law.
- **No per-row wrapped-DEK columns, ever.** Wraps scale with *strategies × scope units* (one subject-key row per subject; one epoch key per realm/jurisdiction/bucket downstream for Class B), never with data rows. Named trigger: Class B epoch machinery is downstream-consumer scope, not this design.

### 3.3 Dev-grade provider

Local dev needs the seam functioning with no cloud vault: an in-memory/file-backed provider, clearly dev-grade, never a production path. Contracts in Asgard; dev provider in Midgard; wiring at the composition root (Yggdrasil). Himinbjörg references the Asgard contracts only — never Midgard (standing law).

### 3.4 Operational laws restated as test obligations

- Unwrapped DEKs cached in memory only, TTL = declared shred-latency SLA.
- Key-store backup lifecycle guarantees DB restore cannot resurrect the dead — **this gets a test**.
- Lookup keyring rotation is a **re-hash ceremony** over all current rows (old hashes become unfindable under the new key), never a config flip.

---

## 4. Himinbjörg — Identity integration

### 4.1 The converter law: three lanes, no ordering problem

EF applies exactly one value converter per property, so there is no converter ordering — there is one composed converter per lane:

1. **Inherited Identity string columns** (`Email`, `PhoneNumber`, `UserName`): Identity's own mechanism, untouched. `IdentityOptions.Stores.ProtectPersonalData = true`; `IdentityUserContext.OnModelCreating` wraps `[ProtectedPersonalData]` string properties with its `PersonalDataConverter` backed by **our** `IPersonalDataProtector` (per-subject DEK, envelope-wrapped, key seam underneath). Their converter, their store, our protector — Identity's columns are beholden to the same shredder because the shredder is under everything.
2. **Custom struct-typed PII properties** (future profile surface — `BirthDate`, `PersonalName` components): Identity's path throws on non-string `[ProtectedPersonalData]` properties, so these get a single Urðarbrunnr-shaped `ValueConverter<TPii, string>` whose lambda is canonical-wire-string ∘ `protector.Protect`, built by a factory resolving `IPersonalDataProtector` the same way Identity's model building does. One converter; composition internal; registered after `base.OnModelCreating`. Never two stacked converters fighting over a property.
3. **The lookup plane never touches EF.** `NormalizedEmail`/`NormalizedUserName` are protected by `ILookupProtector` inside the user store on write and on `FindBy*` compare — store machinery we back with the keyring.

**Written law (so nobody "fixes" it later):** email is the username, so `NormalizedEmail` and `NormalizedUserName` hold the **same blind-index HMAC**. Correct and expected — the whole Identity toolkit compares protected normalized values and works unchanged.

**PII-bearing projections are subject-singular (law).** A query that materializes decrypted PII columns is always scoped to exactly one subject — enforced structurally by the disclosure repository contract: no list-shaped decrypted read exists on it. List and admin views project masked strings built at projection time (§5.3's sanctioned pattern, extended from documents to views) or carry no PII at all. Consequence: the poisoned-list problem — one shredded subject's row throwing mid-materialization of a many-row query — cannot arise, because the only queries that can encounter `KeyDestroyedException` are single-subject by construction, and there the throw *is* the answer (§3.1's fold → `Erased`). Analyzer candidate **NORSE063 reserved** for the day a generic query surface exposes decrypted-PII materialization; until then the contract shape is the enforcement.

Ciphertext columns are base64 text, not `bytea` — the Identity protector seam is string→string; common-sense override on the binary-storage preference. Lengths are declared and pragmatic: base64(nonce + UTF-8 plaintext + GCM tag) ≈ 4/3 × plaintext + ~40, declared with generous headroom per column; `[UnboundedLength]` is the explicit escape for anything genuinely unboundable. No ciphertext column is ever indexed.

### 4.2 Erasure mechanics and re-registration

The shred ceremony is three acts, the database acts committing before or atomically with the key destruction: **null the current-row lookup hashes**, **rotate the security stamp**, **destroy the per-subject wrapping key** (receipt to Syn). Payload ciphertext stays in place, dark — the documented posture. The stamp rotation is what arms §4.4's session-kill claim: without it, `SecurityStampValidator` revalidation would keep succeeding and a dead user's open browser tab would outlive the person until cookie expiry. With it, every live session is dead within one revalidation interval of shred — and that interval is thereby part of the shred-latency SLA conversation.

**Re-registration works because of the nulling, not key movement.** The lookup keyring is service-level and unchanged by erasure; `HMAC(lookup_key, email)` is identical for the erased registration and tomorrow's re-registration — determinism is the point of the blind index. The old row's hashes are null, so the fresh row inserts cleanly and `FindByEmailAsync` finds exactly one match.

Schema consequences:

- `NormalizedUserName` becomes **nullable** (current `IsRequired()` removed). `UserName` stays required — payload columns are darkened, not nulled.
- The unique index on `NormalizedUserName` becomes **provider-aware filtered**: SQL Server requires `WHERE normalized_user_name IS NOT NULL` (its unique indexes admit one NULL; the second erasure ever performed would otherwise violate it). Postgres treats NULLs as distinct by default — no filter needed.
- **Honest-residue addendum for the doctrine:** temporal history keeps old HMACs, and since the keyring is unchanged, a re-registered email's new hash *equals* the erased user's historical hashes — history access permits inferring "this email belonged to someone who erased." Within the ratified pseudonymous-residue posture; keyring rotation is the documented remedy.

### 4.3 Temporal posture: everything, with the lockout columns split out

The temporal hostage-taker on the users table is two columns of rate-limiter state — `AccessFailedCount`, `LockoutEnd` — which are operational telemetry, not identity record. **EF entity splitting** maps exactly those columns to a non-temporal side table (`user_lockout`, same PK) while the entity stays whole: `UserManager`/`SignInManager` read and write the properties unchanged; EF routes the columns. Users table goes temporal; wrong-password churn never mints a history row; a failed-attempt counter was never "who this person is changed."

- Everything else in Himinbjörg goes temporal (SQL Server system-versioning; Postgres has no native equivalent — its temporal story rides the Norns design when it lands; this decision shapes the SQL Server mapping now).
- Crypto-shredding was designed for exactly this coexistence: history rows keep ciphertext under a destroyed DEK (dark) and HMAC residue (documented).
- **The auth posture stays undecided here.** SSO-only remains attractive (attack surface, password machinery evaporates into the IdP) but is a product-posture decision to be taken on its own verdict, not forced by a history table. The reference platform demonstrates both postures; product bridges pick their lineup.

### 4.4 Claims factory

`NorseUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<NorseUser, NorseRole>`: let the base build, then **allowlist-filter** — keep only the closed set, drop everything else by default (a strip-list leaks whatever claim Microsoft adds next release; an allowlist drops it).

- Kept: `NameIdentifier` (the opaque GUID), role claims, the **security-stamp claim** (required by `SecurityStampValidator` cookie revalidation — and, because §4.2's shred ceremony rotates the stamp, that revalidation is the mechanism that kills a dead user's live sessions post-shred), and whatever authentication-method claims the flow requires (exact closed list is a verify item, pinned against .NET 10's factory output at plan time).
- Omitted: `ClaimTypes.Name` and every plaintext PII claim. `User.Identity.Name` is null; display names come from the disclosure surface. Named trigger: if a claim-borne display name becomes a real need, the sanctioned value is the masked rendering, never plaintext.
- Default claim set on the wire is **GUID + roles — nothing else**; encrypted-PII claims are added only by declared consumer need (a claim the consumer cannot decrypt is dead weight plus presence/length metadata leakage).
- **Enforcement: runtime test now** — build a principal from a fully populated user, assert the claim set is *exactly* the closed list; any surplus claim fails the build. A banned-symbol analyzer waits for banned-symbol infrastructure to exist for its own reasons (none exists today; building the stack for one rule is ceremony). Named trigger: when Ginnungagap grows banned-symbol config, add `ClaimTypes.*` bans outside the factory.

### 4.5 Retention ceremony and the proving-ground honesty

Himinbjörg is 100% subject-key erasure — no epochs, no statutory carve-outs; its Glitnir retention-register entry is short and uniform, and exists anyway.

- `NorseUser` overrides its virtual inherited PII properties (`Email`, `PhoneNumber`, `UserName` — precedent: the existing `SecurityStamp` override) and declares `[RetentionPolicy(RetentionBasis.SubjectKey)]` on each: register ceremony, honestly declared.
- **Honesty note:** the analyzer (§5) keys on `IMaskedValue`-typed members and cannot force declarations on inherited `string` properties — those are covered by convention plus the `[ProtectedPersonalData]` machinery. The analyzer's live in-realm proof lands with the first struct-typed PII property (the profile surface); until then the proof lives in the analyzer test suite's fixtures, including the removal-fails-the-build test.
- 2FA/verification flows: phone and email decrypt at send time through the vault, transient in the outbound payload, stored nowhere downstream (settled; restated as an implementation obligation).

---

## 5. The analyzer — NORSE061, NORSE062

### 5.1 Diagnostic block

**NORSE061–069 claimed.** The brief's proposed NORSE040+ collides: 034 is taken (Urðarbrunnr's ambiguous-`ModelSnapshot`), and the 040 decade is reserved on paper for Wells (`Glitnir/docs/Platform/plans/2026-08-01-well-seam-midgard-excision.md`). Svartálfheim opened the 060 decade with NORSE060 and this analyzer lives in the same package — the decade extends naturally. Registry update goes in the `Svartalfheim/gen/Primitives.Analyzers/Diagnostics.cs` header ledger, per the established ritual (platform-wide grep recorded in remarks).

| ID | Law |
|---|---|
| **NORSE061** | A persisted root composes an `IMaskedValue` implementer on a property with no `[RetentionPolicy]` declaration → build error. |
| **NORSE062** | An `IMaskedValue` implementer appears anywhere other than a *direct scalar property* of a persisted root — nested inside a composed type, or as a collection element — → build error. No attribute cures it. |

### 5.2 Scope: transitive walk from persisted roots

- **Roots:** types implementing `INorseEntity<TSelf>` (catches both tiers — Himinbjörg's Identity-based entities implement it directly). Well view declarations join the root set when the well seam ships — named trigger, no speculative code.
- **Walk:** BFS over every type reachable through properties (collection items, composed types) — the `ResponseClosureWalker` shape NORSE060 already uses, cycle-safe.
- **Granularity: the property.** Coverage is judged per declared member, matching §1.6; entity-level shorthand does not exist.
- Assembly-level scoping is rejected as both too coarse (fires on wire DTOs and view models composing PII transiently — retention is a storage concern; a `LoginRequest` holding an `EmailAddress` for thirty seconds needs no retention basis) and too weak (catches nothing the walk misses).

### 5.3 Why NORSE062 is a ban, not a mask

The encryption seam is a value converter, and a converter operates per scalar property. An `EmailAddress` inside a JSON-mapped owned type — or in a primitive collection, which EF maps to JSON — serializes into the document as plaintext: a shredder escape *inside* a retention-declared entity, outside the key seam's reach. And auto-masking the serialization instead would create lossy round-trips that succeed (§1.5). So: PII lives in direct scalar columns where the encrypting converter reaches, or it does not get stored. **The sanctioned pattern for PII-adjacent JSONB views/documents is projecting the masked `string` at build time** — the muddled rendering enters the document; the struct never does; nothing can pretend to round-trip. The same pattern is the law for list/admin read views, per the subject-singular projection law (§4.1).

### 5.4 Infrastructure

- Lives in `Svartalfheim/gen/Primitives.Analyzers` beside NORSE060; ships in the `Norse.Primitives` package (`analyzers/dotnet/cs/`) — zero opt-in for every referencing assembly.
- `WellKnownTypes.Resolve`-style symbol resolution by metadata name (`INorseEntity` resolved without referencing Urðarbrunnr); `IMaskedValue` and `RetentionPolicyAttribute` are same-package symbols — no resolution gap, no silent self-disable.
- Tests follow the existing hand-rolled harness (`AnalyzerTestHarness`, compile-clean-first assertion, stub types for foreign symbols), one test per diagnostic per shape, plus the Himinbjörg removal-fails-build fixture.

---

## 6. The gRPC disclosure surface

New methods on Himinbjörg's system-role-restricted lookup service (settled residence).

| Caller | Disclosure |
|---|---|
| Subject-self (principal's own GUID) | Full decrypted primitives (canonical wire strings) |
| Second party with declared need (system role, ratified per method) | **Masked only** — the struct's own `ToMasked(asOf)`; the endpoint chooses masked, it never authors a mask |
| Everyone else | Denied — constant, honest, pre-touch (below) |

### 6.1 Existence-oracle law (Q8 verdict)

**Disclosure-surface authorization must be decidable from principal + request alone; any policy that needs the row to decide is a design error on this surface.** Role membership and `requested_id == principal_id` are facts about the principal and the request — so denial happens in `AuthorizationBehavior`, before the handler, before any DB roundtrip (positionally guaranteed by the mediator pipeline). The denial is therefore constant by construction: identical whether the subject exists, doesn't, or was erased — and being constant, it leaks nothing, so it is an **honest `Forbidden`**, no `NotFound` masquerade. Masquerade is for flows forced to touch data before deciding (Heimdall's login anti-enumeration); this surface never is. The timing side-channel dies with the same stone: no DB variance on the denied path.

Authorized callers reading an erased subject get `ErrorCategory.Erased` + receipt (§2) — self-auditing: severed on X, receipt Y.

---

## 7. Deliberately out of scope

- `PostalAddress`, national IDs (§1.7).
- Class B epoch-key machinery, epoch destruction ceremonies, the retention-register tooling — downstream-consumer scope; this design reserves their seams (`RetentionBasis.StatutoryEpoch`, epoch wrap topology in §3.2).
- The SSO-only auth posture (§4.3) — product decision, own verdict.
- The banned-symbol claims analyzer (§4.4) — named trigger recorded.
- Changes to no-tracking law, snake_case law, provider seam topology. SQLite stays local-dev-only; the dev key provider (§3.3) is what keeps the seam functional there.
- The `Outcome<T>` JSON tripwire (§2.4) — recorded debt, not this scope.

## 8. Verify items (plan-time gates, before code composes them)

| # | Item |
|---|---|
| 1 | Exact `[ProtectedPersonalData]`/`[PersonalData]` attribute set on .NET 10 `IdentityUser` properties — which columns Identity's converter path claims vs the store's lookup-protector path |
| 2 | `IsTemporal()` + `SplitToTable` composing on the main table in EF 11-preview — prove in a scratch project before the plan commits |
| 3 | Whether failed passkey assertions touch `AccessFailedCount` in .NET 10's `SignInManager` (if so, the split table catches that churn too) |
| 4 | Protector/converter resolution under pooled contexts + model caching (mirror Identity's own service-resolution pattern; confirm no per-model service capture bug) |
| 5 | `google.rpc.ErrorInfo.Metadata` as the receipt channel — size/shape fit |
| 6 | Filtered-unique-index syntax per provider through EF migrations (SQL Server `HasFilter`; PG default NULLS DISTINCT confirmed) |
| 7 | Exact closed claims allowlist pinned against .NET 10 factory output |
| 8 | The key-seam `Destroyed(receipt)` vs `Missing` distinction — dedicated test |
| 9 | Backup-restore-cannot-resurrect — dedicated test |
| 10 | Live-session death: a session authenticated before shred is dead within one revalidation interval after it — dedicated test (§4.2's stamp rotation is the trigger under test) |
| 11 | The typed key exceptions (`KeyDestroyedException`/`KeyMissingException`) propagate from a converter throw during EF materialization to the repository fold undistorted — confirm EF neither swallows nor wraps them beyond unwrappable recognition |

## 9. Ship order (dependency sketch, not a plan)

Svartálfheim (structs + interface + attribute + analyzer) → Asgard (`ErrorCategory.Erased`, `Problem.Receipt`, `Abstractions.Keys`) → Midgard (edge mappings, dev key provider) → Urðarbrunnr (converter factory) → Himinbjörg (protectors, keyring, key table, schema changes, temporal split, claims factory) → disclosure surface. Each realm behind its own ship gate, per standing law.
