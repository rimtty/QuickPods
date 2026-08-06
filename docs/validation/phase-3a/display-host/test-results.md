# Phase 3A — Display host validation

## Environment

| Item | Value |
|---|---|
| Date | 2026-08-06 |
| OS | Windows 11 Pro 25H2, build 26200.8973 |
| Architecture | x64 |
| Build | Release, .NET 10.0.302 |
| Issue / PR | #30 / #32 |
| Current scope | center-aligned Native/Floating placement, left-aligned unsupported Hidden routing, discovery, rendering, hidden-first hosts, and Native live gate |

## Automated evidence

| Check | Result |
|---|---|
| Foundation tests | Pass — 45/45 |
| New Phase 3A executions | Pass — 22 focused executions across native/floating placement, left-aligned unsupported routing, discovery, rendering, style, input, preview safety, and routing contracts |
| TaskbarHost Release build | Pass — 0 warnings, 0 errors |
| Isolated full solution Release build | Pass — 0 warnings, 0 errors; repeated after the Native parent-verification fix without stopping the running Audio MVP |
| Solution locked restore | Pass |
| Format / diff check | Pass |
| Latest stacked PR CI | Pass — run 31072401444, head `c0f5bfc` |

The focused coverage includes standard placement, left-aligned unsupported/Hidden routing, center-aligned verified NoFit, incomplete and contradictory evidence, compact fragmentation, negative coordinates, 100/125/150/200% DPI conversion, automation/native obstacle merging, duplicate Start fail-closed behavior, shared slider hit-test geometry, render-state normalization, PopupPreserved style invariants, pointer/wheel volume conversion, contained floating fallback placement, and Native/Floating/Hidden routing. The Phase 0 diagnostic suite was not copied into the product suite.

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

This proves the first product discovery/placement slice on the 150% primary taskbar. It did not create a window and is retained as the initial read-only baseline.

## Native live gate

The manifest-applied product EXE was then exercised on the 175% (`dpi=168`) primary taskbar. The first bounded preview failed hidden-first before display because the product port verified a PopupPreserved attachment with `GetParent`. A `WS_POPUP` attached with `SetParent` can report no parent through `GetParent`; the authoritative hierarchy check is `GetAncestor(GA_PARENT)`, which the Phase 0 spike already used. Both create-time and continuity verification were corrected to use that API.

After the correction, a one-second smoke preview and a 60-second operator preview both exited naturally with code 0 and no residual process. The operator exercised click-to-jump, drag, and wheel in both directions, then opened and closed Start and Search. The sanitized evidence was:

```text
decision=Place
surface=Native
dpi=168
taskbar_size=5120x84
automation_buttons=29
native_obstacles=1
interaction sequence=0..323
observed volume range=0..100
layout_invalidated count=0
preview_shutdown=natural
exit_code=0
residual_process_count=0
```

The sequence was strictly monotonic, pointer previews and commits were both observed, wheel commits clamped at 0/100, and no layout invalidation occurred while Start/Search were exercised. The operator confirmed that the bar remained inside the taskbar with no flicker or Floating retreat. Native drawing, input, PopupPreserved Start/Search continuity, natural teardown, and the product-level `GetAncestor(GA_PARENT)` correction therefore pass. Explorer-generation recovery remains Phase 3B.

## Left-aligned unsupported-configuration gate

After switching the current 175% taskbar to left alignment with Widgets enabled, three repeated product inspections and one Phase 0 diagnostic inspection agreed on Start at relative X=0 and Widgets at relative X=4155 on a 5120px taskbar. The pre-policy implementation safely returned `TransientUnknown / ContradictoryLandmarks → Hidden`. With Widgets disabled, the former policy produced `VerifiedNoFit → Floating`, but two operator runs showed no useful visible surface among existing normal windows. No experimental Z-order change from that investigation was retained.

The product policy was then deliberately narrowed to Windows 11 center alignment. Left alignment is now a known unsupported configuration, not NoFit or an unknown transient state. The focused contract test and the same live left-aligned machine both produced:

```text
decision=UnsupportedConfiguration
reason=UnsupportedAlignment
surface=Hidden
surface_reason=UnsupportedConfigurationHidden
exit_code=4
residual_process_count=0
```

The read-only inspection and an explicit-confirmation three-second preview agreed. The preview created no surface, emitted no interaction, and left no process. Historical Phase 0 left-aligned Floating evidence remains useful research, but is superseded as a product requirement. Floating remains available only for a verified NoFit inside the supported center-aligned configuration.

## Center-alignment recovery gate

After the operator restored Windows 11 center alignment, the same manifest-applied product EXE immediately returned to the supported route:

```text
decision=Place
reason=None
surface=Native
surface_reason=NativePlacement
dpi=168
taskbar_size=5120x84
automation_buttons=28
native_obstacles=1
preview_shutdown=natural
exit_code=0
residual_process_count=0
```

A 20-second preview and a separate 15-second evidence preview both completed naturally. A full virtual-screen capture taken while the latter was live showed the rendered QuickPods strip inside the center-aligned taskbar at the bottom center. The capture included unrelated application content and therefore remains an ignored local artifact rather than repository evidence. Together with the earlier operator input/Start/Search run, this proves `left-aligned Hidden → center-aligned Native`, actual rendering, input, continuity, and teardown. Phase 3A live acceptance is complete.

The pre-policy Floating investigation exposed a possible generic z-order issue: a non-topmost Floating HWND could be live and visible according to Win32 while remaining below existing normal windows. Left alignment no longer exercises that path, so it does not block Phase 3A. Center-aligned NoFit reproduction and resolution are tracked by Issue #33 for Phase 3B.

## Safety notes

- Raw HWND and process ID remain in ephemeral in-process models and are never emitted by `inspect`.
- Existing-host exclusion is admitted only for the exact supplied HWND when its class is `QuickPods.Taskbar.View`, its process is the current host, and its actual parent is the discovered taskbar.
- Any discovery fault makes the observation incomplete and therefore yields `TransientUnknown` rather than a guessed placement.
- Native creation is hidden-first. Promotion requires exact revalidation of trusted class/process provenance, parent attachment, PopupPreserved/non-topmost styles, taskbar containment, per-monitor DPI, DWM cloak state, and the layered color key.
- Floating creation is separately hidden-first and requires a trusted Floating class, no parent, no owner, no TopMost bit, exact verified-work-area containment, matching DPI, an uncloaked surface, and the layered color key.
- A settings, display, theme, or DPI invalidation received during either creation path is never cleared by creation completion; the surface remains hidden and creation fails for rediscovery.
- Static preview creation additionally requires the exact `--confirm-live-host` flag and a duration from 1 to 60 seconds; the host is disposed when the bounded loop ends.
- The Native live gate was run only while the operator was present. Left alignment is explicitly unsupported and does not require a Floating live gate.
- Phase 3A uses a one-shot MTA UIA scan and registers no repeating UIA handlers. ADR-0001 resolves Issue #18 by assigning the Phase 3B repeating watcher to a short-lived helper process recycled with each Explorer generation.
