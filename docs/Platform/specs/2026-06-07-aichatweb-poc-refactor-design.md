# aichatweb POC Refactor — Anthropic Chat, Voyage Embeddings, Mongo Vector Store

**Status:** Approved 2026-06-07. Greenlit through implementation (spec → plan → subagent execution) by Buvy in the same session.

**Scope:** `poc/aichatweb/VoyageEmbeddings` only. This is POC-land — proving the stack before any verdict reaches the platform realms. Findings feed `docs/Platform/specs/2026-06-07-vector-embeddings-decision-inputs.md` (§5 "findings return here for a ruling") and, downstream, the AI spec.

---

## 1. Mission

The Microsoft `aichatweb` template was staged deliberately with three swappable seams (Ollama chat, Ollama embeddings, Qdrant vector store), all behind Microsoft.Extensions.AI / Microsoft.Extensions.VectorData abstractions. This POC executes the three swaps the platform actually cares about:

1. **Chat:** Ollama → **official Anthropic .NET SDK** via its Microsoft.Extensions.AI `IChatClient` adapter.
2. **Embeddings:** Ollama `all-minilm` → **Voyage AI** via a roll-our-own `IEmbeddingGenerator<string, Embedding<float>>` adapter built to the public-OSS bar.
3. **Vector store:** Qdrant → **MongoDB** via the Semantic Kernel MongoDB MEVD connector, backed locally by `mongodb/mongodb-atlas-local`.

What the POC must *prove* (not just make work):

- The `input_type` query/document asymmetry can be made unrepresentable-wrong behind M.E.AI (decision-inputs §5's design wrinkle).
- The MEVD abstraction actually delivers the store-reversibility it promises (Qdrant → Mongo with `DataIngestor`/`SemanticSearch` consumers unchanged).
- The Anthropic SDK's MEAI adapter carries the template's streaming + function-invocation chat path without custom plumbing.
- What the atlas-local "muck" actually costs (the image docket rejected it for the platform; the POC measures it).

## 2. Decisions Made (this session)

| # | Decision | Ruling |
|---|---|---|
| D1 | Local Mongo backend | **atlas-local container** (`mongodb/mongodb-atlas-local`, bundles `mongot`) orchestrated by Aspire. Clone-and-run preserved; no external accounts. |
| D2 | Voyage adapter scope | **Full public-OSS bar now, own classlib.** The POC adapter is the OSS seed, not a throwaway. |
| D3 | `input_type` asymmetry shape | **Constructor-pinned + keyed pair.** `InputType` is required at construction (no default); DI registers two keyed singletons (`"voyage-query"` / `"voyage-document"`). No per-call `input_type` override — `AdditionalProperties["input_type"]` throws. |
| D4 | Vector dimensions | **1024, passed explicitly** as `output_dimension` — never reliant on API defaults. Exercises the `Dimensions` → `output_dimension` pass-through. |
| D5 | Chat integration mechanism | **A1 — official Anthropic SDK MEAI adapter.** Hand-rolled `IChatClient` (A2) re-enters only if the adapter can't surface something needed; record in findings. |
| D6 | Refactor sequencing | **B1 — three verifiable stages**, app runs after each (§4). |
| D7 | Adapter name | **`Voyage.Extensions.AI`** — provider + what-it-extends, mirroring the M.E.AI adapter ecosystem convention. Namespace mirrors assembly. |
| D8 | Truncation default | **`Truncation = false`**, deliberately inverting Voyage's API default of `true`. Silent truncation is a silent fallback (§2.6 hard-fail law): an embedding of half a document claiming to represent the whole. Truncation is explicit opt-in; overlong input fails loudly. |
| D9 | Chat model | `claude-opus-4-8`, config-driven (appsettings/env), never hardcoded. |
| D10 | Secrets | Aspire secret parameters in the AppHost (`anthropic-api-key`, `voyage-api-key`) → web app config. User-secrets locally. Nothing in source. |

## 3. End-State Architecture

```
VoyageEmbeddings.AppHost
├─ "vectordb"  → mongodb/mongodb-atlas-local container (mongot bundled)
├─ "markitdown" → mcp/markitdown container                  [unchanged]
├─ parameters: anthropic-api-key, voyage-api-key (secret)
└─ VoyageEmbeddings.Web
     ├─ IChatClient            → Anthropic SDK MEAI adapter (claude-opus-4-8)
     ├─ IEmbeddingGenerator ×2 → Voyage.Extensions.AI (keyed: query / document)
     └─ VectorStoreCollection  → SK MongoDB MEVD connector

Voyage.Extensions.AI            (new classlib — the OSS seed)
Voyage.Extensions.AI.Tests      (xUnit v3 + Shouldly)
```

**Ollama exits entirely:** both `AddOllamaApiClient` registrations, `OllamaSharp` + `CommunityToolkit.Aspire.OllamaSharp` packages (Web), `CommunityToolkit.Aspire.Hosting.Ollama` (AppHost), the `AddOllama`/`AddModel` resources, and `OllamaResilienceHandlerExtensions.cs` (it existed for self-hosted-LLM slowness; Anthropic and Voyage ride the ServiceDefaults standard resilience).

**Qdrant exits at stage ③:** `Aspire.Qdrant.Client`, `Microsoft.SemanticKernel.Connectors.Qdrant` (Web), `Aspire.Hosting.Qdrant` (AppHost).

## 4. Sequencing — Three Verifiable Stages

Each stage leaves a running, chat-functional app. A failure isolates to one seam.

| Stage | Swap | What still runs from before | Verification |
|---|---|---|---|
| **①** | Chat → Anthropic SDK MEAI adapter | Ollama embeddings + Qdrant untouched | Chat streams, tools fire (`LoadDocuments`, `Search`), citations render |
| **②** | Embeddings → `Voyage.Extensions.AI` (keyed pair); dims 384 → 1024; `SemanticSearch` switches to explicit query embedding | Qdrant still the store (collection recreated — `IncrementalIngestion = false` already drops/recreates) | Ingestion completes against Voyage; search returns relevant chunks; adapter tests green |
| **③** | Qdrant → Mongo (atlas-local + SK MEVD connector) | Everything above | Same end-to-end behavior; `DataIngestor`/`SemanticSearch` consumer code unchanged (the reversibility finding) |

Stage ① temporarily runs Anthropic chat over Ollama embeddings — a deliberate hybrid; it costs nothing and keeps the feedback loop tight.

## 5. `Voyage.Extensions.AI` — Adapter Design

`VoyageEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>`, typed `HttpClient` via `IHttpClientFactory`. The gravity9 connector's flaws (decision-inputs §5) are the anti-checklist.

### 5.1 Options

| Option | Posture | Rationale |
|---|---|---|
| `Model` | **Required, no default** | Model version is index identity. An ambient default that drifts on package update is silent data corruption across every stored vector. |
| `InputType` | **Required, no default** | D3. Construction without declaring query/document does not happen. |
| `BaseUrl` | Default `https://api.voyageai.com/v1/`; configurable | The Atlas AI API (`https://ai.mongodb.com/v1/`) fronts the same wire shape — one option serves both (decision-inputs §5 prior-art discovery). |
| `OutputDimension` | Optional (omitted = model native); POC pins 1024 | D4. Matryoshka pass-through must be exercised. |
| `Truncation` | **Default `false`** (inverts Voyage's `true`) | D8. Silent truncation is a silent fallback. Opt-in only. |
| `OutputDtype` | float32 only in v1 | Offered dtypes = delivered dtypes — no lying options surface. Class shaped so `Embedding<sbyte>` / binary generators can be added without redesign (`Embedding<T>` exists; gravity9 hard-coded float and lied about the rest). |
| Retry/resilience | **Absent from options entirely** | Host owns resilience (standard handler via `ConfigureHttpClientDefaults`). Offering retry knobs the adapter doesn't implement was a gravity9 disqualifier. |

### 5.2 Per-call `EmbeddingGenerationOptions` — honor-or-throw

- `Dimensions` → `output_dimension`: **honored** (explicit per-call override of the pinned default).
- `ModelId` → `model`: **honored** (explicit is explicit; index-identity discipline is the caller's wiring concern).
- `AdditionalProperties` containing `input_type` or any unrecognized key: **throw**. Nothing is accepted-and-ignored.

### 5.3 Request chunking

Batch inputs to Voyage's per-request input-count cap across multiple sequential POSTs; aggregate results in input order. Client-side token-cap enforcement would require Voyage's own tokenizer — we do not approximate; an API token-limit rejection surfaces as a loud, actionable error carrying Voyage's error body. Findings record whether this bites and whether pulling the tokenizer in is worth it for the OSS release.

### 5.4 Usage and registration

- `total_tokens` from each response maps into `GeneratedEmbeddings.Usage` (summed across chunked requests).
- DI extension `AddVoyageEmbeddingGenerator(serviceKey, configureOptions)` produces keyed singletons; options validated at startup (missing key/model/input-type fails at boot, not first request).

## 6. Wiring Changes — `VoyageEmbeddings.Web`

### 6.1 Chat (stage ①)

```csharp
// Replaces both AddOllamaApiClient blocks
builder.Services.AddChatClient(/* Anthropic SDK client → MEAI adapter; model + key from config */)
	.UseFunctionInvocation()
	.UseOpenTelemetry(configure: c =>
		c.EnableSensitiveData = builder.Environment.IsDevelopment());
```

- `Chat.razor` untouched: `GetStreamingResponseAsync`, `AIFunctionFactory` tools, `ChatSuggestions` all ride `IChatClient`.
- Anthropic's API is stateless → `update.ConversationId` stays null → the template's `_statefulMessageCount` logic already falls back to resending full history. Zero edits.
- **Plan-time verification (no guessing):** exact adapter binding shape (`AsIChatClient` or equivalent) confirmed against the `anthropics/anthropic-sdk-csharp` repo before writing the call site.

### 6.2 Embeddings (stage ②)

```csharp
builder.Services
	.AddVoyageEmbeddingGenerator("voyage-document", o => { o.Model = "voyage-4"; o.InputType = InputType.Document; o.OutputDimension = 1024; })
	.AddVoyageEmbeddingGenerator("voyage-query",    o => { o.Model = "voyage-4"; o.InputType = InputType.Query;    o.OutputDimension = 1024; });
```

| Consumer | Instance | Why |
|---|---|---|
| Vector collection (auto-embed of `IngestedChunk.Vector` on upsert) | document | Stored corpus content |
| `SemanticSimilarityChunker` (`DataIngestor`) | document | Embeds candidate sentences for topic-boundary detection — corpus-side, throwaway vectors |
| `SemanticSearch` | query | Embeds the user's phrase **explicitly**, then `SearchAsync(ReadOnlyMemory<float>, …)` |

The `SemanticSearch` change is the one structural edit the asymmetry forces, and it is the POC's headline finding: MEVD's `SearchAsync(string, …)` overload cannot express `input_type`, so query embedding goes manual. Record in findings as input to the AI spec's retrieval surface.

- `IngestedChunk.VectorDimensions`: 384 → 1024 (constant comment updated — it currently cites all-minilm).
- Chunker tokenizer mismatch: `TiktokenTokenizer.CreateForModel("gpt-4o")` ≠ Voyage tokenization. It only gates chunk sizing, not correctness. Stays, with a comment; findings doc records it.

### 6.3 Vector store (stage ③)

- AppHost: `AddMongoDB("vectordb")` running `mongodb/mongodb-atlas-local`; data volume; persistent lifetime (mirrors the Qdrant resource it replaces).
- Web: Aspire `AddMongoDBClient("vectordb")` + SK MongoDB MEVD connector registrations replacing `AddQdrantVectorStore`/`AddQdrantCollection`.
- `DataIngestor` (`VectorStoreWriter` over `VectorStore`) and `SemanticSearch` (injected `VectorStoreCollection`) are MEVD-abstract — **they must not change**. Any forced change is itself a finding against the reversibility claim.

**Plan-time verifications (no guessing):**
1. Whether `Aspire.Hosting.MongoDB` tolerates the atlas-local image (health checks included), or whether a custom container resource is needed.
2. Mongo MEVD connector key-type support — `IngestedChunk.Key` is `Guid` (Qdrant-friendly); stage ③ may force `Guid` → `string`. The chunk model owns that flip if so.
3. Exact SK MongoDB connector package + registration surface current as of implementation date (pre-GA ecosystem; dev-dependency version posture applies — ride the current train, pin exact).

## 7. Error Handling

- **Startup:** options validation at build — missing API keys, model, or `InputType` fail at boot (error-upstream preference #3, application startup).
- **Adapter:** honor-or-throw per §5.2; no internal retries; Voyage error responses surface with HTTP status + Voyage's error body, never swallowed.
- **Index readiness:** atlas-local builds search indexes asynchronously — ingest-then-immediately-search can return empty *without erroring*. A silent wrong answer. The POC must confront it head-on: findings record what the MEVD Mongo connector does about readiness and what the right loud-or-wait behavior is. Direct intel for AI.

## 8. Testing

- **`Voyage.Extensions.AI.Tests`** (xUnit v3 on Microsoft.Testing.Platform, Shouldly): request shaping on the wire (`input_type`, `output_dimension`, `truncation`), batching boundaries, honor-or-throw paths, error surfacing, usage mapping — against a stub `HttpMessageHandler` (we own the wrapper; no mocking-what-we-don't-own).
- **Live smoke test:** gated on `VOYAGE_API_KEY` presence (skipped otherwise): embed one query/document pair, assert dimensions and non-degenerate similarity ordering.
- **Per-stage manual verification** per §4's table.
- **No mocked-DB tests for store behavior** — stage ③ verification runs against the real atlas-local container.

## 9. Findings Docket (write `poc/aichatweb/FINDINGS.md` at the end)

1. Anthropic SDK MEAI adapter — fidelity of streaming + function invocation; anything A2 would have been needed for; thinking/effort surface through MEAI.
2. `input_type` asymmetry — did constructor-pinned + keyed pair hold? The manual-query-embed seam in `SemanticSearch`.
3. MEVD reversibility — what actually changed Qdrant → Mongo (target: registrations + chunk-model only).
4. atlas-local muck tax — image size, startup time, health-check behavior, index-readiness handling.
5. Voyage API behaviors — batching limits hit or not, truncation failures, usage accuracy, Matryoshka/dtype observations.
6. Chunker-tokenizer mismatch — observed effect on chunk quality, if any.
7. Adapter OSS-readiness gaps — anything between POC state and publishable.

Findings feed back into `2026-06-07-vector-embeddings-decision-inputs.md` per its §5.

## 10. Out of Scope

- Quasi-PII / embedding-eligibility / erasure (parked by human decision — own session; decision-inputs §4).
- Atlas (cloud) connectivity and the `ai.mongodb.com` endpoint — adapter supports the base URL; the POC doesn't exercise it.
- Quantized dtypes (int8/binary) — structured-for, not delivered.
- Any platform-realm code. Verdicts come later; this is the courtroom exhibit.
