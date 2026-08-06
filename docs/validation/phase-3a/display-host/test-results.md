# Phase 3A — Display host validation

## Environment

| Item | Value |
|---|---|
| Date | 2026-08-06 |
| OS | Windows 11 Pro 25H2, build 26200.8973 |
| Architecture | x64 |
| Build | Release, .NET 10.0.302 |
| Issue / PR | #30 / #32 |
| Current scope | native/floating placement, discovery, rendering, hidden-first hosts, and fail-closed presentation routing |

## Automated evidence

| Check | Result |
|---|---|
| Foundation tests | Pass — 43/43 |
| New Phase 3A executions | Pass — 20 focused executions across native/floating placement, discovery, rendering, style, input, preview safety, and routing contracts |
| TaskbarHost Release build | Pass — 0 warnings, 0 errors |
| Solution locked restore | Pass |
| Format / diff check | Pass |

The focused coverage includes standard placement, verified left-aligned NoFit, incomplete and contradictory evidence, compact fragmentation, negative coordinates, 100/125/150/200% DPI conversion, automation/native obstacle merging, duplicate Start fail-closed behavior, shared slider hit-test geometry, render-state normalization, PopupPreserved style invariants, pointer/wheel volume conversion, contained floating fallback placement, and Native/Floating/Hidden routing. The Phase 0 diagnostic suite was not copied into the product suite.

## Read-only live inspection

The manifest-applied product `QuickPods.TaskbarHost.exe inspect` command performed one read-only scan. It did not create or attach a host window.

```text
protocol=1
discovery_complete=true
faults=
decision=Place
reason=None
dpi=144
taskbar_size=3840x72
automation_buttons=32
native_obstacles=1
exit_code=0
residual_count=0
```

This proves the first product discovery/placement slice on the current 150% primary taskbar. It does not yet prove drawing, input, fallback, Start/Search continuity, or Explorer recovery; those remain later Phase 3A/3B gates.

## Safety notes

- Raw HWND and process ID remain in ephemeral in-process models and are never emitted by `inspect`.
- Existing-host exclusion is admitted only for the exact supplied HWND when its class is `QuickPods.Taskbar.View`, its process is the current host, and its actual parent is the discovered taskbar.
- Any discovery fault makes the observation incomplete and therefore yields `TransientUnknown` rather than a guessed placement.
- Native creation is hidden-first. Promotion requires exact revalidation of trusted class/process provenance, parent attachment, PopupPreserved/non-topmost styles, taskbar containment, per-monitor DPI, DWM cloak state, and the layered color key.
- Floating creation is separately hidden-first and requires a trusted Floating class, no parent, no owner, no TopMost bit, exact verified-work-area containment, matching DPI, an uncloaked surface, and the layered color key.
- A settings, display, theme, or DPI invalidation received during either creation path is never cleared by creation completion; the surface remains hidden and creation fails for rediscovery.
- Static preview creation additionally requires the exact `--confirm-live-host` flag and a duration from 1 to 60 seconds; the host is disposed when the bounded loop ends.
- The product host implementation was not launched while the operator was away; visual placement, input, Start/Search continuity, and natural shutdown remain explicit live gates.
- Phase 3A currently uses a one-shot MTA UIA scan. The repeating watcher required by Phase 3B will not be enabled until Issue #18 has an isolation/lifetime disposition.
