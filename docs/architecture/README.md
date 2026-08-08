# Architecture

QuickPods separates product logic, Windows adapters, and failure-prone OS integrations so that uncertainty fails closed instead of producing incorrect device mutations or unsafe taskbar placement.

## Process model

```text
QuickPods.exe (portable launcher)
└─ app\QuickPods.exe
   ├─ WPF flyout, Settings, notification-area icon, and application lifetime
   ├─ Core Audio and Bluetooth product coordination
   ├─ QuickPods.TaskbarHost.exe
   │  └─ taskbar discovery, native surface, rendering, input, and versioned IPC
   ├─ QuickPods.TaskbarObserver.exe
   │  └─ short-lived UI Automation subscription for one Explorer generation
   └─ QuickPods.BluetoothWorker.exe
      └─ bounded Bluetooth capability or mutation call
```

The app owns settings, logs, startup registration, and child-process lifetime. Helper executables never register themselves at sign-in and are placed in kill-on-close job objects where appropriate.

## Project boundaries

| Project | Responsibility |
|---|---|
| `QuickPods.App` | WPF UI, tray integration, settings UI, composition root |
| `QuickPods.Launcher` | dependency-free native launcher for the portable package root |
| `QuickPods.Core` | product state and use-case coordination |
| `QuickPods.Presentation` | language-independent presentation and localization |
| `QuickPods.Contracts` | versioned process contracts and shared glyph definitions |
| `QuickPods.Infrastructure` | settings, logging, update metadata retrieval, single-instance, and host supervision |
| `QuickPods.Windows` | Core Audio, Bluetooth, registry, and Windows Settings adapters |
| `QuickPods.TaskbarHost` | out-of-process taskbar surface and placement policy |
| `QuickPods.TaskbarObserver` | Explorer-generation UI Automation observation |
| `QuickPods.BluetoothWorker` | isolated Bluetooth driver call boundary |

## Core Audio

QuickPods uses public Core Audio interfaces for endpoint observation, master volume, mute, and notifications. Default-output mutation is isolated behind a Windows policy adapter because Microsoft does not publish a general desktop setter for this operation. Every requested change is confirmed through notification and read-back; a request result alone is never treated as success.

Endpoint callbacks are bound to a generation so stale events from a previous default device cannot overwrite current state.

## Bluetooth

Windows can expose several endpoints and profiles for one physical Bluetooth device. QuickPods aggregates them behind a private opaque key and displays one row per physical paired audio device.

Discovery and selection are read-only. A mutation occurs only after explicit user action and only when QuickPods can prove the target, ownership, and driver capability. Potentially blocking kernel-streaming calls run in a bounded worker process. The app confirms the resulting device and endpoint state after the worker returns.

Unsupported capability, partial state, stale generation, timeout, and RDP ownership uncertainty produce an honest failure or Windows Settings fallback.

## Taskbar integration

QuickPods uses an experimental Win32 surface in empty space on the center-aligned Windows 11 taskbar. It does not reserve taskbar space or move shell controls. Placement is permitted only after taskbar identity, monitor, DPI, landmarks, obstacles, geometry, and Explorer generation are verified.

Incomplete evidence hides the embedded surface and leaves notification-area access available. A dedicated short-lived observer contains UI Automation provider lifetime across Explorer restarts; see [ADR-0001](adr-0001-uia-watcher-process-boundary.md).

## Settings and diagnostics

Settings use atomic JSON replacement under `%LocalAppData%\QuickPods`. A malformed file is quarantined before safe defaults are written. Startup registration uses one current-user Run value pointing to the internal application executable with `--background`; the root launcher is needed only for direct user startup and installer cleanup forwarding.

Structured diagnostics contain state classifications and counts, not raw device or account identifiers. See [Privacy](../privacy.md).

Update availability is checked through the public GitHub latest-release endpoint behind `IApplicationUpdateChecker`. Requests run asynchronously, stable-release metadata is validated against the official repository URL, and successful check metadata is stored with the user settings. Automatic checks are opt-in and throttled to once every 24 hours. Downloading and applying packages remains a separate manual operation.

## Distribution

Release payloads are x64 and self-contained. The portable root contains one obvious launcher, keeps runtime files under `app`, and collects legal notices under `licenses`. The MSI installs the same layout per user without elevation, preserves settings during upgrade and uninstall, and removes the startup value owned by QuickPods on complete uninstall. Release signing remains fail-closed when MSI distribution is enabled.
