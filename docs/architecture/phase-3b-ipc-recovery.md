# Phase 3B — IPC and taskbar-host recovery

## Status

Complete on `main`. The app/host IPC, bounded process recovery, ADR-0001 Explorer-generation observer boundary, and complete Phase 3B host command path are implemented and live-tested. The product local-console Explorer-restart Gate recovered one native surface in 7.683 seconds, exchanged the Observer generation without duplicate surfaces, preserved the long-lived App/Host USER/GDI boundary, and passed post-recovery wheel/mute/flyout/slider operations. Issues #34 and #18 are closed. The dedicated center-aligned NoFit reproduction formerly tracked by Issue #33 was removed from the release gate by product decision on 2026-08-07; existing fail-closed routing and automated invariants remain unchanged.

## Process boundary

`QuickPods.App.exe` owns Core Audio and is the only process allowed to mutate the default endpoint volume. `QuickPods.TaskbarHost.exe` owns native/floating HWNDs and turns pointer input into protocol interactions. The host never loads the Windows audio adapter.

The app creates a random per-launch named pipe with `PipeOptions.CurrentUserOnly` before starting the host. The unlogged pipe token and app PID are passed only to that child. The host must connect within eight seconds and must receive a valid full `HostStateEnvelope` before it discovers or displays a surface.

Messages use one bounded JSON object per line. Constructors validate `ProtocolVersion`, sequence, interaction kind, and volume range. Each receiver applies a `MonotonicSequenceGate`; stale or duplicate messages cannot update state. A reconnect starts with the app's latest full snapshot and a fresh interaction gate.

## Lifecycle and recovery

The app supervisor starts one host, observes pipe/process termination, and restarts with 1/2/4-second bounded backoff. A connection must remain stable for ten seconds before the consecutive-failure counter resets. Three short failures disable hosting for the session without terminating the app. Closing the app closes the pipe and then forcibly retires only its owned child if the child does not exit within two seconds.

The host performs hidden-first surface creation. Native and floating surfaces are mutually exclusive. Window invalidation hides and destroys the active surface before a replacement discovery begins. A five-second low-frequency Watchdog performs one-shot discovery off the HWND owner thread and only rebuilds when the route, Explorer PID, taskbar HWND, bounds, work area, or DPI changes. Discovery attempts carry an in-process epoch so an invalidation cannot promote a result calculated from retired taskbar geometry.

The Watchdog does not register a repeating UI Automation handler. One short-lived `QuickPods.TaskbarObserver.exe` owns the repeating UIA subscription for one Explorer generation on a dedicated MTA. A random `CurrentUserOnly` pipe carries only sanitized invalidation kind, source classification, subscription epoch, opaque generation ordinal, and monotonic sequence. Raw Explorer HWND/PID values never cross that boundary. The observer is assigned to a kill-on-close Job Object, and a host or provider failure retires it before fresh discovery creates another generation.

`QuickPods.TaskbarHost.exe` also owns one hidden, top-level, non-activating control HWND for the Shell's registered `TaskbarCreated` broadcast. On receipt, the host destroys any display surface, retires the observer, advances the discovery epoch, and rediscovers hidden-first. The control HWND is not message-only because Shell broadcasts are not delivered to message-only windows.

## Input path

The host normalizes interaction sequences across native/floating HWND recreation. Preview and final volume interactions cross the pipe and call the existing `AudioController`; optimistic coalescing and final flush behavior remain owned by Phase 2. A left click on the rendered speaker icon emits `ToggleMute`, a left click on strip whitespace emits `OpenAudioFlyout`, and `WM_RBUTTONUP` emits `OpenContextMenu`. These hit rules and sequences are shared by Native and Floating surfaces. Audio callbacks publish the resulting full state snapshot back to the host.

Flyout and context-menu requests currently activate the diagnostic main window. Bluetooth-specific flyout contents remain Phase 4 and tray lifetime remains Phase 5.

## Failure behavior

- malformed, oversized, wrong-version, stale, or duplicate messages are rejected;
- parent/pipe loss terminates the host;
- observer loss or a sanitized external/unknown invalidation retires the surface and observer generation before rediscovery;
- `TaskbarCreated` invalidates in-flight discovery results before replacement placement can be promoted;
- missing/incomplete placement evidence renders no surface;
- unsupported left alignment remains `UnsupportedAlignment -> Hidden`;
- repeated host failures leave the app alive with no host and no surface;
- Bluetooth, packaging, auto-start, and installer behavior are not introduced in this phase.
