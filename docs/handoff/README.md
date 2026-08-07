# QuickPods developer handoff

This directory is the entry point for continuing QuickPods development from another Windows PC. It describes only repository-backed state; ignored `artifacts/`, local logs, registry values, and running processes are never prerequisites.

## Snapshot

- Date: 2026-08-07
- Product: QuickPods 0.1.0
- Source of truth: `main`
- Platform: Windows 11 x64, .NET 10, WPF + Win32
- Automated baseline: 398 tests, Release build with 0 warnings and 0 errors
- Acceptance audit: 23/27 Proven
- Distribution: self-contained portable ZIP and elevation-free per-user WiX 6.0.2 MSI
- Final artifacts: unsigned until the code-signing certificate is provisioned

All implemented source through Phase 6B, Phase 5C UI fidelity, local-console evidence, visual.86 taskbar anchoring, and retained Start hardening is integrated into `main`. Merging implementation does not mark QuickPods 0.1.0 released; the remaining physical/session/distribution gates stay in GitHub Issues.

## Read in this order

1. [Development environment](development-environment.md) — clone, restore, build, test, run, and package.
2. [Current work and GitHub state](current-work.md) — remaining Issues and the next safe sequence.
3. [Implementation history and architecture](implementation-history.md) — why the current process and OS boundaries exist.
4. [Assets and evidence index](assets-and-evidence.md) — authoritative mockups, branding, specifications, and validation records.
5. [Completion audit](../validation/release-0.1.0/completion-audit.md) — AC-001 through AC-027.

## Non-negotiable handoff rules

- Do not infer local-console rendering, taskbar geometry, Explorer recovery, physical Bluetooth ownership, or login behavior from RDP.
- Do not register TaskbarHost, TaskbarObserver, or BluetoothWorker for startup. Only quoted `QuickPods.exe --background` may appear in the current-user Run key.
- Do not log or display raw Container, Endpoint, PnP, MAC, or account identifiers.
- Do not broaden retained Start continuity beyond its explicit provenance and fail-closed checks.
- Do not commit PFX/P12/PEM/private keys, environment secrets, runtime logs, or generated `artifacts/`.
- Do not require end users to install .NET Desktop Runtime; shipped RC/MSI payloads are self-contained.

If a new host cannot reproduce the baseline, open a focused Issue with the exact commit, Windows build, SDK version, command, sanitized output, and whether the session is local or RDP.
