# The Serialization Seam — Format-Agnostic Payload Serialization Behind the Wire Border

**Status:** Ratified 2026-08-03 · **Realms:** Asgard (contracts), Midgard (machinery), Himinbjörg (first consumer)

## 0. Why This Exists

NORSE070 evicted Himinbjörg's personal-data-download endpoint for hand-rolling `JsonSerializer.SerializeToUtf8Bytes` below the wire border. The eviction was correct and the capability was legitimate — the crime was the realm holding the machinery. This seam restores the function the lawful way: Asgard declares a format-agnostic serialization contract (pure BCL surface — the `[DataContract]` razor applied to serialization: intent in the realm, encoding at the edge), Midgard implements it over `System.Text.Json`, realms inject it. STJ stays caged in Midgard; the function flows to every Backend-derived service.

Ported from private prior art (the Accelerator endpoint pairing — cited by name, never by path; it lives outside this workspace by design). The prior art predates the law and already obeys it: no STJ type appears anywhere on the abstraction surface.

## 1. The Contracts — `Norse.Abstractions.Backend` (Asgard)

Placement ruled: **`Abstractions.Backend`** — reachable by `{Realm}.Web.Server` and `{Realm}.Worker`, never by `.Components`/WASM/MAUI. **This is a permanently closed door, not deny-by-default (ruled 2026-08-03):** the client is purely gRPC and only ever talks to our own backend, which proxies any egress and returns the data. A client-side serialization seam is the on-ramp to a rogue integration wired straight into the SPA with a third-party API key leaked into client code (the Box-in-the-SPA incident class — it has actually happened); the architecture refuses the category, not the instance.

### `ISerializer` — format-agnostic, JSON-default

The prior art's serializer surface, renamed: **the contract is deliberately not JSON-constrained.** Its default case is JSON — the `ContentType` DIM default says so — but any format implements it: the canonical future example is an F# Data type-provider XML serializer inferring shapes from XSD, registered through DI and dropped in wherever composition points at it. That is why `ContentType` exists on the contract.

- `string ContentType` — DIM default `application/json`.
- `bool HasAsyncSupport` — DIM default `true`.
- `T? Deserialize<T>(byte[] bytes)` / `T? Deserialize<T>(Stream stream)` / `T? Deserialize<T>(string payload)`
- `ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default)`
- `void Serialize<T>(Stream stream, T obj, bool serializeNulls = false)`
- `string Serialize<T>(T obj, bool serializeNulls = false, bool prettyPrint = false)`
- `Task SerializeAsync<T>(Stream stream, T obj, bool serializeNulls = false, CancellationToken cancellationToken = default)`
- `byte[] SerializeToUtf8Bytes<T>(T obj, bool serializeNulls = false)`

### `NamingStrategy`

Prior-art enum plus the platform's enum law (`0` is never a real option):

```csharp
public enum NamingStrategy
{
	/// <summary>Sentinel CLR default — never a valid strategy; a caller always names its convention.</summary>
	Unspecified = 0,
	CamelCase = 1,
	PascalCase = 2,
	SnakeCase = 3,
	KebabCase = 4
}
```

### `ISerializerProvider`

The prior art's `IJsonSerializerProvider`, renamed to follow `ISerializer`'s format-agnosticism:

```csharp
public interface ISerializerProvider
{
	ISerializer this[NamingStrategy key] { get; }
}
```

The indexer answers the registered default-format serializer for a naming convention. A future format joins by its own DI registration and composition-root choice, not by widening this contract (`Unspecified` → implementation throws — the smuggled-sentinel precedent).

## 2. The Machinery — Midgard

- `SystemTextJsonSerializer` (`sealed`, internal): one `JsonSerializerOptions` per instance, minted from its `NamingStrategy` — `CamelCase` → `JsonNamingPolicy.CamelCase`, `PascalCase` → null policy, `SnakeCase` → `JsonNamingPolicy.SnakeCaseLower`, `KebabCase` → `JsonNamingPolicy.KebabCaseLower`; `serializeNulls` → `DefaultIgnoreCondition`; `prettyPrint` → `WriteIndented` (per-call options variants cached, not re-allocated per call).
- `SerializerProvider` (`sealed`, internal): the prior art's shape — `ConcurrentDictionary<NamingStrategy, ISerializer>` with `GetOrAdd` lazy minting; throws on `Unspecified`.
- `AddNorseSerialization(this IServiceCollection)` — registers the provider as a singleton under `ISerializerProvider`. Called at the composition root (Yggdrasil), like every Midgard seam.
- **Project placement (ruled 2026-08-03, post-implementation):** **`Norse.Infrastructure.Backend`** — Midgard's mirror of Asgard's `Abstractions.Backend`, the shared server-side assembly (Web.Server + Worker), with the machinery under its own `Serialization/` folder (`namespace Norse.Infrastructure.Backend.Serialization` — path law). No per-functional-group packages: serialization is a known egress-from-docker concern and belongs in the shared assembly, not a minted `Infrastructure.Serialization` package. This project is new (Midgard had no Backend assembly before) — serialization is its first resident, not its purpose.
- Law posture: this project is `Norse.Infrastructure.*` — inside the wire border, STJ legal. No other realm ever references STJ; they reference `Abstractions.Backend` and inject.

## 3. First Consumer — Himinbjörg's Download Restored

`DownloadPersonalData` returns to `Identity.Web.Server` on the seam: inject `ISerializerProvider`, take `provider[NamingStrategy.CamelCase]`, emit `SerializeToUtf8Bytes(personalData)` with the response content type read from `serializer.ContentType`. Same behavior as the scaffold shipped, zero STJ in the realm, NORSE070 silent. The endpoint remains scaffold-quality (reflection over `[PersonalData]` properties) until the PII disclosure surface replaces it properly at that effort's resume — this restoration is function, not the final shape.

## 4. Deliberately Out of Scope (ruled 2026-08-03)

- **HttpClient egress machinery** (the prior art's `SerializerHttpClientBase`/`QueryStringHttpClient` and any `IHttpClientSerializer`-era plumbing): a much bigger lift, deferred until it can be gotten right. **Roadmap order ruled:** Himinbjörg stands up as a fully functioning gRPC service, Heimdall carries all the Blazor UI components, and the authn/authz story completes in its entirety — then the egress train. The seam ships now precisely so that lift starts from a lawful foundation.
- Client-side (WASM/MAUI) serialization contracts — **never** (see §1): the client speaks gRPC to our backend alone; egress is always proxied server-side. Not a deferral — a closed door.
- Additional formats (the XSD/F# type-provider XML case) — the contract shape welcomes them; none ships until a consumer exists.
- **AOT/trim for the STJ arm:** `SystemTextJsonSerializer` serializes caller-supplied types via reflection — IL2026/IL3050 fire correctly and are suppressed as a documented *accepted gap* (deliberately unlike the bounded-surface false-positive precedents in `Infrastructure.Web.Server/Json`). The real fix is a source-generated `JsonTypeInfo` (`JsonSerializerContext`) design pass — tracked here, shipped when the platform's AOT posture demands it.

## 5. Verification

- Unit tests per naming strategy (round-trip, null-handling, pretty-print, `ContentType` default, `Unspecified` throws) in the Midgard implementation's test project; Asgard contract needs only compilation (no logic).
- The law is the integration proof: Himinbjörg's restored endpoint builds green under NORSE070 with the analyzer attached; the realm never references STJ.
- Full realm suites green per ship-gate discipline (Asgard → Midgard → Himinbjörg, `NorseRef` in-workspace before gates).
