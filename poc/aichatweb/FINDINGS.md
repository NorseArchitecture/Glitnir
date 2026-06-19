# aichatweb POC — Findings

**Status:** Living document, opened 2026-06-07 mid-execution (at Buvy's request, while Stage ② ingestion was running). Sections marked ⏳ await live verification of the remaining stages. Feeds back into `docs/Platform/specs/2026-06-07-vector-embeddings-decision-inputs.md` §5 and, downstream, the Mimir spec.

Plan: `docs/Platform/plans/2026-06-07-aichatweb-poc-refactor.md`. Spec: `docs/Platform/specs/2026-06-07-aichatweb-poc-refactor-design.md`.

---

## 0. Headline — HTTP client resilience is the POC's richest lesson

A throttled real-world API (Voyage's free tier: **3 requests/minute, 10K tokens/minute**) turned out to be the most instructive part of the whole exercise. It forced the resilience contour into the open, and the design churned **three times** before it was right. Documenting it verbatim because it's directly transferable to the platform (Mimir's embedding path, any rate-limited partner integration) and because the failure modes were non-obvious each time.

### 0.1 The three-iteration journey

**Iteration 1 — RateLimiter as an adapter option (REJECTED).**
First cut put a `RateLimiter?` on `VoyageEmbeddingGeneratorOptions`, acquired per-batch inside `VoyageEmbeddingGenerator`. It worked and was unit-tested. But it violated the platform's own law: the adapter is supposed to be **resilience-free** — the host owns retry/throttle (the same reason the adapter has no retry logic). Throttling is an HTTP-pipeline concern, not an embedding-generator concern. Buvy caught it: *"I would hope we could create a polly/retry policy for VoyageTrial and wire it up in one spot in servicedefaults."* Correct. Ripped back out (adapter + tests + package reference all reverted clean).

**Iteration 2 — Polly handler in ServiceDefaults (RIGHT SHAPE, WRONG PIPELINE).**
Moved to `AddSharedRateLimitResilience(requestsPerMinute, params clientNames)` in `VoyageEmbeddings.ServiceDefaults/Extensions.cs`: a single shared `SlidingWindowRateLimiter` paced across the named voyage clients, plus `AddRetry` with `ShouldRetryAfterHeader`. One spot, host-owned, adapter clean again. This is the architecturally correct home.

But it **layered on top of** Aspire's standard resilience handler (applied to every client via `ConfigureHttpClientDefaults` in `AddServiceDefaults`). That standard handler has a **30-second `TotalRequestTimeout`**. At 3 RPM, requests legitimately sit in the rate-limiter queue for 20–40s waiting for a permit — and the outer 30s timeout guillotined them mid-wait:

```
Polly[0] Resilience event ... EventName: 'OnTimeout', Source: '-standard//Standard-TotalRequestTimeout'
Polly.Timeout.TimeoutRejectedException: The operation didn't complete within the allowed timeout of '00:00:30'.
 ---> System.Threading.Tasks.TaskCanceledException
   at Polly.RateLimiting.RateLimiterResilienceStrategy.ExecuteCore(...)   ← cancelled while queued for a permit
```

**The irony, recorded for posterity:** this is the *exact* failure mode the Microsoft template's `OllamaResilienceHandlerExtensions.cs` existed to prevent (it extended the Ollama client timeout to 3 minutes for slow self-hosted inference). We **deleted that file in Stage ②** as Ollama-specific cruft — then recreated its soul in ServiceDefaults two hours later. **Slow self-hosted LLM and aggressively-throttled SaaS are the same resilience problem wearing different hats.** The lesson: a queue-based rate limiter makes a fast API *look* slow to every outer timeout. Any timeout above the limiter must be sized for `queue-wait + request`, not just `request`.

**Iteration 3 — purpose-built pipeline, standard one removed (CORRECT).**
Final shape in `AddSharedRateLimitResilience`: `RemoveAllResilienceHandlers()` on the voyage-named clients (only those — everything else keeps Aspire defaults), then a hand-built pipeline in deliberate order:

```
AddTimeout(10 min)          ← outermost total ceiling, sized for queue waits not requests
  AddRetry(5×, exp backoff, ShouldRetryAfterHeader=true)
    AddRateLimiter(shared 3/min)   ← INSIDE retry: each attempt acquires its own permit
      AddTimeout(60s)        ← innermost, the actual wire call
```

**Why the limiter is inside retry, not outside:** if the limiter wrapped the retry loop, one acquired permit would cover all 5 retry attempts of a single logical request — meaning a request that 429s and retries 4 times would consume 1 permit but make 5 wire calls, busting the 3 RPM ceiling and guaranteeing more 429s. Limiter-inside-retry means **one permit = one wire request, always.** This ordering is the load-bearing insight.

### 0.2 Observed behavior (live, 2026-06-07)

With iteration 3 deployed, during the PDF ingestion run: **13+ HTTP 429 warnings, zero errors.** Every 429 retried gracefully on the Retry-After cadence and the pipeline kept going for the full duration of a throttled ingest. The throttle prevented most 429s (proactive pacing); the retry absorbed the few that slipped through the sliding-window edges (reactive recovery). Belt and suspenders, both pulling weight. Verified working end to end before the posture changed (§0.5).

### 0.2b Iteration 4 — paid tier, reactive flip (CURRENT)

Once a payment method unlocked Voyage's standard rate limits, the proactive sliding-window limiter became unnecessary friction — it was pacing every request at the free-tier 3 RPM even though the ceiling had lifted. Buvy's call: *"send until it hits the first 429 then start the wait... take the foot off the brakes now that we know it works."*

That is **reactive throttling**: no proactive limiter, fire at full speed, and let retry-with-Retry-After absorb the occasional 429 — send until the server pushes back, wait exactly its dictated Retry-After, resume. The proven retry machinery (§0.1 iteration 3) stays; only the proactive limiter is removed. The ServiceDefaults method became `AddRetryAfterResilience(params clientNames)` (the `requestsPerMinute` parameter and the shared limiter are gone). `MaxBatchSize` also rose 8 → 128: the free-tier 10K-TPM window forced tiny batches; on standard tier, batching generously is the bigger ingestion speedup (16× fewer requests = 16× fewer 429 opportunities).

**The proactive pattern is not lost** — its full design lives in §0.1/§0.3 here, and the method's XML remarks point back to it for the next partner that forces proactive pacing. Code reflects current truth (reactive); FINDINGS preserves the whole journey. This proactive↔reactive switch — same retry core, limiter added or removed by tier — is itself the transferable insight: **the resilience posture is a per-partner, per-tier dial, not a fixed choice.**

### 0.3 Transferable rules for the platform (Mimir / any throttled partner)

1. **Resilience lives on the HTTP pipeline, not in the typed client.** The adapter stays resilience-ignorant; a ServiceDefaults extension owns throttle + retry. Confirmed correct after iteration 1's misstep.
2. **A shared limiter must span all clients hitting one account.** Provider rate limits are per-account; N per-client limiters = N× the effective rate = guaranteed throttling. One `SlidingWindowRateLimiter` instance, captured in the registration closure, passed to every named client in the call.
3. **Rate limiter goes INSIDE retry.** One permit per wire attempt (§0.1 iteration 3).
4. **Any timeout outside a queue-based limiter must budget for queue wait.** The standard 30s `TotalRequestTimeout` is wrong for throttled clients; size the outer ceiling to `max-queue-wait + request-time`. When in doubt, replace the standard pipeline for those clients rather than stacking on top of it.
5. **`ShouldRetryAfterHeader = true`** (it's the default on `HttpRetryStrategyOptions`, but set it explicitly as documentation) — honor the server's Retry-After rather than guessing backoff.

### 0.4 Voyage free tier, for the record

3 RPM / 10K TPM is effectively unusable for real ingestion — a 2-document corpus takes minutes; a real one would take geological time. Per Voyage's own 429 body, **adding a payment method does NOT forfeit the free token grant** (200M tokens for the voyage-3 series still apply); it only unlocks standard rate limits "after several minutes." So the card costs nothing until the grant is exhausted. **Done 2026-06-07** — card added, posture flipped to reactive (§0.2b). The throttle existing at all is what made this POC teach the resilience lesson, so the free tier earned its keep regardless: a generous paid tier from the start would have hidden the entire contour.

---

## 1. Anthropic SDK Microsoft.Extensions.AI adapter (Stage ①)

- **Package:** `Anthropic` v12.27.0 (official SDK, beta line, v10+). NOT the community `tryAGI.Anthropic`.
- **IChatClient binding:** `anthropicClient.AsIChatClient("claude-opus-4-8")` — extension method `Microsoft.Extensions.AI.AnthropicClientExtensions.AsIChatClient(IAnthropicClient, string, int?)`, bundled in the `Anthropic` package. `using Anthropic;` for client construction, `using Microsoft.Extensions.AI;` for the extension.
- **Function invocation + streaming:** `.AsBuilder().UseFunctionInvocation()` (or the `AddChatClient(...).UseFunctionInvocation()` hosting form) carried the template's `AIFunctionFactory`-created Search tool and streaming chat with **zero edits to `Chat.razor`**. The MEAI abstraction held — the template's chat component never knew the provider changed.
- **Statelessness:** Anthropic's API is stateless, so `update.ConversationId` stays null and the template's `_statefulMessageCount` logic naturally resends full history each turn. No code change needed; the already-present code path just took the null branch.
- **Key validation gap (recorded for OSS / platform):** `AnthropicClient` does NOT validate the API key at construction. A bad/missing key surfaces as a 401 at first request, not at boot. Acceptable here (the AppHost always injects the key), but a standalone consumer misconfiguring it gets a runtime failure, not a startup failure — counts against the platform's fail-at-startup preference. The Voyage adapter, by contrast, validates required options in its constructor (boot-time). Asymmetry worth noting if the chat client ever gets a wrapper.
- A1 vs A2 (SDK adapter vs hand-rolled IChatClient): **A1 was sufficient.** No reason to hand-roll surfaced. Thinking/effort knobs were not exercised in this POC (the template doesn't expose them) — if the platform needs adaptive-thinking config through MEAI, re-evaluate whether the adapter surfaces it before assuming A2.

---

## 2. input_type asymmetry + the DI shape that proved it (Stage ②)

- **Constructor-pinned + keyed pair held.** `VoyageInputType` is a required, no-default constructor value; query and document generators are separate registrations. The asymmetry is unrepresentable-wrong: you cannot construct a generator without declaring which side it serves.
- **The headline seam — MEVD can't express input_type.** `Microsoft.Extensions.VectorData`'s `SearchAsync(string, ...)` overload auto-embeds the query, but offers no way to pass `input_type: "query"`. So `SemanticSearch` embeds the query **explicitly** via the query-side generator and calls the **vector** overload `SearchAsync(ReadOnlyMemory<float>, ...)`. This is the one structural consumer change the asymmetry forces, and it's the proof the POC existed to produce. **Direct input for the Mimir spec's retrieval surface:** any MEVD-mediated RAG path needs manual query embedding when the embedding provider has query/document asymmetry (Voyage, and others).
- **The non-generic `IEmbeddingGenerator` trap (live failure, fixed).** MEVD's `CollectionModelBuilder.Build(...)` resolves the **non-generic** `IEmbeddingGenerator` from DI to support string-vector auto-embed on upsert. Registering only the generic `IEmbeddingGenerator<string, Embedding<float>>` (keyed or aliased) is invisible to it →

  ```
  InvalidOperationException: Vector property 'Vector' has type 'string' which isn't supported
  by your provider, and no embedding generator is configured.
  ```

  Fix: alias **both** interface shapes (`IEmbeddingGenerator<string, Embedding<float>>` AND `IEmbeddingGenerator`) to the same instance. Recorded as a platform gotcha — anyone wiring MEVD auto-embed behind a custom generator hits this.
- **Boot-gate blind spot (process lesson).** This failure fired on the Blazor **circuit** (component injection constructing the collection), NOT on a bare HTTP GET of the page. My automated boot gate (GET `/` → 200) passed while the app was actually broken. **A boot gate must exercise the DI graph deeply enough to construct the failing service** — a page-returns-200 check is insufficient for circuit-injected dependencies. Buvy caught the real failure by running interactively.
- **Default-alias = document side.** The non-keyed registration resolves to the document generator (corpus-side consumers: MEVD auto-embed, the semantic chunker). Query is opt-in by key. Rationale: the dangerous silent-wrong default would be embedding corpus content with query semantics; making document the ambient default means the only explicit opt-in is the rarer, safer query path. (Superseded in the worker split — see §8 — where each tier has exactly one generator and the ambiguity disappears entirely.)

---

## 3. MEVD reversibility (Qdrant → Mongo)

**The abstraction held — the swap was ~9 files, mostly registrations.** Qdrant → Mongo touched: AppHost store resource (1), both `Program.cs` client+store registrations (2), the chunk model attributes (1), `SemanticSearch`'s typed-collection injection (1), and four csproj package swaps. `DataIngestor` — the actual write logic over `VectorStoreWriter` — needed **zero** changes. That's the reversibility the decision-inputs doc bet on (Microsoft.Extensions.VectorData as the swap-insulating layer): the business logic is store-agnostic; only wiring + the record's storage annotations move.

**What leaked through the abstraction (the real cost):**
- **Storage-name attributes are provider-specific.** Qdrant used `[VectorStoreData(StorageName=…)]` + `[JsonPropertyName]`; Mongo uses `[BsonElement(…)]`. The record model carries provider-coupled annotations — MEVD abstracts the *operations*, not the *storage mapping*.
- **The key type is NOT a free choice — and the docs lied about it.** Stage ③ initially changed `Guid Key` → `string Key` on the strength of the MS Learn doc stating the Mongo connector "supports string keys only." **The running store proved that false:** ingestion wrote 20 docs with `_id : Binary` — the SK Mongo connector + DataIngestion `VectorStoreWriter` generate **Guid** keys and persist them as BSON Binary (UUID). A `string` Key *ingests* without error but *throws on read*: `FormatException: Cannot deserialize a 'String' from BsonType 'Binary'`. Reverting to `Guid Key` (the evidence-backed type) fixes it; the existing Binary `_id`s deserialize straight back to Guid. **Net: the Guid→string→Guid round-trip was an unforced error driven by trusting stale documentation over the live system.** (See §Mongo-key lesson below.)

### §Mongo-key — the likely real root cause (Buvy, 2026-06-07) + the clean path for the platform
**The MongoDB C# driver maps a property named `Id` (or `[BsonId]`) to `_id`, and if the model supplies one, the driver uses it instead of generating its own.** Our key property was named `Key` — so the driver didn't recognize it as the document id, MEVD's `[VectorStoreKey]` mapped `Key → _id` on its own terms, and the Guid value landed serialized as BSON **Binary** (the driver's default Guid representation). That Binary/`string` collision is what cascaded into the whole Guid→string→Guid + explicit-`GetCollection` detour above.

**Candidate simplification (for the platform Mongo-persistence design, likely the port too):** name the key property `Id` (and/or `[BsonId]`), so the driver's native convention owns `_id` cleanly — probably letting the simple `AddMongoCollection<TRecord>` helper work as documented and dropping the explicit `GetCollection<Guid,_>` shim. **Not chased in the POC** — the `GetCollection<Guid, IngestedChunk>` solution is proven working end to end, and re-litigating the key shape risks re-breaking a hard-won green stack for elegance the POC doesn't need. Banked here as the first thing to try when Midgard's Mongo read-store models are designed: **lead with an `Id`-named key and let the driver convention do the work**, rather than fighting `[VectorStoreKey]` + a non-`Id` name. The deeper platform lesson: MEVD's storage mapping and the underlying driver's own conventions are *two* mapping layers that must agree — name the key the way the driver expects and they stop fighting.

### §Mongo-key — the registration sequel
Reverting the model to `Guid` surfaced a second layer: `AddMongoCollection<TRecord>` is **single-arity** (no `<TKey, TRecord>` overload — verified by reflecting the connector assembly) and registers a **string-keyed** `VectorStoreCollection`. So with a Guid model it registered `<string, IngestedChunk>` while `SemanticSearch` needed `<Guid, IngestedChunk>` → DI validation failure at `builder.Build()` ("Unable to resolve service for type VectorStoreCollection`2[System.Guid, …]"). The connector's registration helper is hard-wired to the string-key assumption the docs advertise; it has no way to register a Guid-keyed collection. Fix: bypass the helper and register the collection explicitly off the store with the correct key type —
```csharp
builder.Services.AddMongoVectorStore();
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<VectorStore>().GetCollection<Guid, IngestedChunk>(IngestedChunk.CollectionName));
```
`VectorStore.GetCollection<TKey, TRecord>` *is* arity-2 and honors the key type, so it produces the `<Guid, _>` collection the writer's Binary `_id`s actually need. **Lesson compounds:** the connector's *convenience* registration encodes the (wrong-for-us) string-key assumption; dropping to the *core* MEVD API (`VectorStore.GetCollection`) gave back the control the helper took away. When a connector helper fights you, the abstraction underneath usually still has the knob.

### §Mongo-key — trust the store, not the doc
The MS Learn "MongoDB Vector Store connector" page lists "Supported key property types: string." For connector `1.74.0-preview` that is **wrong or outdated** — Guid keys work and are in fact what the DataIngestion writer produces by default. The tell was visible only at runtime on the *read* path (write succeeded, deserialize failed), so it slipped past the build and past ingestion — the **third** "boot/build-green ≠ works" confirmation this POC produced (cf. §2, §8b: exercise the *read* path). Lesson, platform-flavored: when a doc claims a constraint, **verify against the running system before reshaping a model around it** — a one-line `findOne()` showing `_id : Binary` would have saved the whole Guid→string detour.

---

## 4. atlas-local muck tax (partial — live items still ⏳)

The "muck" the image docket warned about showed up exactly where predicted — the apparatus cost is real, just relocated. Two concrete findings from wiring it:

**4.1 Aspire's MongoDB health check is incompatible with atlas-local (live blocker, fixed).** `mongodb/mongodb-atlas-local` self-configures as a **single-node replica set**. Aspire's `AddMongoDB` health check builds a `MongoClient` from a connection string with **no `directConnection=true`**, so the driver's SDAM server-discovery waits for a replica-set primary it can't reach directly → the health check never completes → the container shows **Up** but the resource never goes **Healthy** → every `WaitFor(vectordb)` dependent hangs forever. This is a known, still-open Aspire gap: it offers no hook to add connection-string options to the health check (dotnet/aspire #7995, #6811).

Fix (AppHost): remove the `HealthCheckAnnotation` from the mongo server + database resources so dependents gate on the container being **Running** instead of **Healthy**; the app clients already connect with `DirectConnection = true` (set per-tier in `Program.cs`) and work the instant the node accepts connections — `directConnection` talks to the single node directly and skips the primary-election wait entirely, so there's no readiness race. The "proper" alternative (a custom `IHealthCheck` that runs `directConnection` + `replSetInitiate`, per the community gist) is overkill here because atlas-local auto-initiates its replica set. **Platform note:** if the platform ever runs Mongo as a replica set under Aspire, this health-check gap is a standing tax — budget a custom health check or the annotation-removal shim.

**4.2 Connection-name discipline.** A latent second bug rode along: the AppHost published the database resource as `"documentdb"` while both clients requested `AddMongoDBClient("vectordb")` — a mismatch that the health-check hang masked (dependents never started, so the name resolution never ran). The Aspire connection name is a string contract between AppHost and consumer with no compile-time check — exactly the kind of stringly-typed seam the platform's typed-everything posture exists to kill. For the POC: matched both to `"vectordb"`. Platform: connection names deserve a shared constant or generator, not hand-typed strings in three files.

**4.3 Credential env-var mismatch (Landmine B — fired live, fixed).** It bit exactly as predicted. Aspire's `AddMongoDB` auto-generates an `admin` user + password and bakes them into the connection string (`mongodb://admin:pw@host?authSource=admin&authMechanism=SCRAM-SHA-256`) — credentials are included whenever a password parameter exists, and `AddMongoDB` always generates one. It injects them as `MONGO_INITDB_ROOT_{USERNAME,PASSWORD}` — the env vars the **plain mongo** image reads. atlas-local reads `MONGODB_INITDB_ROOT_*` (note the extra `DB`), ignored Aspire's vars, started with no such user, and every client auth failed: `MongoAuthenticationException: SCRAM-SHA-256 ... Authentication failed`. (A transient `MongoNodeIsRecoveringException: InterruptedAtShutdown` preceded it — just the replica set still initializing; harmless, the driver retries through it.)

Fix (AppHost): forward the **same** generated credentials under atlas-local's names —
```csharp
mongo.WithEnvironment(context =>
{
    context.EnvironmentVariables["MONGODB_INITDB_ROOT_USERNAME"] = "admin";
    context.EnvironmentVariables["MONGODB_INITDB_ROOT_PASSWORD"] = mongo.Resource.PasswordParameter!;
});
```
so atlas-local seeds the exact user the connection string authenticates as. **Critical companion step:** atlas-local seeds the root user **only on first init of an empty data dir** — the stale volume from the failed runs had already initialized without it, so the data volume had to be `docker volume rm`'d for the fix to take. (Two volume resets this stage now: dimensions in §8b's neighbor, credentials here — `WithDataVolume` + `Persistent` is a double-edged convenience.)

**Platform takeaway:** atlas-local + Aspire's MongoDB integration has *two* impedance mismatches (replica-set health check §4.1, credential env-var names §4.3), both because Aspire targets the plain `mongo` image. If the platform standardizes on atlas-local (or Atlas proper), a small purpose-built hosting helper that bakes in directConnection-health + the `MONGODB_INITDB_ROOT_*` names would pay for itself — or use Atlas (managed) and sidestep both.

**Still ⏳ (need the live run):** image pull size + first-boot time; and **vector search index readiness** — atlas-local builds the `$vectorSearch` index via `mongot` asynchronously, so the first query after ingestion may return empty-without-error until the index is live (the §7-flagged "is it ready?" contour).

---

## 5. Voyage API behaviors (Stage ②, partial)

- **Batching:** `MaxBatchSize` dropped to 8 in the POC config to keep each request well inside the 10K TPM free-tier window. The adapter's chunking (Voyage caps at 1000 inputs/request; voyage-4 at 320K tokens/request) is correct but the free-tier *token* limit binds first, not the input-count limit.
- **Truncation = false** (inverting Voyage's API default of true) did not produce failures during the small-corpus run — inputs stayed under model limits. The guard is correct but un-stressed; a real corpus with an oversized chunk would exercise the loud-failure path.
- **Usage:** `total_tokens` mapped into `GeneratedEmbeddings.Usage.TotalTokenCount`; `InputTokenCount` left null (the wire only sends a total). Accurate.
- **Matryoshka / dtype:** `output_dimension = 1024` passed explicitly and round-tripped (live smoke test asserts 1024-length vectors). Quantized dtypes (int8/binary) structured-for but not exercised.
- **429 behavior:** see §0.2 — graceful, Retry-After honored.

---

## 6. Chunker tokenizer mismatch (Stage ②)

`SemanticSimilarityChunker` uses `TiktokenTokenizer.CreateForModel("gpt-4o")` (cl100k/o200k) — a different tokenizer than Voyage's. Left as-is with a comment; it only gates chunk *sizing*, not correctness. Observed effect on chunk quality during the run: **(to be filled after reviewing ingestion output)**. For the OSS adapter, whether to ship a Voyage-aware token counter is an open question (Voyage's tokenizer isn't a public .NET package as of 2026-06-07).

---

## 7. Adapter OSS-readiness gaps

### 7.0 Build-vs-adopt: `tryAGI/VoyageAI` (decide at port time)

Buvy surfaced `https://github.com/tryAGI/VoyageAI` (2026-06-07) — and the lineage argument is strong: tryAGI's Anthropic SDK *became* the official `Anthropic` package we use on the chat side. Their VoyageAI library is **auto-generated from Voyage's OpenAPI spec via AutoSDK**, MIT-licensed, depends on `Microsoft.Extensions.AI`, and **claims `IEmbeddingGenerator<string, Embedding<float>>` support**.

**But the gap is exactly our value-add.** Per its docs, tryAGI/VoyageAI does **not** handle the `input_type` query/document distinction (§2 — the asymmetry that forced manual query embedding through MEVD), and surfaces no rate-limiting, batching, `output_dimension`, or `output_dtype` options. Those are precisely the correctness-bearing concerns this POC's adapter was built around — `input_type` most of all (get it wrong and query vs document vectors aren't comparable, silently).

**The shape this implies for the port** (mirrors the Anthropic SDK exactly): tryAGI/VoyageAI is a candidate for the **wire-client layer** — replacing our hand-rolled `HttpClient` + `VoyageJsonContext` wire shapes with an auto-generated, auto-updated client. Our `Voyage.Extensions.AI` then becomes the **M.E.AI semantics layer on top** — the `input_type`-pinned, honor-or-throw, batching adapter — the way `AsIChatClient()` wraps the generated Anthropic client. Two clean possibilities to evaluate at port time:
1. **Wrap** tryAGI's generated client in our asymmetry-safe adapter (we own the semantics, they own the wire + spec-tracking).
2. **Contribute** `input_type` handling upstream to tryAGI and consume it directly (less to maintain, but cedes control of the asymmetry guarantee to an auto-generated surface — risky for a correctness invariant).

**Decision deferred to the port window** (per the agreed sequencing — the library extracts to its own repo there). This finding is the port's first agenda item: evaluate whether tryAGI's generated client exposes `input_type` on the request (even via `AdditionalProperties`) before choosing wrap vs. contribute vs. stay-hand-rolled. The POC's hand-rolled adapter stays as-is — it's the working courtroom exhibit and the reference design regardless of which client layer wins.

### 7.1 Remaining gaps

What stands between current POC state and a publishable `Voyage.Extensions.AI`:
- **Token-aware chunking / request sizing:** the adapter chunks by input count, not token count. A real consumer needs token-budget awareness to avoid 320K/request rejections. Requires Voyage's tokenizer (not yet a .NET package) or a conservative heuristic.
- **Quantized dtypes:** int8/uint8/binary/ubinary are structured-for (the options surface anticipates them) but only float32 is delivered. Adding them means `Embedding<sbyte>` / `Embedding<byte>` generator variants.
- **Package metadata:** package id, README, license (Apache 2.0 to match the ecosystem), XML doc completeness (`GenerateDocumentationFile` is off in the POC), and a `net10.0`-vs-broader target decision.
- **Base-URL config** for the Atlas-fronted Voyage endpoint (`https://ai.mongodb.com/v1/`) is built and ready but untested against the real Atlas AI API — exercise when/if Stage ③ lands on Atlas.
- The resilience pattern (§0) is **not** the adapter's to ship — it's host wiring. The OSS README should point consumers at the shared-limiter-inside-retry pattern without baking it in.

---

## 8. Worker breakout — the `.Server`/`.Worker` wall, in miniature (Stage ②.5)

Buvy's mid-flight call: *"break out a worker and treat it as waitforcompletion in aspire — this is precisely what a POC should do, figure out these contours."* It surfaced the platform's central architectural wall as a live, observable contour.

- **Why it came up:** ingestion ran in the web tier, so the first chat question blocked the UI while the PDF indexed at 3 RPM. That's the `.Server` doing `.Worker` work — the exact boundary the platform's hard wall exists to enforce.
- **Shape built:** `VoyageEmbeddings.Ingestion` (console, run-to-completion, exits 0) does all embedding-on-write; `VoyageEmbeddings.Web` serves queries only; `VoyageEmbeddings.Backend` holds the shared `IngestedChunk`. AppHost gates the web app on the worker via `WaitForCompletion(ingestion)`.
- **One generator per tier — answers Buvy's "why two registrations?" question.** Once ingestion and serving are separate processes, each registers exactly **one** generator: the worker gets document-side, the web gets query-side. The keyed-pair ceremony (§2) collapses; the non-generic-alias safety comment in Web now reads "this tier has no upsert path, so a query-flavored ambient generator can never silently embed corpus content." The wall makes the asymmetry trivially safe instead of carefully-guarded.
- **`IncrementalIngestion = true`** (was false): the worker reruns every boot under `WaitForCompletion`, so unchanged documents must not re-embed — at 3 RPM, redundant re-indexing would burn the token budget for nothing.
- **Transferable:** this is the platform's persistence design in a 3-container shadow. The Mimir spec's "only Mimir's worker touches the vector store; serving reads go through a published surface" is exactly this split. The POC proved the Aspire orchestration mechanics (`WaitForCompletion`, shared-project for wire shapes, per-tier DI) before the platform has to commit to them.
- **Unexpected contour — shared source documents (live, 2026-06-07).** Moving the source docs out of `Web/wwwroot/Data` into the worker's `Ingestion/Data` (so the worker could ingest them) **broke the citation links** — the web tier serves those same PDFs at `/Data/{file}` for the in-browser citation viewer, and they vanished from its wwwroot. The bug exposed the real ownership question: source documents are neither the worker's nor the web's exclusively — the worker *ingests* them, the web *serves* them to the user. POC fix: a served copy in `Web/wwwroot/Data` alongside the worker's ingestion copy (two copies, each tier self-contained). **Production contour (FINDINGS for the platform):** source/claim/policy documents belong in **shared blob storage** (or a document store), and *both* the ingesting worker and the serving web tier read from there — never a local per-project file copy. The duplication in the POC is the honest stand-in for "we don't have the shared doc store yet." This is a direct input for any platform spec that handles user-facing source documents (claims attachments, policy PDFs, ACORD forms).

---

## 8b. Floating wildcards floated a transitive dep UP across a preview breaking change (live, 2026-06-07)

The Microsoft `aichatweb` template ships **floating version wildcards** on nearly every package (`1.*-*`, `10.*`, `1.*`). That produced a runtime crash no build caught — and the debugging took a wrong turn worth recording honestly.

```
TypeLoadException: Could not load type 'Microsoft.Extensions.VectorData.VectorSearchFilter'
from assembly 'Microsoft.Extensions.VectorData.Abstractions, Version=10.6.0.0'
  at Microsoft.SemanticKernel.Connectors.Qdrant.QdrantCollection.SearchAsync(...)
```

**Wrong hypothesis (act 1):** I first blamed a **stale binary** — believed `VectorSearchFilter` existed in neither the connector nor the abstractions (a `strings | grep` check returned 0 for both), so concluded the runtime must be loading a leftover old DLL. I pinned everything to the *highest resolved* versions (abstractions → 10.6.0), nuked bin/obj, rebuilt. **The error persisted**, which falsified the hypothesis.

**The `strings` trap (the lesson under the lesson):** `strings` does **not** reliably surface .NET metadata strings on these (ReadyToRun/compressed-metadata) assemblies — it reported 0 references where the types genuinely existed. A proper metadata reader (`System.Reflection.Metadata.PEReader` → `MetadataReader`, walking `TypeReferences`/`TypeDefinitions`) gave the authoritative answer:

| Assembly | `VectorSearchFilter`? |
|---|---|
| `VectorData.Abstractions` **10.1.0** | **present** (TypeDef) |
| `VectorData.Abstractions` **10.6.0** | **removed** |
| `Connectors.Qdrant` **1.74.0-preview** | **references it** (TypeRef) |

**Real root cause (act 2):** SK Qdrant connector `1.74.0-preview` was built against `VectorData.Abstractions` **10.1.0** (its nuspec declares `version="10.1.0"`, i.e. *≥* 10.1.0) and binds the `VectorSearchFilter` type. Abstractions **10.6.0 removed that type** — a breaking change within the same `10.x` line. The `Backend` project's `10.*` wildcard floated the *whole graph* up to 10.6.0 — a perfectly "valid" NuGet up-resolution — and the connector, never rebuilt against 10.6.0, threw `TypeLoadException` the first time its `SearchAsync` was JIT'd. `SemanticSearch.cs` was correct throughout (it compiled against the expression-based `VectorSearchOptions<T>.Filter`); the break was purely the connector ↔ abstractions version skew.

**Fix:** pin `Microsoft.Extensions.VectorData.Abstractions` **down to 10.1.0** (the version the connector declares), pin every other wildcard to its resolved exact, force-delete bin/obj, rebuild. Verified at the binary level: the rebuilt Web bin's abstractions DLL contains the `VectorSearchFilter` TypeDef and `deps.json` resolves 10.1.0.

**Transferable lessons:**
1. **Pin-exact is determinism, not pedantry** — and the platform's law would have prevented this. But the sharper rule: **don't pin to the *highest resolved* version; pin to the version your consumer was built against.** "Highest wins" is NuGet's default *and* the trap — a transitive consumer (the connector) can declare a floor (`10.1.0`) while a sibling wildcard floats the shared dep past a breaking removal. My first fix pinned the broken-high version *in place*; the correct pin was *downward* to the connector's declared version.
2. **`strings` is unreliable for .NET assembly type analysis** — R2R/compressed metadata hides type names from it. Use `System.Reflection.Metadata` (built into the runtime) to walk `TypeReferences`/`TypeDefinitions`. A wrong tool produced a confident-but-false reading that sent the first fix the wrong direction.
3. **Boot-gate blind spot, second confirmation (extends §2).** This fires only on the **read path** (`SearchAsync`) — the write path (`VectorStoreWriter`) and the live smoke test (Voyage adapter direct, no Qdrant) both sailed past it. A gate must exercise the **read path**, not just "starts" and "ingestion writes."
4. **Falsify fast, re-diagnose from authoritative tools.** The first hypothesis survived only because the evidence tool (`strings`) was wrong. When a fix doesn't move the error, distrust the *measurement*, not just the theory — switch to a ground-truth tool before pinning a second guess.

**Open item:** `NU1903` transitive vulnerability warning on `Microsoft.Bcl.Memory 9.0.4`. Pre-existing, transitive, not introduced by the pinning. Run `dotnet nuget why` to find the dragger and decide whether a direct pin to a patched version is warranted before the OSS cut.

## 9. Process notes (for the next POC / the writing-plans loop)

- **Boot gates need DI depth** (§2) — HTTP 200 on `/` is not "it works" when failures hide in circuit-injected services.
- **Live API throttling is a feature, not a bug, for a POC** — it forced the resilience contour (§0) into the open where a generous paid tier would have hidden it.
- **Mid-flight design rulings (rate-limit shape, worker breakout) were the highest-value moments** — the spec-first discipline got us a clean adapter, but the real architectural lessons came from running the thing against reality and reacting. Both got folded back into the design via spec/plan amendments rather than silent drift.
