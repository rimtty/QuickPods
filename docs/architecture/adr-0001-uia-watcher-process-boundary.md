# ADR-0001: UI Automation watcher process boundary

## Status

Accepted, 2026-08-06. Rationale superseded by [ADR-0002](adr-0002-shell-signal-observer.md) on 2026-09-07; the observer process boundary remains, but the observer no longer registers a UI Automation subscription.

## Context

The Windows UI Automation provider used for taskbar observation can retain USER resources across Explorer generations even after managed handlers are removed. A long-lived taskbar host must not accumulate provider-generation resources, but QuickPods still needs prompt invalidation when taskbar landmarks or Explorer change.

## Decision

A short-lived `QuickPods.TaskbarObserver.exe` owns the repeating UI Automation subscription for exactly one verified Explorer generation.

- `QuickPods.TaskbarHost.exe` owns placement, native/floating windows, rendering, input, main-app IPC, and fail-closed presentation state.
- The observer independently discovers the primary taskbar after a same-user authenticated pipe handshake.
- Raw window handles and process identifiers are not passed on the command line or persisted.
- Observer output is a versioned, sanitized invalidation batch.
- Provider disconnect, Explorer-generation change, protocol failure, parent-pipe loss, or callback failure retires the observer.
- Parent shutdown terminates the observer through an owned job object.
- Repeated observer failure hides the embedded surface instead of enabling aggressive polling or stale placement.

One-shot UI Automation scans may remain in the host because they do not register a repeating event subscription.

## Consequences

- Provider-generation resources are reclaimed by process exit.
- The product includes one additional executable and an authenticated protocol.
- Observer failure cannot make placement evidence complete; the notification-area fallback remains available.
- The main application and audio/Bluetooth services remain isolated from Explorer provider failures.

## Verification

Changes to this boundary must cover:

1. Explorer restart and generation replacement;
2. retirement of every old observer;
3. stale or malformed invalidation rejection;
4. taskbar surface hide-first behavior;
5. stable long-lived app and host USER/GDI resources;
6. Start/Search continuity and exclusive native/floating ownership.
