# Implementation history and architecture

## Phase history

| Phase | Delivered result | Key evidence |
|---|---|---|
| 0A | Core Audio volume/mute read-write and notifications | Windows UI parity, latency, 1,000-change restoration |
| 0B | Bluetooth KS feasibility and isolated worker protocol | AirPods connect/disconnect 5/5, false-success fix, selected-device boundary |
| 0C–0E | taskbar placement, native host, Start/Search continuity | PopupPreserved, fail-closed fallback, Explorer spike, retained Start proof |
| 1 | production solution and OS boundary separation | App/Core/Windows/Contracts/Infrastructure projects |
| 2 | real Audio MVP | default endpoint, volume, mute, external change, endpoint A→B→A |
| 3A/3B | taskbar product host, IPC, supervision, recovery | versioned named pipe, sequence/generation rejection, helper lifetime |
| 4A/4B | Bluetooth product catalog and mutation/default output | physical AirPods aggregation, connect/disconnect/default, external-state following |
| 5A/5B | tray lifetime, settings, startup, resilience/accessibility | explicit exit, JSON/HKCU persistence, lifecycle recovery, sanitized diagnostics |
| 5C | final Fluent taskbar/flyout/settings UI | user-approved visual.75–86 sequence and DPI 100–350% |
| 6A | release hardening | deterministic RC, dependency audit, resource/Observer diagnostics |
| 6B | distribution | self-contained ZIP, per-user WiX MSI, uninstall/update/signing contracts |

## Production process boundaries

```text
QuickPods.exe
├─ WPF flyout and settings window
├─ notification-area icon and explicit lifetime policy
├─ Core Audio controller and product state
├─ Bluetooth catalog/operation coordinator
├─ QuickPods.TaskbarHost.exe
│  └─ native taskbar surface, rendering, input, versioned IPC
├─ QuickPods.TaskbarObserver.exe
│  └─ Explorer-generation UI Automation observation; replaced per generation
└─ QuickPods.BluetoothWorker.exe
   └─ bounded capability/mutation call; kill-on-close and authenticated request
```

Only the App owns startup, settings, logs, and child lifetime. Helper executables never self-register and must not outlive the App except for the bounded shutdown interval.

## Important decisions

### Core Audio

- Use public Core Audio interfaces for endpoint state, volume/mute, and notifications.
- Keep default-endpoint mutation behind an isolated `IPolicyConfig` adapter because Microsoft does not publish a general desktop setter.
- Confirm state by notification/read-back; never claim success from a request return value alone.
- Bind callbacks to endpoint generations and reject stale events.

### Bluetooth

- Aggregate A2DP render and Hands-Free endpoints by a private, opaque physical-device key.
- Show one row per physical paired audio device; never expose the key.
- Selection and refresh are read-only. Only an explicit primary action may mutate OS state.
- Run KS calls in `QuickPods.BluetoothWorker.exe`, authenticated to the parent and bounded by timeout/job object.
- Connect only the unique stereo render candidate, then optionally set Console/Multimedia default. Do not change Communications by default.
- Disconnect only endpoints proven to belong to the selected Container, and require a stable unplugged/not-present window.
- On unsupported driver capability, partial default switch, stale generation, or RDP ownership uncertainty, fail honestly and offer Windows Settings.

### Taskbar

- Windows 11 center alignment is supported. Left alignment is explicitly unsupported and routes to Hidden/tray-only.
- `PopupPreserved` deliberately retains `WS_POPUP` after attachment; converting to `WS_CHILD` allows Start/Search composition to overwrite the surface.
- Safety is fail-closed: incomplete identity, geometry, DPI, monitor, DWM, obstacle, or generation evidence hides or uses the documented fallback.
- Start retention is not a boolean. A private proof can only be minted from a complete same-generation anchor under the narrow sole-`StartButtonMissing` path.
- The taskbar surface publishes its real physical-pixel anchor and DPI to the App, preventing startup-before-hover and live-DPI flyout misplacement.
- Flyout is topmost without stealing focus and dismisses when the pointer leaves both taskbar surface and popup.

### Settings and lifetime

- Settings are atomically stored at `%LocalAppData%\QuickPods\settings.json`.
- The only startup value is current-user `QuickPods = "<absolute QuickPods.exe>" --background`.
- Window close hides; tray `終了` is the only explicit full shutdown path.
- Normal logging is structured and sanitized. Diagnostic copy contains product state, not raw OS identifiers.

### Distribution

- Shipped binaries are x64 and self-contained; users do not install .NET separately.
- MSI installs per user under `PerUserProgramFilesFolder\QuickPods`, creates current-user integration only, and requires no elevation.
- Major upgrade preserves user settings/startup intent. Complete uninstall removes the owned startup value and product files but keeps settings/logs.
- Normal CI artifacts remain unsigned. Final publication requires a protected signing workflow, timestamp, Authenticode `Valid`, and matching manifests/hashes.

## Design and evidence authority

When sources disagree, use this precedence:

1. current code and automated contracts on `main`;
2. current architecture documents in `docs/architecture/`;
3. current validation and completion audit in `docs/validation/`;
4. `QuickPods UI Mockup v2` and the Bluetooth selector specification for user-facing intent;
5. v1 mockup and Phase 0 records as historical context only.
