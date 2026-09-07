# ADR-0002: Shell-signal taskbar observer without UI Automation events

## Status

Accepted, 2026-09-07. Supersedes the rationale of [ADR-0001](adr-0001-uia-watcher-process-boundary.md); the observer process boundary remains.

## Context

`QuickPods.TaskbarObserver.exe` previously registered a repeating UI Automation event subscription (structure and property changes) rooted at the primary taskbar. UI Automation event registration is wired desktop-wide by the UI Automation core, and scope filtering happens on the client side. Every UI Automation provider on the desktop therefore learned that a client was listening, and applications that treat any UI Automation client as an assistive technology reacted accordingly. Google Chrome enabled its accessibility mode for the whole browser and listed the observer under `chrome://accessibility`, which produced measurable CPU load inside Chrome even though QuickPods never queried a Chrome window.

The host's one-shot UI Automation scans of the taskbar only send `WM_GETOBJECT` to Explorer's taskbar window and do not register a listener, so they are not affected.

The original reason for a separate observer process was that the UI Automation provider retained USER resources across Explorer generations. That reason disappears with the subscription, but a short-lived observer is still the right lifetime for Win32 registrations that must be recreated for every Explorer generation.

## Decision

The observer no longer uses UI Automation. It drives invalidation from Win32 signals that do not involve the accessibility infrastructure:

- A hidden, top-level, unowned window registered with `RegisterShellHookWindow` receives `WM_SHELLHOOKMESSAGE`. Window created, destroyed, and replaced notifications invalidate the taskbar structure. Redraw notifications are ignored while taskbar button labels are hidden (the default combined layout) because some applications redraw their title or icon several times per second; while labels are visible they are throttled to one invalidation per second. Activation, flash, and rude-app notifications are ignored because they do not change geometry.
- A `SetWinEventHook` registration scoped to the Explorer process and filtered to the taskbar HWND tree reports HWND-level create, destroy, reorder, show, hide, and location changes. Location changes only arm the settle timer.
- Registry change notifications on the user's taskbar settings keys (pinned items, Explorer advanced settings, taskbar placement state, and search box mode) invalidate the structure. Every registry watch is optional; a missing key or failed registration degrades silently.
- The hidden window also consumes `WM_SETTINGCHANGE`, `WM_DISPLAYCHANGE`, `WM_THEMECHANGED`, and `TaskbarCreated` broadcasts.
- A 250 ms identity poll continues to detect Explorer generation changes and retires the observer, exactly as before.
- A 400 ms settle timer, restarted by every raw signal, emits one geometry invalidation after Explorer finishes animating so the host re-discovers the final layout.

The wire contract, protocol version, host lifecycle handling, job-object containment, and authenticated pipe handshake are unchanged. The existing invalidation kinds keep their generic meaning: structure changed, geometry changed, and visibility changed. The observer never reports a process or window identity, and it never classifies a signal as owned by the host because the process-scoped hook cannot observe the host's own child window.

The host keeps its one-shot UI Automation scans and its 5-second watchdog as the last resort.

## Consequences

- QuickPods no longer appears as an assistive-technology client to browsers or other UI Automation providers.
- The observer only sees HWND-level changes inside Explorer. Windows 11 taskbar buttons are XAML elements without HWNDs, so button additions, pin changes, and alignment changes are inferred from the shell hook, registry notifications, and the settle timer rather than observed directly.
- The observer no longer needs the Windows Desktop framework and ships as a plain .NET application.
- The owned-source classification in the protocol is retained for compatibility but is no longer emitted.
- Registry key names may change between Windows releases; because every watch is optional, such a change reduces signal fidelity without breaking the observer.

## Verification

Changes to the observer must cover:

1. `chrome://accessibility` shows no `quickpods.taskbarobserver.exe` client after Chrome is restarted with QuickPods running;
2. taskbar button additions and removals, pin changes, alignment changes, auto-hide changes, notification-area changes, display and DPI changes, and theme changes re-place the surface within about one second;
3. bounded invalidation rate while a window title changes continuously and during auto-hide animations;
4. Explorer restart and generation replacement, including retirement of every old observer;
5. stable long-lived observer USER and GDI resources.
