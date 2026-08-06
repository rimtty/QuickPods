# Phase 3A — Display host architecture

## Status

Phase 3A is in progress on `codex/phase-3a-display-host` and is tracked by Issue #30. The branch is stacked on the Phase 2 Audio MVP until PR #28 is merged.

## Scope boundary

This phase productizes the native display surface proven by Gate B without referencing a Spike assembly. It owns:

- physical-pixel geometry and per-monitor DPI conversion;
- fail-closed `Place`, `VerifiedNoFit`, and `TransientUnknown` decisions;
- raw Win32 primary-taskbar discovery plus UI Automation landmarks;
- `PopupPreserved` native hosting, drawing, hit testing, and mouse capture;
- static, versioned state rendering and interaction production;
- floating or hidden fallback when native placement is verified unsafe.

Named-pipe IPC, live Audio MVP integration, Explorer-generation recovery, host supervision, and reconnect snapshots belong to Phase 3B.

## Placement invariants

All geometry is half-open and expressed in physical pixels. Negative virtual-desktop coordinates are valid. A placement may be returned only when:

1. the primary horizontal taskbar and its DPI are known;
2. the Start landmark is present and contained by the taskbar;
3. an optional Widgets landmark is ordered before Start;
4. every obstacle is valid and its expanded interval is removed from the candidate lane;
5. the final rectangle is contained by the taskbar and selected gap;
6. final intersection area with every standard element is zero pixels.

Incomplete, contradictory, or unrepresentable evidence returns `TransientUnknown`; a complete observation with insufficient height or width returns `VerifiedNoFit`. Neither decision permits a guessed native placement.

## Implemented slice

The first product slice contains:

- `PixelRect` and `PixelInterval` overflow-safe geometry;
- DIP-to-physical-pixel conversion for per-monitor DPI;
- standard/compact placement selection with padded obstacle subtraction;
- safe revalidation of an already-visible rectangle;
- one-shot raw Win32 primary-taskbar and native notification-area discovery;
- one-shot MTA UI Automation button discovery with a five-second fail-closed timeout;
- unique Start/Widgets identity validation and observation adaptation;
- trusted live-host exclusion requiring exact HWND, QuickPods class, same process, and actual-parent match;
- shared slider drawing/hit-test geometry and contract-state normalization;
- a color-keyed, double-buffered GDI renderer with distinct muted and unmuted speaker states;
- a sanitized, read-only product `inspect` command;
- focused coverage for centered placement, left-aligned NoFit, incomplete and contradictory evidence, 100/125/150/200% DPI, negative coordinates, compact fragmentation, discovery adaptation, and render geometry.

The suite remains intentionally focused: thirteen new test executions were added to the existing Foundation suite rather than porting the Spike's diagnostic test inventory.

## Remaining in Phase 3A

- Popup-preserved native and unowned floating hosts;
- static preview entry point, manifest, and natural shutdown validation;
- complete the remaining host-creation race checks from Issue #15 (the exclusion provenance boundary is implemented);
- an explicit disposition for the UIA watcher lifetime risk in Issue #18 before Phase 3B recovery loops are enabled.
