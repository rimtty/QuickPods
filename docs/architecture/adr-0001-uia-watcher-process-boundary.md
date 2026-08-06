# ADR-0001 — UI Automation watcher process boundary

## Status

Accepted on 2026-08-06. This decision resolves the production-lifetime disposition tracked by Issue #18; implementation belongs to Phase 3B.

## Context

The selected PopupPreserved spike recovered successfully through ten Explorer restarts, but the long-lived process retained one additional USER object per Explorer provider generation. GDI objects and HWND counts stayed stable. Discovery-only and native/floating HWND create/destroy runs were stable; watcher-only restart runs reproduced the USER growth. Removing handlers on the owning MTA, resubscribing, and forcing managed collection did not reclaim it.

The product cannot accept generation-unbounded GUI-resource growth in `QuickPods.TaskbarHost.exe`. It also cannot weaken hide-first handling, Start/Search continuity, or the ten-second Explorer recovery target merely to avoid event subscriptions.

## Decision

The repeating UI Automation event subscription will be owned by a short-lived `QuickPods.TaskbarObserver.exe` helper, not by the long-lived app or taskbar-host process.

- `QuickPods.TaskbarHost.exe` continues to own native/floating HWNDs, placement, rendering, main-app IPC, and the fail-closed presentation state machine.
- One observer helper is launched for one verified Explorer/taskbar generation. It owns the UIA client, subscription, callbacks, and dedicated MTA.
- The observer independently discovers the primary taskbar after a same-user authenticated pipe handshake. Raw HWND/PID values are never placed on its command line or persisted.
- Observer output is limited to versioned, sanitized invalidation batches: event kind, owned/external/unknown source classification, subscription epoch, and an opaque generation ordinal.
- On provider disconnect, Explorer-generation change, protocol failure, parent-pipe loss, or callback failure, the observer signals termination when possible and exits. The host hides first, discards retained observer state, and launches a new generation only after fresh Win32/UIA proof.
- The helper is assigned to a kill-on-close Job Object so host termination cannot leave a residual observer. Restart attempts use the existing bounded supervisor/backoff policy; repeated failure leaves the surface Hidden rather than polling aggressively.
- Phase 3A may retain one-shot UIA scans because they do not register a repeating event watcher. Phase 3B must not add a repeating UIA handler to `QuickPods.TaskbarHost.exe`.

The native `IUIAutomation6` handler-group path may be evaluated later as an optimization, but it is not the production resource-lifetime boundary and cannot replace the helper-process acceptance gate without equivalent physical evidence.

## Consequences

- Explorer-generation USER-object retention is reclaimed when the observer process exits, independent of managed/provider cleanup behavior.
- Phase 3B gains one executable and a small authenticated protocol, plus parent/child lifecycle supervision.
- A dead or incompatible observer causes a safe Hidden/Floating transition; it cannot make placement evidence complete.
- The main app and Core Audio/Bluetooth services remain isolated from Explorer and UIA provider failures.

## Verification

Phase 3B must prove all of the following:

1. Ten Explorer restarts complete within the existing recovery target with exactly one current observer and one current display surface.
2. USER/GDI counts in the long-lived app and taskbar-host processes do not grow monotonically by Explorer generation.
3. Every retired observer exits and no observer/pipe/HWND residue remains after normal or forced host shutdown.
4. Observer disconnect and malformed/stale batches hide first and cannot reuse stale Start geometry.
5. Start/Search continuity, conservative obstacle union, and native/floating exclusivity remain intact.

Automated coverage stays focused on the observer lifecycle/epoch contract; physical Explorer restart and resource evidence remain mandatory.
