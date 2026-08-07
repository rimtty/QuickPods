# Current work and GitHub state

## Integrated and accepted work

The following work is integrated into `main` or accepted against the current `main` candidate:

- Phase 0–6B product, hardening, self-contained RC, and per-user WiX MSI work
- Phase 5C production UI fidelity and retained Start provenance/race hardening
- local-console DPI, keyboard, tray, settings, explicit-exit, and normal Light/Dark/System theme checks
- taskbar theme propagation and crisp opaque flyout composition fixes (#84/#86)
- real sign-out/sign-in auto-start with no product window (#43)
- bounded real Explorer restart, one-surface recovery, control usability, and generation/resource proof (#34/#18)
- disposable standard-user install, resident major upgrade, resident uninstall, retention, and unrelated Run-value preservation (#50)

The [completion audit](../validation/release-0.1.0/completion-audit.md) is 27/27 Proven for the explicitly approved non-signing scope. Additional High Contrast visual testing was removed from the final Gate by the repository owner; the implemented system-color behavior remains intact and is not claimed as newly proven.

## GitHub state

| Issue | Resolution |
|---|---|
| [#43](https://github.com/rimtty/QuickPods/issues/43) | Closed after real-login background auto-start and normal theme acceptance |
| [#34](https://github.com/rimtty/QuickPods/issues/34) | Closed after 7.683-second product Explorer recovery and post-recovery interaction checks |
| [#18](https://github.com/rimtty/QuickPods/issues/18) | Closed after Observer generation exchange and stable long-lived App/Host USER/GDI evidence |
| [#50](https://github.com/rimtty/QuickPods/issues/50) | Non-signing lifecycle accepted; closes with the final documentation audit |
| [#2](https://github.com/rimtty/QuickPods/issues/2) | Non-signing 0.1.0 roll-up closes after the final documentation audit is merged |

## Deferred publication boundary

Code-signing certificate provisioning, protected-environment secret registration, timestamped Authenticode `Valid`, and publication of a signed MSI are intentionally deferred by the repository owner. Existing signing tooling remains fail-closed and no private key belongs in the repository. If signed distribution is resumed, use the protected release workflow and verify the final manifest and checksum; do not reinterpret unsigned acceptance evidence as a signature result.

## Recommended next sequence

1. Merge the final documentation audit after Release build, all tests, formatting, and documentation checks pass.
2. Close #50 and Roadmap #2 for the approved non-signing scope.
3. Produce a versioned unsigned RC only when the owner wants an installable test release, clearly labeling `NotSigned`.
4. Treat any future signed production publication as a separately authorized Gate with a real certificate and timestamp.

## Already accepted; do not reopen without a defect

- dedicated center-aligned NoFit repetition (#33)
- Bluetooth catalog and operation Gates (#38/#41)
- Bluetooth 50-cycle durability repetition
- 24-hour resource duration; the approved Gate was 2.01 hours / 1,391 samples (#48)
- generic guarded-mutation multi-output repetition (#19)
- extra retained Start physical negative layout repetition (#15)

If a new defect contradicts this evidence, open a new focused defect tied to the exact candidate and environment rather than reopening broad historical Gate matrices.
