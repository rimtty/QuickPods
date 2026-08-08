# Troubleshooting

## The taskbar surface is not visible

- Confirm that the horizontal Windows 11 taskbar has enough free space immediately left of the notification area.
- Check QuickPods Settings and use **Automatic** taskbar display.
- Look for QuickPods in the notification area; the app intentionally falls back there when safe taskbar placement cannot be verified.
- Restart QuickPods after Explorer, display, or DPI changes if the surface does not recover.

Both center- and left-aligned taskbar buttons are supported by the experimental placement.

## A Bluetooth device is missing

1. Confirm that the device is paired in Windows Settings.
2. Open the QuickPods flyout and choose Refresh.
3. Confirm that the device exposes an audio render or capture endpoint.
4. Disconnect Remote Desktop and test from the local console; QuickPods does not treat Remote Audio as local Bluetooth inventory.

## Connect or Disconnect is unavailable

Direct operations require driver capabilities that QuickPods can verify for the selected physical device. Some devices or drivers do not expose safe per-device operations. Use the Bluetooth Settings link when QuickPods reports the device as unsupported or unavailable.

## A connection times out

Bluetooth profile activation can take several seconds, especially with multi-host devices that are also paired to a phone or another computer. QuickPods does not retry automatically because an unbounded retry could mutate the wrong state later. Confirm the device is reachable and retry once manually.

## The wrong output remains selected

- Enable **Set connected device as the default audio device** in Settings.
- Confirm that the stereo render endpoint becomes active.
- Open Windows Sound settings from the flyout and check the Console/Multimedia default.

QuickPods intentionally does not change the Communications default output.

## Volume does not match Windows

QuickPods controls the current default render endpoint. If Windows or another application changes the default output, wait briefly for the endpoint notification. Refresh or restart QuickPods if the endpoint remains stale.

## QuickPods starts twice or will not launch

Only one instance can run in a user session. Exit the existing notification-area instance before starting a build from another directory. If a previous process is stuck, use Task Manager to confirm that `QuickPods.exe` and its owned helper processes have exited.

## Collect diagnostics

Open QuickPods Settings and choose **Copy diagnostics** or **Open logs**. Review the content using the [privacy checklist](privacy.md) before attaching it to a public bug report.
