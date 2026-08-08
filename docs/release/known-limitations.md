# Known limitations

- The experimental embedded taskbar surface does not reserve shell space. If taskbar buttons leave no verified gap immediately left of the notification area, QuickPods uses the notification-area fallback.
- Direct Bluetooth connect and disconnect depend on driver capability. Unsupported devices use Windows Settings.
- QuickPods does not enumerate or mutate local Bluetooth devices while running through Remote Desktop.
- Connecting a multi-host headset can time out while the device is attached to another phone or computer. QuickPods does not retry automatically.
- Default-output changes target Console and Multimedia roles. The Communications default is intentionally left unchanged.
- The taskbar integration uses Windows shell behavior that is not a supported public taskbar extension API. QuickPods hides the surface whenever its placement proof is incomplete.
- Third-party taskbar replacements and extensive shell modifications are not supported.
- Automatic application updates are not implemented.
- Public portable releases are unsigned and can trigger Microsoft Defender SmartScreen warnings. Verify the GitHub Release source and published SHA-256 checksum before running them.
- MSI installers are not publicly distributed until a valid timestamped Authenticode signing option is available.
- Uninstall preserves `%LocalAppData%\QuickPods` settings and logs by design.
