# Scheduled Ceremonies — Runbook

**Date:** 2026-08-04
**Status:** Settled doctrine, documented — deliberately *not* a design or implementation effort. The decision is dead simple and final; this document exists so the process is written down and so each hosting scenario has a suggested recipe. Endpoints land with the ceremonies that need them; nothing here is built speculatively.
**Doctrine source:** `../../norse-architecture-feature-roadmap.md` Phase 1 item 8 (the tick contract, watchdog law, in-house-scheduler rejection).

---

## 1. The Process (the whole thing)

1. **The schedule lives outside the system.** A cron job, cloud scheduler, or anything else that can POST on a schedule. The platform never hosts a scheduler.
2. **The tick authenticates like any machine.** The scheduler obtains a client_credentials grant from OpenIddict for the system user (the machine door — [Himinbjorg#49](https://github.com/NorseArchitecture/Himinbjorg/issues/49)).
3. **The tick is a plain HTTP POST** to a thin ceremony endpoint on the web server, carrying the ceremony name and the logical scheduled fire time.
4. **The endpoint sends the command on Ratatoskr.** The web server does what it always does — authn, fingerprint, dispatch. The worker stays dumb and just handles the command when it arrives.

That is the entire design. Scheduled work rides the same spine as user work — same auth, same idempotency, same pipeline — and editing a schedule is an operator action, never a deploy.

**Why no in-house scheduler (Quartz.NET, Hangfire — rejected, roadmap-ratified):** they make the worker smart, turn schedule state into a persistence concern, and hide cadence from the operator.

## 2. Idempotency (load-bearing)

Every external scheduler is at-least-once (EventBridge documents this explicitly). The tick payload carries **ceremony name + logical scheduled fire time**; the idempotency key derives from that pair, so duplicate deliveries collapse in the platform's fingerprint spine ([Midgard#58](https://github.com/NorseArchitecture/Midgard/issues/58)) — the principal is the system user, the payload is the pair, the second delivery is a no-op by construction.

## 3. The Watchdog Law

*The schedule is external, but the expectation of the schedule is internal.* Each ceremony declares its expected cadence. Runs are already recorded (audit / receipt ledger). A small in-house dead-man's switch — the only in-house piece, an alerter, not a scheduler — fires when last-run exceeds tolerance. "Epoch destruction quietly stopped eight months ago" must be impossible to miss.

## 4. Hosting Recipes

The contract: **anything that can POST with a client_credentials token on a schedule satisfies it.** Zero cloud dependency by construction. Suggested shapes per scenario — recipes, not requirements:

### Bare metal / sovereign / VM

`cron` (or a systemd timer) + `curl`, two calls:

```bash
# 1. Token from OpenIddict (client_credentials, system application)
TOKEN=$(curl -s -X POST https://$HOST/connect/token \
  -d "grant_type=client_credentials&client_id=$CLIENT_ID&client_secret=$CLIENT_SECRET" \
  | jq -r .access_token)

# 2. Fire the tick — ceremony name + logical fire time
curl -s -X POST https://$HOST/ceremonies/tick \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d "{\"ceremony\":\"epoch-key-destruction\",\"scheduledFor\":\"$(date -u +%Y-%m-01T00:00:00Z)\"}"
```

### Kubernetes

A `CronJob` running the same two-call script in a minimal curl image. Secrets via the cluster's secret store; schedule edits are `kubectl apply`, not deploys.

### AWS

**EventBridge Scheduler → API Destination.** API Destinations support OAuth client_credentials natively: create a Connection (auth type `OAUTH_CLIENT_CREDENTIALS`) pointing at the OpenIddict token endpoint; EventBridge handles token acquisition, caching, and refresh itself. The schedule targets the API Destination with the ceremony payload. No code anywhere.

### Azure

**Logic App (Consumption) with a Recurrence trigger** and two HTTP actions: POST the token endpoint, then POST the tick with the bearer from step one. (Azure Scheduler is retired; Logic Apps recurrence is the roadmap-named successor. The built-in HTTP OAuth support is Entra-shaped, hence the explicit two-step against OpenIddict.)

### GCP

**Cloud Scheduler → Cloud Workflows.** Cloud Scheduler's HTTP target only mints Google-signed tokens, so it can't do the OpenIddict token dance alone; pair it with a two-step Workflow (`http.post` token, `http.post` tick). The general rule when a scheduler can't speak client_credentials natively: pair it with the platform's thinnest workflow primitive — never move the logic into the worker.

### Local dev

No escape hatch, on purpose: obtain a client_credentials JWT and fire the tick endpoint from Vafþrúðnir (Bruno). A manual fire is just a scheduler with an irregular cron expression, and it exercises the *real* production path — same auth, endpoint, command, pipeline. Duplicate-tick collapse is demonstrable by hand the same way: fire twice, watch it run once.

## 5. Standing Ceremonies (as of this writing)

| Ceremony | Cadence | What it does |
|---|---|---|
| Epoch-key destruction | Monthly (per realm/jurisdiction bucket) | Destroys retention-epoch keys past their statutory lifetime — every field wrapped under them goes permanently dark platform-wide; receipt appended to the ledger ([Himinbjorg#55](https://github.com/NorseArchitecture/Himinbjorg/issues/55)) |
| Blob reaper | Lazy / off-peak | Walks shredded-key manifests and reclaims Edda blob storage — housekeeping, not erasure; the key destruction already erased |

## 6. Gates

Nothing in this runbook is buildable until its dependencies exist, and that is fine — the runbook is the deliverable:

- Machine door (client_credentials): [Himinbjorg#49](https://github.com/NorseArchitecture/Himinbjorg/issues/49)
- Idempotency spine: [Midgard#58](https://github.com/NorseArchitecture/Midgard/issues/58)
- Command dispatch: Ratatoskr's opening ([Ratatoskr#28](https://github.com/NorseArchitecture/Ratatoskr/issues/28) records the message-plane authn boundary)
- The ceremonies themselves arrive with their features (epoch keys with Class B retention; the reaper with Edda)
