# Core Audio spike test results

## Automated validation

Run on 2026-08-05 from the repository root.

| Check | Result |
|---|---|
| Locked NuGet restore | Pass |
| Release x64 build with warnings as errors | Pass; 0 warnings, 0 errors |
| Repository smoke tests | Pass; 1/1 |
| Core Audio unit tests | Pass; 41/41 |
| Format verification at warning severity | Pass |

The tests cover percent/scalar clamping, self-event classification, stale generations, nearest-rank percentile calculation, plateau-aware resource-growth detection, bounded latest-value mailboxes, 10,000-callback coalescing, recovery after a notification sink throws, ABI header sizes, safe CLI bounds, and the mute → volume → original-mute restoration order.

## Read-only hardware validation

| Scenario | Observed result |
|---|---|
| `status` | Pass; active Console render endpoint, x64 process, volume/mute read successfully |
| `watch --seconds 2` | Pass; initial endpoint binding and clean callback registration/unregistration |
| `pulse --percent 25` without confirmation | Pass; exited with code 2, rejected the mutation, volume remained 64% and unmuted |

Only the sanitized endpoint hash is retained in the evidence.

## Pending user-assisted hardware validation

The following are intentionally not marked complete until playback is stopped and the target device can be observed safely:

- 0%, 50%, and 100% pulse/readback checks
- external Windows UI, volume-key, and mute notification latency
- default endpoint switch and rebind without restart
- 1,000 bounded changes with p95 latency and resource CSV
- final comparison with the Windows volume UI within ±1 percentage point
