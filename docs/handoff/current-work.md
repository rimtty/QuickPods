# Current work and GitHub state

## Integrated work

The following important branches are already integrated before the handoff:

- Phase 0–6A product and hardening work
- Phase 5C production UI fidelity
- Phase 6B self-contained RC and per-user WiX MSI implementation
- PR #70 local-console/DPI/resource/settings evidence
- PR #59 retained Start provenance and race hardening
- visual.86 startup-before-hover taskbar anchor fix

Issue #15 completed with PR #59. The additional physical negative layout repetition is not required. Issue #19 was closed `not planned` because AC-005 already has real endpoint A→B→A evidence and deterministic guarded-restoration coverage.

## Remaining release work

| Issue | Why it remains | Next acceptable evidence | RDP status |
|---|---|---|---|
| [#43](https://github.com/rimtty/QuickPods/issues/43) | final session/theme acceptance | local sign-out/in or reboot with auto-start ON and no product window; System/Dark/Light and High Contrast visual check | Leave open |
| [#34](https://github.com/rimtty/QuickPods/issues/34) | AC-018 product Explorer recovery | bounded real Explorer restart, one surface, recovery within 10 seconds, controls usable, no residue/Warning/Error | Leave open |
| [#18](https://github.com/rimtty/QuickPods/issues/18) | AC-021 Explorer-generation resource proof | combine with #34; verify old Observer exits and long-lived App/Host USER/GDI do not grow across bounded generations | Leave open |
| [#50](https://github.com/rimtty/QuickPods/issues/50) | AC-022/027 distribution closure | disposable standard-user install/launch/upgrade/uninstall/residue run; provision certificate and validate signed final MSI | Leave open |
| [#2](https://github.com/rimtty/QuickPods/issues/2) | 0.1.0 roll-up | close only after all AC rows are Proven and final release artifacts are accepted | Leave open |

The completion audit remains 23/27. The four partial rows map exactly to two work packages:

- AC-018 and AC-021: Issues #34 and #18
- AC-022 and AC-027: Issue #50

Issue #43 is an explicit product acceptance gate even though its persistence/keyboard/DPI portions already support Proven AC rows.

## Recommended next local-console session

1. Clone `main` on the physical Windows 11 development host and run the CI-equivalent baseline.
2. Build a fresh self-contained candidate from that exact commit.
3. Complete #43 theme/High Contrast and real-login auto-start checks; restore auto-start OFF afterward.
4. Run one small, bounded Explorer batch that gathers both #34 functional recovery and #18 process/resource evidence. Do not repeat the former 10-cycle stress matrix.
5. Use a disposable standard-user profile/VM for #50 MSI lifecycle. Preserve settings only where the uninstall contract requires it.
6. When the certificate is available, run the protected signed-release workflow and verify Authenticode and hashes.
7. Update the completion audit to 27/27, produce final RC notes/tag, then close #50 and Roadmap #2.

## Already accepted; do not reopen without a defect

- dedicated center-aligned NoFit repetition (#33)
- Bluetooth catalog and operation Gates (#38/#41)
- Bluetooth 50-cycle durability repetition
- 24-hour resource duration; the approved Gate was 2.01 hours / 1,391 samples (#48)
- generic guarded-mutation multi-output repetition (#19)
- extra retained Start physical negative layout repetition (#15)

If a new defect contradicts this evidence, open a new focused defect tied to the exact candidate and environment rather than reopening broad historical Gate matrices.
