# Phase 3A — Display host architecture

## Status

Phase 3A implementation and live gates are complete on `codex/phase-3a-display-host`, tracked by Issue #30 and PR #32. The branch remains stacked on the Phase 2 Audio MVP until PR #28 is merged.
CI is configured for every pull-request base so stacked phase branches receive the same Windows build/test gate as PRs targeting `main`.

## Scope boundary

This phase productizes the native display surface proven by Gate B without referencing a Spike assembly. It owns:

- physical-pixel geometry and per-monitor DPI conversion;
- fail-closed `Place`, `VerifiedNoFit`, `UnsupportedConfiguration`, and `TransientUnknown` decisions;
- raw Win32 primary-taskbar discovery plus UI Automation landmarks;
- `PopupPreserved` native hosting, drawing, hit testing, and mouse capture;
- static, versioned state rendering and interaction production;
- floating or hidden fallback when native placement is verified unsafe.

Named-pipe IPC, live Audio MVP integration, Explorer-generation recovery, host supervision, and reconnect snapshots belong to Phase 3B.
The repeating UIA watcher will be isolated in a per-Explorer-generation helper process as accepted in [ADR-0001](adr-0001-uia-watcher-process-boundary.md); it will not be registered in the long-lived display host.

## Placement invariants

All geometry is half-open and expressed in physical pixels. Negative virtual-desktop coordinates are valid. A placement may be returned only when:

1. the primary horizontal taskbar and its DPI are known;
2. the Start landmark is present and contained by the taskbar;
3. Start is not at the verified left edge; Windows 11 left alignment is an explicitly unsupported product configuration;
4. an optional Widgets landmark is ordered before Start;
5. every obstacle is valid and its expanded interval is removed from the candidate lane;
6. the final rectangle is contained by the taskbar and selected gap;
7. final intersection area with every standard element is zero pixels.

Left-aligned Start returns `UnsupportedConfiguration / UnsupportedAlignment` and routes directly to Hidden, never Floating. Incomplete, contradictory, or unrepresentable evidence returns `TransientUnknown`; a complete **center-aligned** observation with insufficient height or width returns `VerifiedNoFit`. None of these decisions permits a guessed native placement.

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
- hidden-first PopupPreserved host creation with class, process, parent, style, bounds, DPI, cloak, and color-key verification before promotion;
- mouse capture, drag preview/commit, wheel commit, monotonic interaction envelopes, and fail-closed layout invalidation;
- one shared HWND-free slider interaction session used by both Native and Floating surfaces;
- an explicit-confirmation, bounded static preview command with natural HWND teardown;
- pure, overflow-safe floating fallback placement constrained to the verified primary work area;
- hidden-first, unowned, non-topmost Floating HWND validation with the same renderer and interaction contracts;
- explicit routing of `Place` to Native, center-aligned `VerifiedNoFit` to verified Floating, and both `UnsupportedConfiguration` and `TransientUnknown` to Hidden;
- a sanitized, read-only product `inspect` command;
- focused coverage for centered placement, left-aligned unsupported/Hidden routing, incomplete and contradictory evidence, 100/125/150/200% DPI, negative coordinates, compact fragmentation, discovery adaptation, and render geometry.

The suite remains intentionally focused: twenty-two new test executions were added to the existing Foundation suite rather than porting the Spike's diagnostic test inventory.

## Phase 3B follow-ups

- complete the retained-continuity race checks from Issue #15 in Phase 3B (the Phase 3A exclusion provenance and create-time invalidation boundaries are implemented);
- implement the accepted ADR-0001 observer helper in Phase 3B before recovery loops are enabled.
- reproduce and resolve center-aligned NoFit Floating z-order behavior from Issue #33; if safe visibility cannot be guaranteed, route that condition to Hidden and update the specification.
