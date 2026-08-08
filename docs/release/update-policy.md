# Update policy

QuickPods currently uses manual portable ZIP updates.

1. Exit QuickPods from the notification-area menu.
2. Download the ZIP only from the official GitHub Release and verify its published SHA-256 checksum.
3. Extract the new `QuickPods` folder and replace the previous application files in the same permanent location.
4. Start QuickPods and confirm settings, startup preference, and device behavior.

User settings and logs under `%LocalAppData%\QuickPods` are separate from the application folder and are preserved. Keeping the same application path also keeps an existing start-at-sign-in entry valid.

Maintainers must retain the previous supported ZIP for rollback testing. QuickPods does not download or execute updates automatically.
