# Kvasir — Content Ingestion & Serving Realm

**Status:** Brainstorm draft — input to Claude Code `/brainstorming` session, laws proposed for Glitnir ratification
**Realm name:** Kvasir (the wisest of beings, whose essence was brewed into the mead of poetry that others drink — content authored elsewhere, transformed, and served to inspire)
**Namespace law:** The Norse name dissipates at file paths and namespaces. Consumers see `Asgard.Content.*`, `Midgard.Content.*`, `Urdarbrunnr`-resident storage. Never `Midgard.Kvasir`.

---

## The Grievance

Never again a WordPress installation on the TLD of a company. The traditional CMS fuses three things that must never be fused: the authoring surface, the serving runtime, and the attack surface — all parked on the organization's most valuable DNS real estate. When it breaks, the site is down. When it's exploited, the brand is compromised. When the vendor's roadmap shifts, the business is hostage.

Kvasir dissolves the trap: content creators author wherever they prefer, in whichever headless CMS suits them. When they publish, the platform grabs it, maps it, and serves it. The creators never see the machinery and never need to — their LinkedIn post is there. Design-time is entirely between the creator and their CMS; the platform has zero design-time opinions, integrations, or support obligations.

---

## Proposed Laws (Glitnir ratification required)

| Law | Substance |
|---|---|
| **The CMS Law** | No realm may ever couple to a CMS vendor. Content exists to the platform only as the canonical store. Vendor SDKs, vendor payload shapes, and vendor rich-text formats never cross the Asgard seam. |
| **The Doorbell Law** | An inbound CMS webhook is a *notification*, never a delivery. Verify signature, record idempotency key (per the Webhook Law), enqueue a sync command, and pull the full content from the vendor's delivery API. A periodic reconciliation sweep is mandatory — webhooks are lossy, and a doorbell you didn't hear still means a package on the porch. |
| **The Custody Law** | On sync, the platform takes the *whole* package into its own store: content and assets both. The CMS, its delivery API, and its CDN are authoring-time dependencies — allowed to exist, allowed to fail. A CMS outage pauses publishing and degrades preview; the serving path does not notice. |
| **The Runtime Dependency Law** | The serving path may depend on exactly five things: the message queue, the database, distributed configuration, the key vault, and cloud blob storage (S3/Azure Blob) — the last admitted as *upstream-only* (see Single Origin Law). Every other capability is implemented in-realm or precomputed into the canonical store at sync time. (Generalizes platform-wide — same instinct that keeps vendor exporters out of containers in Midgard.ServiceDefaults.) |
| **The Single Origin Law** | Every byte the browser loads comes from the platform's origin. Blob storage is a private upstream the platform connects to and streams through — never a public endpoint, never a pre-signed URL handed to a browser, never a vendor CDN hostname in markup. The CSP stays lean and mean: effectively `'self'`. Bucket topology is an implementation detail invisible to the wire. |
| **The Redirect Law** | A slug change mints a 301 redirect *in the same sync transaction* that writes the new slug. Redirects are first-class data in the canonical store. Backlink equity is never silently destroyed. |
| **The Editorial Boundary Law** | Vendor content is untrusted external input. Ingest validation is `Result<T>` at the edge: missing image alt text, broken heading hierarchy, malformed blocks, unsanitized SVG → validation failure. Failures dead-letter *always* (nothing silently vanishes; the DLQ is the operator's safety net); where the vendor supports status write-back, the adapter phones home on the way down as a courtesy. Never silent acceptance, never silent repair. |
| **The Principal Law** *(platform-tier — belongs in the roadmap's Phase 0 doctrine, discovered here)* | Every request in the landscape executes under a ClaimsPrincipal — no exceptions, no anonymous code path, only an Anonymous *principal*. Signature verification and IP whitelisting are authentication *schemes*, never controller code: an `AuthenticationHandler` per mechanism (HMAC signature, IP whitelist, JWT bearer, anonymous cookie), each terminating in a first-citizen principal with `client_id` and `amr` (how convincingly) stamped in. Authz is written once against claims; audit always ties to a correct actor — Identity user, anonymous id, or integration client that cannot do OAuth. Replay defense (timestamp tolerance) lives inside the scheme; idempotency remains application-side per the Webhook Law. Signatures verify against *raw buffered bytes*, never a re-serialized model. |

---

## Tenancy Ruling (settled)

**Single tenant per deployment.** One Norse deployment serves one site for one business. The platform does not care what you build on top of it — it insists only that the software embodying your business be well-made. Multi-brand operators run multiple deployments. No site discriminator pollutes the content spine; no host-header routing; one sitemap.

---

## The Four Seams (what a vendor adapter *is*)

Every Midgard adapter implements exactly four seams against the Asgard contract:

1. **Webhook receiver.** Event classification and normalization *only* — published / unpublished / deleted / slug-changed — into the neutral sync command, executing under an already-authenticated integration ClaimsPrincipal (Principal Law). Signature verification is **not** the receiver's job: the adapter contributes a **signature profile** (header names, encoding, algorithm, timestamp-tolerance semantics, key-vault secret reference, and the **event-identity selector** — which header or payload field is stable across retry attempts, the name input for the deterministic idempotency GUID) to the platform's webhook authentication scheme in Himinbjörg territory. Vendors that cannot sign fall to the whitelist scheme — egress ranges as a Ginnungagap-distributed, versioned dataset, with `amr` recording the weaker tier. Idempotency remains here, application-side, per the Webhook Law.

2. **Content puller** (`IContentProvider` shape). Fetch full entity by id. Fetch draft by id (preview path, on demand). Enumerate changes since cursor (reconciliation path), where the vendor supports it.

3. **Rich-text transcoder.** Vendor serialization (Portable Text, Contentful Rich Text, Storyblok blocks, Lexical JSON, …) into the neutral block model. This is the bulk of each adapter's real code, and it is where the Editorial Boundary Law is enforced.

4. **Asset resolver.** Discover every asset referenced by the content, mirror it into the platform asset store, hand it to the image pipeline for variant precomputation, and rewrite all references in the transcoded blocks to platform-owned URLs. No vendor CDN URL ever reaches the canonical store.

---

## Neutral Content Model (Asgard)

The platform knows what a *page* is. It never knows what a *product* is. Core types are the web primitives every engagement needs:

- **Page** — routable, carrying the SEO envelope: title, meta description, canonical URL, robots directives, OpenGraph/Twitter card fields, JSON-LD type.
- **Article** — page specialization with author, published/updated timestamps, taxonomy tags.
- **Navigation** — ordered, nestable link structure.
- **Redirect** — source path, target path, status (301/410), provenance (slug-change vs. manual).
- **SiteSettings** — site title, base URL, default SEO fallbacks, social handles.
- **Block model** — the extensible spine: paragraph, heading (level-checked), image (alt mandatory, srcset precomputed), code, quote, embed, list, table, and a consumer-extension point where engagements register custom block types *and their Blazor renderers*. Unknown blocks fail ingest loudly per the Editorial Boundary Law; they do not render as mystery divs.

Entity patterns per house-rules.md: sealed records, colocated static `Configure`, `required` scalars, deterministic identity where derivable.

---

## Sync Choreography

```
webhook in
  → authenticate (platform webhook authn scheme: signature or whitelist per vendor profile → integration ClaimsPrincipal, Principal Law)
  → classify + normalize event (adapter, seam 1)
  → mint idempotency/correlation id: UUIDv5(client-id namespace, stable vendor event id) — framework-minted, no way around it
  → idempotency ledger insert, ON CONFLICT DO NOTHING (Webhook Law, application-side)
  → enqueue sync command (queue = whitelisted dependency)
worker dequeues
  → pull full content from delivery API (seam 2)
  → transcode rich text → neutral blocks (seam 3)
  → editorial validation (Result<T>; failure → dead-letter + vendor status write-back where supported, stop)
  → mirror assets + precompute variants (seam 4 + image pipeline)
  → write canonical store: ON CONFLICT ... DO UPDATE ... WHERE IS DISTINCT FROM
  → mint 301 if slug changed (same transaction — Redirect Law)
  → maintain tsvector search column (same write)
  → publish cache-invalidation event
reconciliation sweep (scheduled)
  → enumerate changes since cursor where supported; full-scan diff where not
  → same worker path, same idempotency
```

The worker owns the projection and writes it while the content is hot in its hands — the established JSONB view-model pattern. Serving never computes; serving reads.

---

## Asset Store (settled)

**Ruling:** Cloud blob storage (S3/Azure Blob), admitted to the runtime whitelist as an upstream-only dependency under the Single Origin Law.

**Placement:** The blob seam is platform infrastructure, not Kvasir property — neutral abstraction in Asgard, S3 and Azure Blob adapters in Midgard as peer providers (the Urdarbrunnr posture). Kvasir is the first consumer; Edda (document streaming, third-party upload intake) is the known second. Kvasir owns its *usage* of the seam — content-addressed asset layout, variant storage — never the seam itself.

- **Private bucket, zero public access.** No public endpoints, no pre-signed browser URLs. The platform authenticates via managed identity / credentials from the key vault (both already whitelisted) and streams objects through its own asset endpoint.
- **Content-addressed keys.** Assets and variants are stored under content-hash-derived keys, so platform asset URLs are immutable by construction: `Cache-Control: public, max-age=31536000, immutable`, ETag for free, cache-busting never needed, and the streaming proxy is warm-path cheap because browsers and any intermediary cache do the repeat work.
- **Streaming, not buffering.** The asset endpoint streams blob → response; range requests pass through for large media.
- **One origin on the wire.** `img-src 'self'` (and friends) holds; adding a vendor never widens the CSP.

## Image Pipeline

- **Library: NetVips** (libvips binding). Fastest option in .NET, memory-frugal, LGPL, vendored native code shipped in-process — no external throat.
- **ImageSharp is banned** — Six Labors' split license makes commercial use above a revenue threshold a paid surprise, which is exactly the wrong discovery inside a platform sold to banks.
- **Variants are precomputed at sync time, never transformed on demand.** On-demand transformation is how an external image service sneaks back into the serving path. The worker mints the variant set (width ladder × AVIF/WebP/fallback) during ingest; the block model carries the finished srcset; serving is a static read.
- Variant policy changes are a batch re-run over the asset store, not an architecture event.
- **Variant ladder (settled, delegated call):** widths 320 / 640 / 960 / 1280 / 1920, capped at source width — never upscale. Formats: AVIF (quality ≈50) + WebP (≈75) + original-format fallback (JPEG ≈80, PNG lossless). Platform default; SiteSettings override.
- **Animated GIFs pass through untouched** — no variant ladder, no re-encode.
- **SVGs are executable documents.** Ingest sanitizes them — scripts, event handlers, and foreignObject stripped — before they enter custody. Editorial Boundary Law and the lean CSP working the same shift.

---

## Serving Path

- **Blazor static SSR** for all public content pages. Crawlers receive fully-formed HTML; no circuit, no WASM payload on the content surface.
- **Output caching** keyed to canonical store versions, invalidated by the sync event. Server does the work once, not N times per load.
- **SEO surfaces generated from the canonical store, never the CMS:** sitemap.xml, JSON-LD, canonical tags, OG/Twitter cards, robots.
- **Redirect middleware** reads the redirect table; slug history serves 301s forever.
- **Asset endpoint** streams from private blob storage under immutable content-addressed URLs (see Asset Store) — the only place the fifth whitelisted dependency touches the serving path.
- **Unpublish/delete → 410 Gone.** Delete does not exist as a concept; the Urdarbrunnr temporal store *is* the tombstone. Serving reads the current row — if the period end is not the 9999-12-31 sentinel, the content has been binned and the response is 410. Composes with the Redirect Law: renamed-then-deleted walks old slug → 301 → final slug → 410. Crawlers de-index promptly.
- **Preview:** signed, expiring URLs that run the *same* render pipeline against draft content pulled on demand through seam 2. Same code path, different content source — preview never lies. Drafts are not mirrored into custody; they are authoring-time material fetched transiently.

---

## Site Search

Postgres full-text search over the canonical store. The worker maintains the `tsvector` column as part of the canonical write (see choreography). Ranking via `ts_rank`, headline extraction via `ts_headline`, language configuration from SiteSettings. No external search service — the Runtime Dependency Law already ruled. It will not out-Algolia Algolia at typo-tolerant instant search; it will be good, cheap, and ours.

---

## Adapter Matrix & Capability Flags

| Vendor | Hosting | Delivery API | Rich text | Signed webhooks | Draft API | Delta cursor |
|---|---|---|---|---|---|---|
| Sanity | SaaS | GROQ | Portable Text | HMAC | Yes | Yes |
| Contentful | SaaS | REST + GraphQL | Proprietary Rich Text | Request verification | Yes (preview API) | Yes (sync API) |
| Storyblok | SaaS | REST + GraphQL | Proprietary blocks | HMAC | Yes | Partial |
| Prismic | SaaS | REST + GraphQL | StructuredText | **Weak** (secret in payload) | Yes | Partial |
| Strapi | Self-hosted | REST + GraphQL | Blocks JSON / Markdown | Self-configured | Yes | No (poll) |
| Payload | Self-hosted | REST + GraphQL | Lexical JSON | Self-configured | Yes | No (poll) |
| Directus | Self-hosted | REST + GraphQL | As modeled | Self-configured | Yes | No (poll) |

The Asgard contract must carry these as **capability flags** — `SignsWebhooks`, `SupportsDraftFetch`, `SupportsDeltaCursor`, `SupportsStatusWriteBack` — with defined degraded behavior rather than pretending all vendors are equal. Verify each row against current vendor documentation at implementation time; this matrix is a design-time sketch, not settled fact.

**Vendor Admission Criterion (settled):** webhook notification is *mandatory* for adapter admission. A vendor that cannot ring the doorbell forces full-graph diffing as the primary sync mechanism — maintenance overhead the platform will not carry. Poll-only vendors are eliminated from contention. If a customer requires one, they build the adapter themselves and submit it, proven with wired-not-just-designed tests. (Every vendor in the current matrix passes — the self-hosted trio fire webhooks; they merely make signing the operator's configuration problem.)

**Reconciliation cadence (settled, delegated call):** hourly cursor pull where the vendor exposes a true delta API; nightly `updatedAt`-filtered sweep otherwise (the self-hosted trio all support updated-since queries — a poor man's cursor, acceptable as the *backup* mechanism since webhooks carry the primary load). Both cadences configurable.

**Proving pair (ratified): Sanity + Payload.** Sanity is the most idiosyncratic SaaS (GROQ, Portable Text, its own asset CDN semantics); Payload is the self-hosted generalist where signing and hosting are the operator's problem. A seam proven against both maximal extremes admits Contentful and Storyblok as routine adapters. Strapi's authoring ergonomics judged clunky in operator trial — Payload takes the self-hosted seat.

---

## Sequencing

1. **Doctrine** — ratify the seven laws and the settled rulings in Glitnir.
2. **Asgard contracts** — neutral content model, block model, `IContentProvider`, capability flags, sync command shapes.
3. **Canonical store** — Urdarbrunnr schema: pages, blocks (JSONB), assets, redirects, search column. Migrations via the established pipeline.
4. **Sync worker skeleton** — Doorbell choreography end-to-end with a fake in-repo provider; wired-not-just-designed tests for every registration.
5. **First adapter** (proving vendor #1) — all four seams.
6. **Serving path** — static SSR, output cache + invalidation, SEO surfaces, redirect middleware.
7. **Preview** — signed URLs, draft pull.
8. **Second adapter** (proving vendor #2) — the neutrality proof. Contract amendments discovered here are cheap now, ruinous later.
9. **Image variant pipeline** — NetVips, variant ladder, batch re-run tooling.
10. **Search** — tsvector maintenance, query surface, ranking.

---

## Settled Rulings (record in Glitnir alongside the laws)

- **Tenancy:** single tenant per deployment.
- **Asset store:** cloud blob storage, upstream-only, streamed through the platform origin (Single Origin Law). Blob seam is platform infrastructure — Asgard abstraction, Midgard S3/Azure adapters; Kvasir first consumer, Edda second.
- **Image library:** NetVips/libvips; ImageSharp banned on licensing grounds.
- **Variant ladder:** 320/640/960/1280/1920, source-width capped; AVIF + WebP + original fallback; SiteSettings override. GIF passthrough; SVG sanitized on ingest.
- **Search:** Postgres FTS; no external search service.
- **Proving pair:** Sanity + Payload.
- **Unpublish/delete:** 410 Gone via temporal store — period end ≠ 9999-12-31 sentinel means binned. No tombstone entity, no hard removal.
- **Ingest rejection:** dead-letter always; vendor status write-back where `SupportsStatusWriteBack`, as courtesy on the way down.
- **Vendor admission:** webhook notification mandatory; poll-only vendors eliminated. Customer-built adapters accepted by submission with wired-not-just-designed proof.
- **Reconciliation cadence:** hourly where delta cursor exists; nightly `updatedAt` sweep otherwise; configurable.
- **Webhook authentication:** signature verification and IP whitelisting are Himinbjörg authentication schemes producing integration ClaimsPrincipals — never controller code (Principal Law). Adapters contribute signature profiles; the mechanism is built once, platform-tier, and serves Kvasir's CMS vendors and Draupnir's payment webhooks (Stripe) through the same door. Adds Himinbjörg scheme work to Kvasir's dependency list alongside the blob seam.
- **Idempotency = correlation, deterministic:** UUIDv5(authenticated client-id namespace, stable vendor event id via the profile's event-identity selector), minted by the framework post-authentication before adapter code runs — structurally unavoidable. One GUID serves as idempotency ledger PK (`ON CONFLICT DO NOTHING`), correlation id through queue/traces/audit/provenance, and is *recomputable* from the vendor dashboard event id for debugging — derived, never assigned. Svartalfheim owns the mechanism, as it does for Mimisbrunnr. Reconciliation-discovered changes name their GUID from entity id + version, so the sweep and the doorbell collapse to one identity scheme.

## Open Questions for Forseti

None outstanding. All rulings rendered; doctrine is ready for Glitnir and the brief is ready for the `/brainstorming` gate.
