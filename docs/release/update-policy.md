# Update policy

QuickPods currently uses manual MSI updates.

1. Exit QuickPods from the notification-area menu.
2. Verify the new installer's source, Authenticode signature, and published SHA-256 checksum.
3. Run the newer MSI as the same Windows user.
4. Start QuickPods and confirm settings, startup preference, and device behavior.

The WiX package performs a per-user major upgrade in the existing installation directory. User settings and logs under `%LocalAppData%\QuickPods` are preserved. Version downgrades are rejected.

Maintainers must retain the previous supported installer for rollback testing. QuickPods does not download or execute updates automatically.
