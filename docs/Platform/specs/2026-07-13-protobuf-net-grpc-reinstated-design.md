# protobuf-net.Grpc Reinstated as the Platform RPC Stack

**Date:** 2026-07-13
**Status:** Approved design, pre-implementation
**Owner:** Buvy
**Supersedes:** `Midgard/specs/2026-06-05-ui-composition-design.md` §8 in full (the native `Grpc.AspNetCore` + hand-authored `.proto` ruling). `Yggdrasil/specs/2026-05-20-yggdrasil-hosting-design.md`'s native-gRPC-stack passages (client/server package wiring, the explicit protobuf-net.Grpc rejection text) are superseded to the same effect. `2026-05-26-mediator-design.md` §3.1's "the interface carries `[MediatorService]` only — no protobuf-net.Grpc decoration" rule is retired (see §2).
**Resolves inconsistency in:** `Platform/specs/2026-06-07-egress-http-resilience-parsing-design.md` line 37 ("Inter-service RPC is protobuf-net.Grpc via `I{Context}Api}`") — that line was already correct and is no longer out of step with the rest of the platform.
**Trigger:** `Heimdall/specs/2026-07-13-authn-identity-split-design.md` §0 carved a scoped exception for Heimdall/Himinbjörg alone. Buvy's actual position, given directly in that same session: protobuf-net.Grpc should be the platform norm, not a one-realm carve-out — see §1.
**Blast radius:** zero shipped code anywhere in the platform references `Grpc.AspNetCore`, `Grpc.Net.Client`, `Grpc.Tools`, `Google.Protobuf`, or `.proto` files (verified by direct search across all twelve realm submodules). This is a pure documentation reconciliation, not a migration.

---

## 1. The Ruling

**protobuf-net.Grpc is the platform's RPC stack, full stop.** Every `I{Context}Api` contract is decorated directly with protobuf-net.Grpc's WCF-derived attribute model — `[ServiceContract]` on the interface, `[OperationContract]` per method, `[DataContract]`/`[DataMember]` on request/response records. No `.proto` files exist anywhere in the platform. No realm carries a native `Grpc.AspNetCore`/`Grpc.Tools` reference.

**Why the reversal:** the native stack's entire rationale was AOT-readiness risk. Two things make that the wrong trade for this platform:

1. **C# records and domain classes are the platform's actual lingua franca.** A hand-authored `.proto` file is a second source of truth for a shape the C# interface and its request/response records already fully express — ceremony bought for a risk, not a requirement in hand. Code-first is the pit-of-success choice: there is no drift to catch between the interface and the wire contract, because there is no second artifact.
2. **Blazor Server — the platform's primary hosting model — forecloses Native AOT already**, independent of which gRPC library sits underneath it. Most of the platform's realms carry zero incremental AOT risk from this choice in practice, because there was never an AOT path available to them regardless.

**Scoped acceptance of the risk that remains:** for a realm that genuinely does pursue Native AOT publishing someday (a standalone worker, a CLI, an M2M-only service with no Blazor Server surface), this decision is a live, deliberate trade — protobuf-net.Grpc's AOT story is real but immature. That trade is accepted consciously, not discovered by surprise; if a realm's AOT ambitions ever collide with it, Buvy's stated fallback is to help close protobuf-net.Grpc's AOT gap upstream (with Marc Gravell) rather than migrate the platform back to the native stack.

---

## 2. What Changes in the Mediator/Door Pattern

The mediator's core discipline is **unchanged**: `I{Context}Api` is the consumer-facing abstraction, dispatched to `IRequestHandler<TRequest, Outcome<TResponse>>` behind a generated forwarder, with `Outcome<T>` as the one return vocabulary across every door. What changes is purely which attributes decorate that interface and how the wire is generated:

| | Old (native stack, `2026-06-05` §8) | New (this spec) |
|---|---|---|
| Interface decoration | `[MediatorService]` only | `[ServiceContract]` (interface), `[OperationContract]` (methods) |
| Wire contract source | Hand-authored `.proto`, compiled via `Grpc.Tools` | Generated directly from the decorated C# interface — no second artifact |
| Server-side generated class | `{Service}Base` (proto-gen) wrapped by a thin `.Server` adapter delegating to `{Context}Service` | protobuf-net.Grpc hosts `{Context}Service : I{Context}Api` directly — the adapter layer collapses, there is one forwarder, not two |
| Client-side | `Grpc.Net.Client.Web` + `Google.Protobuf`-generated stub, wrapped by an adapter implementing `I{Context}Api` | protobuf-net.Grpc's client proxy (`channel.CreateGrpcService<I{Context}Api>()`) implements the interface directly — no separate generated stub, no wrapping adapter |
| Transport | gRPC-Web (both WASM and MAUI) | **Unchanged** — protobuf-net.Grpc rides ordinary `Grpc.Net.Client`/`GrpcChannel` underneath, which supports gRPC-Web identically; the WASM/MAUI transport-parity goal from `2026-06-05` §8 is preserved without modification |
| Mediator generator | Emits forwarder only when the interface carries `[MediatorService]` and *no* gRPC decoration | **Follow-up work, not resolved here:** the generator needs to accept `[MediatorService]` *and* protobuf-net.Grpc decoration on the same interface, or the platform hand-writes the thin forwarder per context until the generator is taught this shape. Tracked in §4. |

The `Outcome<T>` → wire-status mapping (`2026-06-05` §8.2/§8.3, mediator §7) is unaffected in spirit — a success/`Validation`/`NotFound`/`Conflict` result still needs to round-trip across the gRPC boundary — but its mechanics move from "the native adapter maps `Outcome` to gRPC status codes" to whatever protobuf-net.Grpc's own exception/status surface supports (`RpcException`-equivalent). Not fully worked out here; first realm to actually implement it (Heimdall, per its own spec) proves the mechanics, and this document gets a follow-up amendment once that's real.

---

## 3. Heimdall's Spec Is No Longer an Exception

`Heimdall/specs/2026-07-13-authn-identity-split-design.md` §0 recorded protobuf-net.Grpc as a scoped, one-realm exception. That framing is now obsolete — Heimdall is simply the first realm to build against the (reinstated) platform norm, not a carve-out from it. That document is being trimmed to reflect this in the same session.

---

## 4. Follow-Up Work

1. **Mediator source generator** — teach it to emit the forwarder for an `I{Context}Api` interface that also carries `[ServiceContract]`/`[OperationContract]`, instead of treating gRPC decoration as disqualifying. Until this lands, forwarders are hand-written (exactly as Heimdall's spec already describes for its bootstrap phase).
2. **`Outcome<T>` ↔ protobuf-net.Grpc status mapping** — needs its own worked-out design once a real implementation (Heimdall's bootstrap slice) proves the shape. Not blocking — hand-mapping is fine for a first cut.
3. **`Midgard/specs/2026-06-05-ui-composition-design.md`** and **`Yggdrasil/specs/2026-05-20-yggdrasil-hosting-design.md`** are left as historical record, not edited in place — this document is the superseding record, consistent with how `2026-06-28-migrations-framework-identity-schema-design.md` superseded `2026-06-07-auth-design.md` §10 by citation rather than in-place edit.
4. **`Bifrost/CLAUDE.md`** Platform Stack Defaults table already lists protobuf-net.Grpc for RPC — no change needed there; it was correct all along and this document brings the rest of the record back in line with it.

---

## 5. References

- `Midgard/specs/2026-06-05-ui-composition-design.md` §8 (superseded).
- `Yggdrasil/specs/2026-05-20-yggdrasil-hosting-design.md` (native-stack passages superseded).
- `Platform/specs/2026-05-26-mediator-design.md` §3.1 (decoration rule retired), §7 (door/`Outcome<T>` pattern carried forward in spirit).
- `Platform/specs/2026-06-07-egress-http-resilience-parsing-design.md` line 37 (no longer inconsistent).
- `Heimdall/specs/2026-07-13-authn-identity-split-design.md` (the trigger; first real implementation).
