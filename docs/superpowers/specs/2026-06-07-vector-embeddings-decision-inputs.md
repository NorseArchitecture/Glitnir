# Vector Store & Embeddings — Decision Inputs (NOT a spec)

**Status:** Decision inputs captured 2026-06-07. This is **not** a design spec and decides nothing. It exists so the future AI spec session (and the `EncryptedString` spec, §4 below) inherits the whole picture instead of rediscovering it. Platform-first sequencing applies — the AI spec convenes after the platform realms are codified.

**Supersession warning:** CLAUDE.md §4 ("pgvector for embeddings feeding AI") and two parentheticals in the hosting spec's container-image docket (§13 #17: "pgvector owns vectors (AI)") record the *current* decision. This document records why that decision is **under review**, not yet reversed. Do not treat either as settled until the AI spec rules.

---

## 1. The framing that must survive: vectors are AI's one thing

Per "smart about one thing": no bounded context embeds text or queries a vector index directly. Contexts ask AI (gRPC read path — legal under §3 integration rules) or consume AI's published signals. Workers doing IChatClient-heavy work get retrieval context from AI like everyone else; only AI's own worker touches the vector store.

Consequence: "pgvector vs Mongo vs Qdrant" is an **AI-internal implementation choice**, made once, behind an abstraction — not a per-context debate. Microsoft.Extensions.VectorData (companion to the IChatClient/M.E.AI stack already planned) is the abstraction that makes the choice reversible; it has connectors for all three candidates. Dragon-sizing satisfied: pick the smallest store that serves known demand, verify the swap path exists, document the triggers.

## 2. The wall constraint that reopened this

Embeddings are **derived data** — rebuildable from source text + model version, written after the fact, read by serving paths. That is operational-read-store shape, not system-of-record shape. The `.Server` tier cannot reach Postgres by construction, so:

- **pgvector is viable only while AI's vector workload is asynchronous** (consume events → compute similarity/recommendations in AI's worker → publish signals). No wall violation, no new container, smallest dragon. This is the current CLAUDE.md §4 posture.
- **The moment a synchronous request path needs vector reads** (RAG for customer-service chat, semantic policy/claims document search in the portal, any `.Server`-resident IChatClient flow), the store must live on the read side of the wall. pgvector is structurally disqualified for that traffic — not by preference, by §2.4.

**The rationale is threat-model, not just data-shape** *(Buvy, 2026-06-07)*: the RDBMS is the source of truth and should have as few hands in it as possible — **including a compromised HTTP server acting badly**. Vector serving from the read store keeps the web tier's blast radius contained even under compromise; granting the web tier a Postgres path "just for vectors" would breach the containment that §2.4 exists to provide. This framing outranks any convenience argument for pgvector-on-the-serving-path.

**Sync serving is confirmed real** *(2026-06-07)*: web-consumable chat bot clients are planned, probably Blazor Server only (InteractiveServer circuits on `Norse.Hosting.Web.Server`, per the UI Composition spec's render modes). Mechanical note: circuit code executes **server-side**, so chat components reach AI over plain gRPC (or in-process) and stream tokens to the browser over the circuit — no gRPC-capable chat component needed.

**The gRPC chat client's trigger is MAUI, specifically** *(Buvy, 2026-06-07)*: MAUI has no Blazor Server mechanism to fall back on, so a MAUI rollout *forces* a client-side streaming chat path (gRPC over `{Company}.{Context}.Contracts`-style surface, consumed from Components). The follow-on payoff once that client exists: the web chat component can flip from InteractiveServer to **InteractiveAuto** — end-user devices take on the rendering workload and the server is freed to just stream content. Sequence matters: the gRPC client is never built *for* the web; it's built for MAUI and the Auto-mode offload comes free afterward. Until MAUI is real, InteractiveServer-only stands and no gRPC chat surface exists.

## 3. Store candidates and their trigger conditions

| Candidate | Wall-compliant for sync reads? | Cost | Decides in its favor |
|---|---|---|---|
| **pgvector** (inside AI's Postgres) | No — worker-side only | Zero new infrastructure | AI spec scopes V1 vector work to async/advisory only |
| **MongoDB vector search** | Yes — Mongo *is* the read store | Self-hosted = `mongot` sidecar (separate Lucene-based process; the `mongodb-community-search` / atlas-local muck rejected in the image docket). Atlas = mature managed Vector Search, no muck | Production Mongo hosting lands on **Atlas** — *signaled nearly certain by Buvy 2026-06-07*, making this the **presumptive winner**. The MongoDB–Voyage AI acquisition (early 2025) makes the path compound: Atlas is growing native Voyage auto-embedding, so the integrated story improves over time. "We already run Mongo" is NOT by itself the argument — container-count parity with Qdrant is roughly even once `mongot` is counted; Atlas is what dissolves that cost. Local-dev consequence if confirmed: the image docket's Mongo pin gains an atlas-local/`mongot` companion decision |
| **Qdrant** | Yes | One purpose-built Rust container (official image, `Aspire.Hosting.Qdrant` exists, M.E.VectorData connector) | Demoted to **fallback** given the Atlas signal: re-enters if Atlas falls through, or if vector workload outgrows what the read store should carry (quantization tuning, payload-filtered point-deletes at scale, index-rebuild isolation) |

Anti-condition worth recording: choosing Mongo vector search **solely** to avoid one container repeats the mistake the image docket exists to prevent — the apparatus cost is real, just relocated.

## 4. The quasi-PII question (feeds the `EncryptedString` spec)

**PARKED by human decision 2026-06-07** — Buvy: this is "a whole debate in and of its own" and gets its own session; do not fold it into other discussions or attempt to resolve it in passing. The questions below are the docket for that session, nothing more.

Embeddings of PII are **quasi-PII**: inversion attacks recover meaningful content from vectors. The platform's crypto-shredding answer does not transfer — encrypted vectors cannot be searched, so any customer's vectors sit plaintext in whatever index holds them, **outside the per-customer DEK regime**. The `EncryptedString` spec (the surviving §7 #11 work item) must therefore answer a sibling question:

1. **What text is permitted to be embedded at all?** (An allowlist of embedding-eligible content classes, declared per ingress — never ad hoc.)
2. **How does right-to-erasure reach the vector index?** Usual answer: stamp `customer_id` on every vector record's payload and hard-delete by filter on erasure (Qdrant does payload-filtered point-deletes well; Mongo deletes documents natively; pgvector deletes rows). Crypto-shredding the DEK does NOT erase the vectors — erasure must be a second, explicit step in the same workflow.
3. **Third-party data flow:** every embed call ships text to Voyage's API — a data-processing relationship to paper with legal. Escape hatch if legal balks: `voyage-4-nano` is open-weight (Apache 2.0, Hugging Face), self-hostable inside the stamp; quality step-down vs `voyage-4`, but "the text never left" is sometimes the requirement.

## 5. Embeddings model defaults (Anthropic shop, verified against Anthropic's live docs 2026-06-07)

Anthropic ships no first-party embeddings model and officially points at **Voyage AI** (owned by MongoDB since early 2025 — commercially adjacent to the store question above, technically independent: the Voyage API is standalone, also on AWS Marketplace).

| Slot | Model | Why |
|---|---|---|
| Default | `voyage-4` | Quality/cost balance; 32K context; Matryoshka dims (256/512/1024/2048); int8/binary quantization |
| Quality ceiling | `voyage-4-large` | Same dims/context, best retrieval |
| Cheap/latency paths | `voyage-4-lite` | Same dims/context |
| Self-host / data-residency | `voyage-4-nano` | Open-weight, Apache 2.0 (see §4 #3) |
| Domain candidates | `voyage-finance-2`, `voyage-law-2` | Closest domain fits (insurance text; claims/legal docs) — evaluate against `voyage-4` on real corpus before adopting; previous-generation models |
| Document images | `voyage-multimodal-3.5` | Interleaved text/image/video — ACORD forms, PDFs-as-screenshots, slide-shaped submissions |

Usage rules that survive any model choice:
- **`input_type` asymmetry is mandatory** — `query` vs `document` at embed time, never omitted.
- **Model version is index identity.** Vectors from different models or versions are not comparable. Stamp `(model, version, dimensions)` on every vector record; a model change is a full reindex, never a mix. Hard rule for the AI spec, ReferenceData-flavored.
- No first-party Voyage .NET SDK as of 2026-06-07 — a thin `IEmbeddingGenerator<string, Embedding<float>>` adapter over their REST API keeps the model swappable behind M.E.AI.

**Decided & in flight** *(Buvy, 2026-06-07)*:
- **IChatClient abstraction is confirmed** — the Anthropic C# SDK is already loaded and wired (external to Glitnir). At AI-spec time this graduates to a CLAUDE.md §4 stack-table row (M.E.AI `IChatClient` + Anthropic SDK), ratified rather than implied.
- **Voyage `IEmbeddingGenerator` adapter: Buvy is prototyping externally**; findings return here for a ruling. Decided 2026-06-07: roll our own (not fork the prior art below) and **publish to GitHub when the time comes** — so the design bar is public-OSS quality (API shape, naming, license, README), not internal-only. The one real design wrinkle the PoC should answer: `IEmbeddingGenerator<string, Embedding<float>>.GenerateAsync` has no native query/document distinction, but Voyage's `input_type` asymmetry is mandatory (§5 usage rules). Candidate shapes to evaluate: two keyed registrations (query-generator / document-generator), an `EmbeddingGenerationOptions.AdditionalProperties["input_type"]` convention, or a thin domain wrapper that makes the asymmetry unrepresentable-wrong. Also worth exercising: `EmbeddingGenerationOptions.Dimensions` → Matryoshka `output_dimension`, and `output_dtype` quantization pass-through.

**Prior art reviewed 2026-06-07 — `gravity9-tech/mongodb-voyageai-net-connector` (Apache 2.0): reference, not a dependency.**
- **Key discovery:** its default base URL is `https://ai.mongodb.com/v1/` — **MongoDB Atlas fronts the Voyage models through its own AI API**, same wire shape as `https://api.voyageai.com/v1/`. If Atlas confirms (§3), store + embeddings consolidate to one vendor/credential/bill, and the §4 third-party-data-flow question simplifies to a single processor (MongoDB) either way. The adapter should take base URL as configuration so both endpoints work.
- **Worth keeping:** typed-`HttpClient`-via-`IHttpClientFactory` registration (matches hosting conventions); its options class as a checklist of the Voyage embed surface (`input_type`, `truncation`, `output_dimension`, `output_dtype`, `encoding_format`).
- **Disqualifying flaws (the adapter's quality bar, by counterexample):** `EmbeddingGenerationOptions` accepted but wholly ignored (per-call `Dimensions` silently discarded — silent fallback); `InputType` registration-time-only **plus** `TryAddSingleton`, so query+document generators cannot coexist in one process; options promise int8/binary dtypes and retry settings the implementation doesn't deliver (converter throws on non-float; no retry exists — dead/lying configuration); no request chunking against Voyage's per-request input-count/token caps; hard-coded `Embedding<float>` locks out quantization (`Embedding<T>` exists).
- **Bar for our adapter:** honor per-call options or fail loudly; dual query/document registration must be possible; chunk to API limits; offer only deliverable dtypes; base-URL-configurable (Atlas AI API vs direct Voyage).

## 6. What the AI spec must rule on

1. ~~Is synchronous vector serving in V1 scope at all?~~ **Signaled yes, 2026-06-07** — web chat bot clients over Blazor Server are planned (§2). The spec confirms and scopes; it doesn't reopen.
2. ~~Production Mongo hosting — Atlas or self-hosted?~~ **Signaled Atlas, nearly certain, 2026-06-07.** The spec ratifies; if it ratifies, Mongo Atlas Vector Search is the presumptive store (§3) and the local-dev atlas-local/`mongot` companion pin gets decided in the same pass.
3. Store choice behind M.E.VectorData, with documented re-entry triggers for the paths not taken (Qdrant = fallback; pgvector = AI-internal async only, if at all).
4. Embedding-eligible content classes + erasure workflow — **parked for its own dedicated session** (§4); the AI spec must reference it as a dependency, not resolve it inline.
5. Whether CLAUDE.md §4's "pgvector for embeddings feeding AI" line stands, narrows ("pgvector for AI's async V1"), or is superseded — and the matching cleanup of the two §13 #17 parentheticals in the hosting spec.
