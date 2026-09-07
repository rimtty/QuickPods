# Documentation

QuickPods documentation is organized by audience. The current code and automated tests are authoritative when older design history differs from these pages.

## Users

- [User guide](user-guide.md) — daily use, settings, language, and startup behavior
- [Troubleshooting](troubleshooting.md) — common audio, Bluetooth, taskbar, and startup problems
- [Privacy](privacy.md) — locally stored data and safe diagnostic sharing
- [Compatibility](release/compatibility.md) — supported Windows, display, and Bluetooth boundaries
- [Known limitations](release/known-limitations.md) — behavior that is intentionally unsupported or driver-dependent

## Contributors and maintainers

- [Development](development.md) — clone, restore, build, test, run, and package
- [Architecture](architecture/README.md) — process boundaries, Windows integration, and fail-closed design
- [ADR-0001](architecture/adr-0001-uia-watcher-process-boundary.md) — taskbar observer process boundary
- [ADR-0002](architecture/adr-0002-shell-signal-observer.md) — shell-signal taskbar observer without UI Automation events
- [Release guide](release/README.md) — versioning, packaging, signing, and publication checklist
- [Public repository checklist](release/public-repository-checklist.md) — GitHub settings to review before changing visibility
- [Installer guide](../installer/README.md) — WiX per-user MSI details
- [Technical spikes](../spikes/README.md) — historical feasibility projects retained for engineering reference

## Documentation policy

- Keep user-facing documentation current with `main`.
- Prefer stable behavior and public interfaces over phase, branch, or acceptance-gate history.
- Do not commit temporary review screenshots, raw validation logs, machine-specific paths, account information, or device identifiers.
- Place durable architecture decisions under `docs/architecture/` and release operations under `docs/release/`.
