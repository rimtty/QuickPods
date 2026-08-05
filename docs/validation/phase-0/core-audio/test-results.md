# Core Audio spike test results

## Automated validation

Run on 2026-08-06 from the repository root.

| Check | Result |
|---|---|
| Locked NuGet restore | Pass |
| Release x64 build with warnings as errors | Pass; 0 warnings, 0 errors |
| Repository smoke tests | Pass; 1/1 |
| Core Audio unit tests | Pass; 44/44 |
| Format verification at warning severity | Pass |

The tests cover percent/scalar clamping, self-event classification, stale generations, nearest-rank percentile calculation, sustained resource-growth detection, bounded latest-value mailboxes, 10,000-callback coalescing, recovery after a notification sink throws, ABI header sizes, safe CLI bounds, guarded cancellation/failure completion, and the mute → volume → original-mute restoration order.

## Read-only hardware validation

| Scenario | Observed result |
|---|---|
| `status` | Pass; active Console render endpoint, x64 process, volume/mute read successfully |
| `watch --seconds 2` | Pass; initial endpoint binding and clean callback registration/unregistration |
| `pulse --percent 25` without confirmation | Pass; exited with code 2, rejected the mutation, volume remained 64% and unmuted |

Only the sanitized endpoint hash is retained in the evidence.

## Playback-stopped hardware validation

| Scenario | Observed result |
|---|---|
| `pulse --percent 0 --hold-ms 5000` | Pass; set 0.517 ms, notification 0.766 ms, exact readback, restored 64%/unmuted |
| `pulse --percent 50 --hold-ms 5000` | Pass; set 0.428 ms, notification 0.711 ms, exact readback, restored 64%/unmuted |
| `pulse --percent 100 --hold-ms 5000` | Pass; set 0.436 ms, notification 0.798 ms, exact readback, restored 64%/unmuted |
| 90-second external watch | Pass; 96 external notifications, 0 self notifications, 0–100% and repeated mute/unmute changes observed without restart |
| Windows Settings agreement | Pass; Settings displayed 64 and `status` returned 64.0%, unmuted |
| 100-step external key latency | Pass; p95 4.409 ms, restored, 250 ms gate passed |
| 1,000 bounded changes (63–64%) | Pass; set p95 0.111 ms, notification p95 0.119 ms, restored, no crash |
| Resource trend | Pass; private bytes +344,064, handles +7, threads +1 from post-warmup baseline, then plateau; sustained-growth flags all false |
| Default endpoint switch | Not executed; Windows exposed only one output. Design accepted for this gate and physical corroboration deferred to #19 |

The first key-latency attempt exposed an x64 `INPUT` union-size defect before any volume key was delivered. The guarded mutation still restored 64%/unmuted. The union was corrected to include native `MOUSEINPUT` sizing, after which 2/2 and 100/100 bounded external steps passed.

## Evidence files

- `metrics.csv`: 1,000 operation rows, 11 live-resource samples, and final summary
- `logs/exercise-1000.txt`: final stability gate output
- `logs/key-latency-100.txt`: external notification latency output
- `logs/external-volume-watch.txt`: sanitized external volume/mute events
- `logs/single-endpoint-watch.txt`: 120-second single-endpoint observation
- `screenshots/windows-sound-single-output-64.png`: Windows Settings showing the sole output and volume 64
