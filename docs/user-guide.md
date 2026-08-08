# User guide

## Start QuickPods

Extract the official portable ZIP to a permanent folder and launch the root `QuickPods.exe` from the extracted `QuickPods` folder. The internal `app` directory contains runtime files and helper processes; do not launch or move them manually. License and third-party notice files are collected under `licenses`. QuickPods stays in the notification area after its flyout closes.

The taskbar surface appears immediately left of the notification area when QuickPods can verify a safe empty region on a horizontal Windows 11 taskbar. This experimental placement supports center- and left-aligned taskbar buttons. If no safe region is available, use the QuickPods notification-area icon instead.

## Open the audio flyout

Click the QuickPods taskbar surface or notification-area icon. The flyout contains:

- paired Bluetooth audio devices;
- current connection and default-output state;
- the master volume slider and mute button;
- a primary Connect or Disconnect action;
- links to Windows Sound and Bluetooth settings;
- a Settings button and refresh button.

## Select and connect a Bluetooth device

1. Select a paired Bluetooth audio device.
2. Review the current state shown on the same row.
3. Choose **Connect**.
4. Wait for QuickPods to confirm the Windows device state.

When enabled in Settings, QuickPods makes the connected stereo endpoint the Console and Multimedia default output. It does not change the Communications default by design. QuickPods may disconnect another connected Bluetooth audio device so that the active audio connection converges on the selected device.

Direct connection and disconnection depend on capabilities exposed by the device's Windows driver. If a safe operation cannot be proven, QuickPods opens or offers the appropriate Windows Settings page instead.

## Selection at startup

On the first device refresh after launch, QuickPods selects:

1. a connected device that is already the default output;
2. otherwise, the first connected device;
3. otherwise, the first visible device.

Later refreshes preserve the current selection and do not reorder the list simply because a row was clicked.

## Volume and mute

Use the taskbar surface or flyout slider to change the master volume of the current Windows default output. Mouse-wheel changes use the step configured in Settings. The mute button always reflects the current endpoint state.

When Windows changes the default output, QuickPods rebinds to the new endpoint and displays its volume and mute state.

## Settings

Open Settings from the flyout gear button or the notification-area menu.

- **Taskbar display** — automatic safe placement or notification-area-only mode.
- **Theme** — follow Windows, dark, or light.
- **Language** — follow the Windows UI language, English, or Japanese. System mode uses Japanese only for Japanese Windows and falls back to English for every other language.
- **Volume adjustment step** — percentage changed by each mouse-wheel step.
- **Set connected device as the default audio device** — update Console and Multimedia roles after connection.
- **Confirm before disconnecting Bluetooth** — require confirmation for disconnect actions.
- **Start QuickPods when signing in to Windows** — create or remove the current-user startup entry.

Settings are saved automatically. **Reset to defaults** restores editable preferences, **Restart** restarts QuickPods after orderly shutdown, and **Close** closes only the Settings window.

## Exit QuickPods

Closing the flyout or Settings window does not terminate the app. Use **Exit QuickPods** from the notification-area menu for a full shutdown.

## Stored data

QuickPods stores settings and logs under `%LocalAppData%\QuickPods`. To remove the portable version, first disable start at sign-in, exit QuickPods, and delete the extracted application folder. Settings and logs are intentionally preserved unless `%LocalAppData%\QuickPods` is deleted separately. See [Privacy](privacy.md) and the [uninstall policy](release/uninstall-policy.md).
