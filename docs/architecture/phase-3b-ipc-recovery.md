# Phase 3B — IPC and taskbar-host recovery

## Status

In progress on `codex/phase-3b-ipc-recovery` under Issue #34. The app/host IPC and process-recovery checkpoint is implemented and live-tested. The Explorer-generation observer boundary from ADR-0001 and the center-aligned NoFit decision in Issue #33 remain pending.

## Process boundary

`QuickPods.App.exe` owns Core Audio and is the only process allowed to mutate the default endpoint volume. `QuickPods.TaskbarHost.exe` owns native/floating HWNDs and turns pointer input into protocol interactions. The host never loads the Windows audio adapter.

The app creates a random per-launch named pipe with `PipeOptions.CurrentUserOnly` before starting the host. The unlogged pipe token and app PID are passed only to that child. The host must connect within eight seconds and must receive a valid full `HostStateEnvelope` before it discovers or displays a surface.

Messages use one bounded JSON object per line. Constructors validate `ProtocolVersion`, sequence, interaction kind, and volume range. Each receiver applies a `MonotonicSequenceGate`; stale or duplicate messages cannot update state. A reconnect starts with the app's latest full snapshot and a fresh interaction gate.

## Lifecycle and recovery

The app supervisor starts one host, observes pipe/process termination, and restarts with 1/2/4-second bounded backoff. A connection must remain stable for ten seconds before the consecutive-failure counter resets. Three short failures disable hosting for the session without terminating the app. Closing the app closes the pipe and then forcibly retires only its owned child if the child does not exit within two seconds.

The host performs hidden-first surface creation. Native and floating surfaces are mutually exclusive. Window invalidation hides and destroys the active surface before a replacement discovery begins. A five-second low-frequency Watchdog performs one-shot discovery off the HWND owner thread and only rebuilds when the route, Explorer PID, taskbar HWND, bounds, work area, or DPI changes.

This Watchdog does not register a repeating UI Automation handler. ADR-0001 still requires repeating UIA subscriptions to live in the short-lived `QuickPods.TaskbarObserver.exe`; that helper is the next Phase 3B implementation slice.

## Input path

The host normalizes interaction sequences across native/floating HWND recreation. Preview and final volume interactions cross the pipe and call the existing `AudioController`; optimistic coalescing and final flush behavior remain owned by Phase 2. Audio callbacks publish the resulting full state snapshot back to the host.

Flyout and context-menu requests currently activate the diagnostic main window. Bluetooth-specific flyout contents remain Phase 4 and tray lifetime remains Phase 5.

## Failure behavior

- malformed, oversized, wrong-version, stale, or duplicate messages are rejected;
- parent/pipe loss terminates the host;
- missing/incomplete placement evidence renders no surface;
- unsupported left alignment remains `UnsupportedAlignment -> Hidden`;
- repeated host failures leave the app alive with no host and no surface;
- Bluetooth, packaging, auto-start, and installer behavior are not introduced in this phase.
