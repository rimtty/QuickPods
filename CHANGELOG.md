# Changelog

All notable changes to QuickPods will be documented in this file. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and released versions use [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Added manual latest-release checks from the notification-area menu and Settings.
- Added an opt-in startup update check limited to once every 24 hours.

### Changed

- Reorganized the portable package around an obvious root `QuickPods.exe` launcher.
- Moved the application runtime and helper executables into an internal `app` directory.
- Collected license and third-party notice files in a dedicated `licenses` directory.
- Added a bilingual package README with startup, security, update, and internal-file guidance.
- Clarified the existing tray refresh action as a device-status refresh.

## [0.1.1] - 2026-08-08

### Changed

- Made the self-contained Windows x64 ZIP the official public distribution format.
- Stopped publishing unsigned MSI artifacts while code signing is unavailable.
- Added portable installation, update, removal, checksum, and unsigned-app guidance.

## [0.1.0] - 2026-08-08

### Added

- English and Japanese localization with an in-app language selector.
- Paired Bluetooth audio selection, guarded connect/disconnect operations, and default-output switching.
- Taskbar volume controls, notification-area fallback, settings, startup registration, diagnostics, and per-user MSI packaging.

### Changed

- Prepared documentation, contribution guidance, CI templates, and packaging metadata for a public open-source repository.

### Security

- Kept device mutation fail-closed and excluded raw device and account identifiers from normal diagnostics.

[Unreleased]: https://github.com/rimtty/QuickPods/compare/v0.1.1...HEAD
[0.1.1]: https://github.com/rimtty/QuickPods/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/rimtty/QuickPods/releases/tag/v0.1.0
