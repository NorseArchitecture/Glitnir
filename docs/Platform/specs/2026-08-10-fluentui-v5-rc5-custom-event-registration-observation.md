# Fluent UI Blazor v5 RC5 custom-event registration observation

**Observed:** 2026-08-10
**Status:** Upstream diagnostic note; not a Norse design decision
**Affected package:** `Microsoft.FluentUI.AspNetCore.Components` `5.0.0-rc.5-26219.1`
**Package source commit:** `e4d168193e5d5f334588d977a73fa8159ea8d3b2`
**Runtime:** .NET/ASP.NET Core `11.0.0-preview.6.26359.118`
**Browser driver:** Microsoft.Playwright `1.61.0`, Chromium 149

## Summary

Fluent UI Blazor v5 RC5's JavaScript initializer registers `overflowchange` as a Blazor custom
event whose `browserEventName` is also `overflowchange`. The .NET 11 Blazor runtime rejects that
registration because the custom name and browser event name are identical:

```text
Error: The custom event 'overflowchange' cannot have the same name as its browserEventName
'overflowchange'. Choose a different name for the custom event.
```

The exception interrupts Fluent UI's `afterStarted` initializer after earlier event registrations
have succeeded but before its one-time completion flag is set. When another render mode invokes the
initializer in the same document, initialization starts over and the first previously registered
event fails as a duplicate:

```text
Error: The event 'accordionchange' is already registered.
```

The `accordionchange` error is therefore a secondary partial-initialization symptom, not an
independent application registration.

## Packaged initializer shape

The RC5 static web asset contains these effective registrations (formatting expanded from the
minified package asset):

```javascript
registerCustomEventType("accordionchange", {
    browserEventName: "change",
    createEventArgs: /* Fluent accordion projection */
});

registerCustomEventType("overflowchange", {
    browserEventName: "overflowchange",
    createEventArgs: /* Fluent overflow projection */
});
```

The second call violates the .NET 11 `registerCustomEventType` contract. Because the initializer's
completed guard is set only after the registration sequence finishes, the thrown exception leaves
the document in a partially registered state.

The asset is supplied by the package at:

```text
staticwebassets/Microsoft.FluentUI.AspNetCore.Components.lib.module.js
```

The Norse application does not register either event itself.

## Reproduction contexts

The observation is reproducible in two independent Yggdrasil hosts.

### InteractiveAuto Blazor Web App

1. Start the real ASP.NET Core host using global `InteractiveAuto` rendering.
2. Load `/` in a fresh browser context.
3. Navigate to `/reference/country-lookup` after the WebAssembly resources finish downloading.
4. Observe the renderer transition through the Fluent UI initializer's `web`, `server`, and `wasm`
   startup modes.

Observed page-error sequence:

```text
afterStarted mode "web"
The custom event 'overflowchange' cannot have the same name as its browserEventName 'overflowchange'.

afterStarted mode "server"
The event 'accordionchange' is already registered.

afterStarted mode "web" (fresh route startup)
The custom event 'overflowchange' cannot have the same name as its browserEventName 'overflowchange'.

afterStarted mode "wasm"
The event 'accordionchange' is already registered.
```

The country lookup itself does not render an Accordion or Overflow component. Despite the startup
errors, its WebAssembly renderer becomes interactive, its gRPC-Web request returns HTTP 200, and the
expected component result renders. The errors are nevertheless real unhandled page errors.

### BlazingStory pure-WebAssembly catalog and canvases

The outer catalog document and each independent canvas iframe boot a separate WebAssembly runtime.
Each new document reports the exact `overflowchange` error once. It does not report the secondary
`accordionchange` duplicate because there is no second render-mode initializer in that document.

This second reproduction is useful because it removes InteractiveAuto and server-circuit lifecycle
from the equation: the invalid registration occurs in a plain WebAssembly document too.

## Minimal upstream reproduction

A compact upstream reproduction should use:

1. a .NET 11 preview 6 Blazor Web App;
2. `Microsoft.FluentUI.AspNetCore.Components` `5.0.0-rc.5-26219.1`;
3. `AddFluentUIComponents()` and the normal Fluent providers/layout setup; and
4. a browser `pageerror`/`window.error` observer before Blazor starts.

The pure-WebAssembly variant is the smallest proof of the primary defect. InteractiveAuto is useful
as a second test because it exposes the partial-initialization/idempotency consequence.

No Accordion or Overflow component needs to be rendered; loading the Fluent UI initializer is
sufficient.

## Likely upstream correction points

Two corrections appear independently valuable:

1. Give the Blazor custom event a name distinct from the native `overflowchange` browser event (or
   avoid custom registration if native event args are sufficient), while keeping the component's
   .NET event binding consistent with that name.
2. Make `afterStarted` restart-safe so one failed registration cannot leave earlier registrations
   to fail as duplicates on the next render-mode initialization.

An upstream regression test should cover both a single pure-WebAssembly startup and repeated
`afterStarted` calls representing InteractiveAuto's Web/Server/WebAssembly lifecycle.

## Temporary Norse canary treatment

The browser-runtime canary remains strict by default. It does not ignore console errors or Fluent UI
errors generally. Until the package is corrected, it admits only the exact primary/secondary
messages with local Fluent UI registration stack fingerprints and structural occurrence ceilings:

- Web.Server: at most two `overflowchange` and two secondary `accordionchange` errors.
- Stories.Server: at most seven `overflowchange` errors, matching the outer catalog plus the audited
  maximum of six live canvas runtimes.

Zero occurrences pass, so an upstream fix requires no compatibility delay. A changed message,
changed stack origin, or excess occurrence still fails the canary and requires renewed inspection.
