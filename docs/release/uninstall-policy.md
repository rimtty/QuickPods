# Uninstall policy

To remove the portable release:

1. Turn off **Start QuickPods when signing in to Windows** in Settings.
2. Exit QuickPods from the notification-area menu.
3. Delete the extracted QuickPods application folder.

Uninstall intentionally preserves:

- `%LocalAppData%\QuickPods\settings.json`;
- quarantined settings recovery files;
- `%LocalAppData%\QuickPods\logs`.

This preserves user preferences across reinstall and supports post-removal diagnostics. To remove all local data, delete `%LocalAppData%\QuickPods` separately after exiting the application. Review the directory first because deletion cannot be undone.
