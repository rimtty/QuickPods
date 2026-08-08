# Uninstall policy

The per-user MSI removes:

- installed QuickPods binaries;
- the Start menu shortcut;
- installer registration;
- the current-user startup value owned by QuickPods.

Uninstall intentionally preserves:

- `%LocalAppData%\QuickPods\settings.json`;
- quarantined settings recovery files;
- `%LocalAppData%\QuickPods\logs`.

This preserves user preferences across reinstall and supports post-uninstall diagnostics. To remove all local data, exit QuickPods, uninstall the application, and manually delete `%LocalAppData%\QuickPods`. Review the directory first because deletion cannot be undone.
