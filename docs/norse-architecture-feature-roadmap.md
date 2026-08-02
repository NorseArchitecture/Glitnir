# Norse Architecture — Cross-Cutting Feature Roadmap

**Status:** Draft for Glitnir ratification
**Scope:** The business-facing cross-cutting features every enterprise ends up needing — delivered as opt-in, provider-neutral realms following the Urdarbrunnr pattern (neutral abstraction in Asgard, provider adapters in Midgard, composition via Ginnungagap-distributed config, every registration proven by a wired-not-just-designed test).

**Sequencing principle:** Doctrine before code. Plumbing before features. The feature that justifies the stack (event-driven messaging) ships first among features. Everything that touches money ships after the machinery it depends on exists.

---

## Phase 0 — Doctrine Sprint (Glitnir only, no code)

Laws that constrain every subsequent phase's data model. Cheap to write now, ruinous to retrofit.

| Law | Substance |
|---|---|
| **The Three Telemetries** | Observability (server events/metrics, OTel, Huginn/Muninn) ≠ Analytics (browser usage stats, UTM, marketer questions) ≠ Audit (actor/action/entity/before/after, compliance artifact). Different consumers, different retention, different legal character. Smuggling one into another's pipeline is illegal. |
| **The Messaging Law** | *No realm may ever couple to the concept of email or SMS.* Notifications exist only as published events. The messaging realm subscribes, marshals payload fields, and carries the message out the door. Sending mail via a command is forbidden platform-wide. |
| **The Money Law** | Money is minor units (integer) + ISO 4217 currency. Floating point is forbidden. Minor-unit exponents flow from the ISO 4217 dataset Mimisbrunnr already owns — the payments realm takes its currency law from reference data, deterministic GUIDs included. |
| **The PAN Law** | Primary account numbers never touch Norse infrastructure. Hosted fields / tokenization only. Consumers stay at SAQ-A. "The Norse Architecture does not expand your PCI scope" must remain a true sentence. |
| **Consent & Erasure Doctrine (Syn)** | Consent is an append-only ledger: who, what, when, which policy version. Erasure strategy is **crypto-shredding** (verdict rendered — see Rendered Verdicts §1): sever the person from the records via per-subject key destruction; tombstoning rejected as incompatible with temporal tables. DSAR shape (export / erase / rectify) defined as a cross-realm contract; per-field data classification (Classes A–E) and realm retention registers per §1. |
| **The Webhook Law** | All inbound webhooks: signature verification + idempotency key, non-negotiable. All outbound webhooks: signed, versioned, retried with backoff, dead-lettered. |
| **Tax Doctrine (scope-only)** | Tax is calculated by a provider behind a neutral seam, never hand-rolled. Payment model must carry line items from day one so tax has something to attach to later. |

**Exit criteria:** Each law lands in Glitnir as settled doctrine with a Forseti verdict.

---

## Phase 1 — The Spine (Yggdrasil plumbing)

The machinery every feature phase leans on. Nothing here is user-visible; all of it is load-bearing.

1. **Idempotency Realm (Yggdrasil).** Web server fingerprints the request post-authn/authz (Heimdall supplies the "who"). That fingerprint becomes the outbox basis in the Yggdrasil worker. This is the duplication firewall for the entire platform.
2. **Transactional Outbox.** Send-after-commit, exactly-once-ish delivery, dead-letter path. Consumed by messaging (Phase 2) and outbound webhooks (Phase 3) — build once, prove twice.
3. **Feature Flags / Entitlements.** The generalized opt-in mechanism. "Turn payment methods on and off" *is* this system — build it once here rather than letting Draupnir grow a private toggle config. Doubles as the progressive-rollout story for strangler-fig consulting engagements.
4. **Audit Log.** Actor, action, entity, before/after, timestamp, immutable. Identity flows from Heimdall. Banks ask for this in the first meeting; it should predate every feature that mutates business state.
5. **Syn (Consent Ledger + DSAR endpoints).** Append-only consent record and the export/erase/rectify machinery, implementing the Phase 0 doctrine. Gates analytics later; informs suppression semantics in messaging.
6. **Key Seam (envelope-encryption infrastructure).** The spine's key-management abstraction: wrapping-key custody, per-subject payload keys, service-level lookup keyrings, retention-epoch keys with scheduled destruction. Declared interfaces with off-the-shelf adapters (Azure Key Vault, AWS KMS, GCP Cloud KMS) or bring-your-own implementation. See Key Architecture addendum.
7. **Runtime Config & Secrets Seam.** Provider-neutral runtime configuration with encrypted-value support: Azure App Configuration + Key Vault references; AWS Systems Manager Parameter Store (SecureString) + Secrets Manager; GCP Secret Manager + Cloud KMS (no true config-store equivalent — secrets yes, config store BYO). Thin seam over `IConfiguration`: provider selection, encrypted-value convention, reload semantics. **Boundary law:** Ginnungagap distributes settled design-time config; this seam serves runtime config and secrets. Different offices.
8. **Scheduled Ceremonies (the tick contract).** Schedules are operations, not code. An external scheduler fires an authenticated tick (client-credentials grant via Heimdall) at a thin endpoint, which enqueues a command through the normal fingerprint→outbox pipeline — scheduled work rides the same spine as user work, no second path. The worker stays dumb; editing the schedule is an operator console/IaC action, never a deploy.
   - **Contract:** anything that can POST with a client-credentials token on a schedule satisfies it. Cloud adapters: AWS EventBridge Scheduler, GCP Cloud Scheduler, Azure Logic Apps recurrence (Azure Scheduler is retired). Sovereign/on-prem: Kubernetes CronJob or plain cron + curl — zero cloud dependency by construction.
   - **Idempotency is load-bearing:** ticks are at-least-once (EventBridge explicitly so). Payload carries ceremony name + logical scheduled fire time; the idempotency key derives from that pair, so duplicate deliveries collapse in the outbox.
   - **Watchdog law:** *the schedule is external, but the expectation of the schedule is internal.* Each ceremony declares its expected cadence; runs are already audit/Syn-recorded; a small in-house watchdog (the only in-house piece — a dead man's switch, not a scheduler) alerts when last-run exceeds tolerance. "Epoch destruction quietly stopped eight months ago" is a compliance incident this law makes impossible to miss.
   - **Local dev:** no escape hatch needed — obtain a client-credentials JWT and fire the tick endpoint from Vafþrúðnir (Bruno). A manual fire is just a scheduler with an irregular cron expression, and it exercises the *real* production path (same auth, endpoint, command, outbox) rather than a parallel dev-only code path that could drift. Duplicate-tick collapse is demonstrable by hand the same way.
   - **In-house schedulers (Quartz.NET, Hangfire) rejected:** they make the worker smart, add schedule state as a persistence concern, and hide cadence from the operator. Standing ceremonies as of this writing: epoch-key destruction, the blob reaper.
   - **Naming candidate:** *Gullinkambi* — the golden-combed rooster who crows at the appointed hour to wake the warriors; announces the time, does none of the work.

**Class A/C residence (resolved):** the "party vault" is not a new realm — Class A identity and Class C behavioral data live in **Himinbjörg** alongside ASP.NET Identity, fronted by a system-role-restricted (plus subject-self) gRPC lookup-by-id service. The watchman's hall houses the identities; every other realm holds only opaque deterministic GUIDs.

**Exit criteria:** Fingerprint → outbox pipeline demonstrated end-to-end with a synthetic event; flag registration, audit write, and consent write each covered by a test that fails when the registration is removed; one ceremony proven round-trip (tick → command → outbox → execution → audit record) with a duplicate tick collapsing to a single run, and the watchdog alerting on a deliberately silenced ceremony.

---

## Phase 2 — Transactional Messaging (the raison d'être)

The stage-demo feature: *publish an event, and the messaging realm carries it out the door.*

- Messaging service subscribes to platform events, extracts required fields from the payload, dispatches email/SMS. No caller ever knows email exists.
- Delivery rides the Phase 1 outbox keyed on the request fingerprint — duplicate-proof by construction.
- **Provider adapters** behind the neutral seam: Postmark (philosophically aligned — transactional-only) and SES (cheap, enterprise-palatable) for email; one SMS adapter (Twilio-class). Swapping providers changes adapter registration and nothing outside the bounded context — that's the on-stage sentence.
- **Suppression list**: bounces and complaints recorded and honored. Deliverability hygiene and, in some regimes, a legal requirement even for transactional mail.
- **ASP.NET Identity close-out**: 2FA token delivery and email verification implemented as published events consumed by this realm — proving the pattern on the platform's own front door.

**Naming candidate:** *Gná* — Frigg's messenger, who rides Hófvarpnir through air and over sea to deliver her words. A courier, not an author: she carries the message; she does not compose the policy. Precisely the boundary the realm enforces.

**Exit criteria:** Registration → verification email → 2FA SMS demo running end-to-end through event publish, outbox, and adapter; provider swap demonstrated with zero changes outside the messaging bounded context.

---

## Phase 3 — Gjallarhorn (Outbound Webhooks)

The "future integrations" answer made concrete. Deliberately adjacent to Phase 2 because it reuses the same outbox and event spine.

- Signed (HMAC, rotatable secrets), versioned event delivery to subscriber endpoints.
- Retry with exponential backoff; dead-letter after exhaustion.
- **Management UI**: subscription registration, per-delivery log with request/response capture, signing-secret management, and a manual **retry button** for deliveries whose endpoint failed to return success.
- Event catalog published from the same contracts the internal event spine uses — one vocabulary, inside and out.

**Exit criteria:** Subscriber endpoint deliberately failing 3× then succeeding on manual retry, fully visible in the UI; signature verification example code shipped for consumers.

---

## Phase 4 — Edda (Document Service)

Sequenced before payments because Draupnir immediately needs receipt/invoice rendering.

- **Ingest half:** upload via signed URLs to a blob-storage abstraction (provider seam: Azure Blob / S3 / MinIO for the sovereign story), virus scanning on ingest, metadata in Urdarbrunnr, content-addressable storage where practical.
- **Render half:** data + template → PDF. Template versioning treated like migrations — a rendered document must be reproducible from its inputs and template version.
- Retention/erasure semantics wired to Syn doctrine (documents are where PII goes to hide).

**Name (settled):** *Edda* — the written artifacts through which the entire mythology survived. Every other realm in the pantheon is only known because the document layer preserved the record; the name makes that dependency literal. Carries both halves: the Eddas are simultaneously archive (custody) and rendered composition (Snorri's Prose Edda is structured data retold as a finished artifact). Bragi remains with the designers in Blazing Story, where a performance space for components is proper skald work. *Sága* is formally reserved — see Naming Reservations below.

**Exit criteria:** Round trip demo — upload a document, retrieve via signed URL; render a PDF from structured data; both audited.

---

## Phase 5 — Draupnir (Payments)

The ring that drips eight new rings every ninth night. Orchestration-shaped abstraction (intents, tokenization, captured/refunded/disputed lifecycle, webhook ingestion), not per-method — methods are adapters and flags.

Staged rollout, each stage an entitlement toggle:

1. **Cards** — hosted fields/tokenization per the PAN Law; intent lifecycle; refunds; dispute webhook ingestion (reusing Phase 3's signature-verification muscle and Phase 1 idempotency for provider redeliveries).
2. **Digital wallets / mobile pay** — Apple Pay, Google Pay. Mostly presentation-layer additions atop the card rails.
3. **BNPL** — Klarna / Afterpay / Affirm adapters; redirect-flow handling generalized.
4. **Pay by Bank / A2A** — ACH, RTP, open-banking initiation; asynchronous settlement states become first-class in the intent model (this is where the lifecycle model earns its keep).
5. **Crypto (last mile)** — processor-mediated only (Coinbase Commerce class), ubiquitous coins only. Custody is a different business with a different regulator; Norse never holds keys.

Receipts render through Edda on `PaymentCaptured` — an event, consumed by document + messaging realms, exactly as doctrine demands. Every payment carries line items (per Phase 0 tax doctrine) even while tax itself remains unimplemented.

**Exit criteria per stage:** end-to-end sandbox capture → webhook → outbox → receipt email; method disabled via entitlement flag with a test proving the endpoint refuses when off.

---

## Phase 6 — Tax & Invoicing (Draupnir satellites)

- **Laws first** (extending Phase 0): tax calculation is provider-delegated; jurisdiction determination inputs defined; invoice numbering is sequential-per-legal-entity and gap-auditable.
- **Neutral seam + adapters:** Stripe Tax and Avalara as the two known-provider adapters.
- **Invoicing:** line-item model already in place from Phase 5; B2B invoice rendering through Edda; delivery through the messaging realm.

**Exit criteria:** Sandbox transaction with tax calculated by either adapter interchangeably; invoice PDF rendered, numbered, audited, delivered.

---

## Phase 7 — Analytics

Deliberately last: most decoupled, and legally gated on Syn.

- **Cookieless, consent-clean:** server-side emission, no client storage, no fingerprinting — the cleanest PECR posture is no banner at all.
- **UTM capture:** first-touch UTM parameters captured at the edge and attributed server-side; attribution stored as data, not as a cookie.
- **Provider seam:** self-hostable defaults (Umami / Plausible CE / PostHog self-hosted) aligned with the sovereign-infrastructure story; SaaS adapters optional.
- **Hard boundary enforcement:** banned-symbol analyzer rules preventing analytics SDK types from appearing outside the analytics realm — the Three Telemetries law made compile-time, in the house style.

**Naming candidate:** *Frigg* — she who knows all fates but speaks of them to no one. A privacy-preserving analytics realm could not ask for a better patron.

**Exit criteria:** Feature-usage dashboard populated from server-side events with zero cookies set; UTM attribution visible on a demo conversion; analytics blocked for a user with consent withheld, proven by test.

---

## Dependency Graph (summary)

```
Phase 0 (Doctrine)
   └─ Phase 1 (Spine: idempotency, outbox, flags, audit, Syn, key seam, config seam, ceremonies)
        ├─ Phase 2 (Gná — Messaging)  ──┐
        ├─ Phase 3 (Gjallarhorn)        ├─ shared outbox + event spine
        ├─ Phase 4 (Edda — Documents)   │
        │     └─ Phase 5 (Draupnir) ────┘  (receipts need Edda; webhooks need P3 muscle)
        │            └─ Phase 6 (Tax & Invoicing)
        └─ Phase 7 (Frigg — Analytics)     (gated on Syn; otherwise independent)
```

## Naming Reservations (record in Glitnir)

- **Sága — RESERVED.** Held for a future process-orchestration realm (long-running processes / process managers), should one be summoned. The industry "saga" pattern (NServiceBus/MassTransit long-running business processes, compensating transactions) and the goddess who recounts an ongoing story across many sittings reinforce each other perfectly there — and would collide confusingly anywhere else, particularly given Draupnir's A2A settlement states and Phase 6 return/refund flows, where the saga *pattern* will naturally enter design conversations. Nobody spends this name on anything lesser.
- **Bragi — TAKEN.** Blazing Story (designer component gallery). Not available.

## Rendered Verdicts (formerly Open Questions)

### 1. Erasure strategy — VERDICT: crypto-shredding; tombstoning rejected

Tombstoning is a contradiction against system-versioned temporal tables: history faithfully preserves every value a row ever held, and SQL Server forbids transacting on the history table while versioning is on (`SYSTEM_VERSIONING = OFF` surgery is a schema-locking, audit-destroying ceremony, forbidden in production). Urdarbrunnr's three-provider temporal support would multiply that ceremony per provider. Crypto-shredding is the only strategy where temporal history *and backups* are erased by construction: destroy the per-subject key and every ciphertext copy — current row, history rows, every backup ever taken — becomes noise simultaneously, with zero history writes on any provider.

**Scope law — sever the person from the records, don't erase the records.** GDPR Art. 17(3) exempts processing necessary for legal-obligation compliance and legal-claims defense: invoices (tax retention), policies/claims (regulatory retention, limitation periods), payments (AML/audit) remain immutable *with citable basis*. Data classification, ratified per field, never per table:

| Class | Contents | Treatment |
|---|---|---|
| **A — Direct identity** | Name, email, phone, addresses, national IDs | Lives in **Himinbjörg** (with ASP.NET Identity), referenced everywhere else by opaque deterministic GUID via system-role-restricted gRPC lookup; payload encrypted with per-subject DEK; lookups via blind index under service keyring; erasure = payload key destruction + lookup-hash removal |
| **B — Statutorily retained w/ embedded identity** | Invoice billing name/address as-issued, policy holder snapshots, claim records | **Consumer-composed** from Svartalfheim PII structs into their own records; `[RetentionPolicy]` attribute **required by analyzer — no declaration, no build**; DEKs wrapped under retention-epoch keys (per realm/jurisdiction/expiry bucket); epoch key alive for the statutory period, scheduled destruction shreds the whole cohort across temporal history and backups in one key-store operation. Platform provides guidance, recommendations, citations; the composition is theirs |
| **C — Behavioral/derived** | Frigg events, preferences, usage | Rides **Himinbjörg** with Class A — per-subject, non-temporal, no retention basis; same key, same shred, free |
| **D — Audit + Syn ledger** | Immutable entries | Entries immutable; PII *within* entries is Class A ciphertext — shredded subject's trail survives as "opaque party X did Y at Z." Syn's erasure receipt (subject, timestamp, policy version, key-destruction proof) is the one permanent record, kept deliberately |
| **E — Edda documents** | Rendered PDFs, uploads | Every document **declares** a retention policy; **default = shredded on subject's request** (per-subject key); statutory-policy documents join a retention epoch and shred on epoch-key destruction. Post-shred, blobs are **reaped** (deleted) by the lazy garbage collector — ciphertext-only documents have no value; the key destruction was the erasure, deletion is housekeeping |

**Costs acknowledged:** (a) key management is real infrastructure — per-subject DEKs wrapped by KEK in a proper key store, whose backup lifecycle must guarantee a DB restore cannot resurrect the dead (this property gets a test, not an assumption); (b) shredded fields need **blind indexes** (HMAC of normalized value under its own destroyable key) for login-by-email / lookup-by-phone — a house-rules chapter of its own.

**Mechanism is event-shaped:** `ErasureRequested` publishes → each realm shreds its Class A/C holdings and reports → Syn folds reports, destroys key, appends ledger receipt. Naming candidate for the key-destruction mechanism: **Surtr's fire** — the flame that burns so completely nothing returns.

**Retention register (unchanged requirement):** each realm keeps a register in Glitnir citing legal basis per retained field — field, class, basis, jurisdiction, clock start, clock duration. The register doubles as the DSAR refusal script. Adjacent fields with no basis (shipping notes, contact email on an order) are Class A and shred normally.

#### Key Architecture (Verdict §1 addendum)

**The envelope law.** A key stored in a temporal row cannot be the key that shreds it — history keeps copies. What lives in the row is the **wrapped DEK**; **wrapping keys never touch temporal storage**. Destroying a wrapping key in the key seam darkens every row its DEKs ever protected — current, history, backup — in one operation, identically across all three Urdarbrunnr providers.

**Two key planes (by necessity, not preference).**
- *Payload plane:* per-subject DEKs (Class A/C) and per-record DEKs wrapped under retention-epoch keys (Class B/E). Shredded on erasure or epoch expiry respectively.
- *Lookup plane:* service-level rotatable keyrings producing blind indexes (HMAC of normalized value). **Cannot be per-subject** — you must find the user before you know whose key to use. On erasure: payload key destroyed, current-row lookup hashes nulled; temporal history retains old HMACs — non-reversible under a secret key, pseudonymous residue, honestly documented in doctrine.

**ASP.NET Identity integration (Himinbjörg).** Identity ships the seam already: `[ProtectedPersonalData]`, `IPersonalDataProtector`, `ILookupProtector`, `ILookupProtectorKeyRing`. Payload properties encrypt via protectors backed by the key seam; `NormalizedEmail`/`NormalizedUserName` become blind indexes so `FindByEmailAsync` and the whole toolkit work unchanged. 2FA/verification: phone and email are decrypted **at send time** through the vault, handed to Gná's event payload transiently, stored nowhere downstream.

**Operational laws.**
- **Shred, then reap.** Key destruction is the erasure — instantaneous, Syn-receipted, effective against backups and replicas that deletion can never reach. Blob deletion is demoted to garbage collection: a lazy background reaper walks shredded-key manifests and reclaims Edda storage on its own schedule, with no compliance clock attached. Temporal database fields are never reaped — their ciphertext residue is the design: audit structure intact, personal content permanently dark. *Blobs get reaped because they can be; temporal fields keep ciphertext because they must; both are equally erased, because erasure happened at the key.*
- **Key hierarchy (cost containment).** One provider-native KEK per environment (Key Vault / KMS / Cloud KMS); epoch keys are platform-generated, KEK-wrapped, stored as rows in the platform key store; row DEKs wrap under epoch keys. KMS cost is O(1) regardless of epoch granularity; epoch destruction = delete a key-store row + Syn ledger entry.
- **Epoch granularity: monthly by default,** configurable per (realm, jurisdiction). Daily is available once the hierarchy makes it free, but a 7-year retention at daily granularity without the hierarchy is ~2,555 live provider keys per bucket dimension — a CFO conversation, not a compliance one.
- **The key store is never temporal**, and it is the one store whose backup retention is deliberately short: backup window ≤ declared shred-latency SLA, or a restore resurrects destroyed keys. This is the restore-can't-resurrect test's second half.
- Unwrapped DEKs may be cached in memory only, with bounded TTL; **cache TTL is the declared shred-latency SLA** — a number stated in doctrine, not discovered in an incident.
- Epoch-key destruction is a scheduled, audited ceremony recorded in the Syn ledger like any erasure.

**Provenance (for the deck).** Cryptographic erasure is a NIST SP 800-88-recognized sanitization technique — the mechanism behind instant secure wipe in full-disk encryption. The Norse Architecture's claim is altitude, not invention: a NIST-recognized erasure primitive applied at the field level, enforced at compile time.

**Compile-time enforcement (the crown jewel).** Svartalfheim PII structs (`EmailAddress`, `PhoneNumber`, `PersonalName`, `PostalAddress`, …) make personal data visible to the type system. An analyzer refuses to compile any entity composing them without an explicit `[RetentionPolicy(...)]` declaration. *You cannot accidentally store PII in the Norse Architecture; you can only do it on purpose, with a citable basis, at build time.* Compile-time enforcement over runtime convention, applied to law itself.

**Provider seam.** Keys: Azure Key Vault, AWS KMS, GCP Cloud KMS, or bring-your-own behind the declared interfaces. The seam is custody + wrap/unwrap + scheduled destruction; algorithm choices are ours, not the provider's.

### 2. Feature flags — VERDICT: spine-owned seam, realm-owned semantics

The spine provides the seam and the config binding; **it does not know what any flag means.** Realms declare flags locally, the spine evaluates them, and every flag-gated registration carries a test proving the gate (the Draupnir "endpoint refuses when off" test generalized into settled law). Naming candidate on the bench: **Verdandi** — the norn of *that which is presently becoming*; a flag system is the mechanism deciding what the platform presently is versus what it merely could be.

### 3. SMS adapter — VERDICT: one blessed adapter, seam validated against five providers' docs

Seam shape survives contact with Twilio, Vonage, Sinch, Bird, and AWS End User Messaging: all agree on E.164 destinations; all deliver status **asynchronously via callback**, never in the send response; all differ on sender identity (long code / short code / alphanumeric, per-country legality). Therefore the seam: accept E.164 recipient + rendered body + idempotency key, return `Outcome` with provider message ID; delivery receipts arrive as inbound webhooks (Phase 3/5 signature+idempotency muscle) and republish as platform events. **Ours, never the provider's:** templating (provider-hosted templates re-couple) and opt-out/suppression state (STOP handling varies by provider and regulation; the suppression ledger is ours, providers merely inform it). Deliberately not modeled: segmentation/encoding (GSM-7 vs. UCS-2) — a provider concern.

### 4. Frigg for analytics — VERDICT: ratified

Analytics is where conservative buyers expect to catch the platform being creepy; spending a senior name signals a first-class architectural commitment. She who knows all fates and speaks of them to no one is the selling point, stated in one clause.
