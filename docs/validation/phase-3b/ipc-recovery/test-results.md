# Phase 3B — IPC and recovery validation

## Checkpoint environment

| Item | Value |
|---|---|
| Date | 2026-08-06 |
| OS | Windows 11 Pro 25H2, build 26200.8973 |
| Architecture | x64 |
| Taskbar | Center aligned, DPI 168, 5120 x 84 physical px |
| Audio safety | Playback stopped; original and restored state 10.0%, mute off |
| Issue | #34 and the physical acceptance portion of #18 remain follow-up gates; #33 was removed from the release gate on 2026-08-07 |

The checkpoint values above describe the original local-console run. A later
Remote Desktop session alternated between virtual and physical display
topologies, so its DPI, taskbar bounds, and visual rendering observations are
explicitly excluded from acceptance evidence. DPI/Floating visual gates are
deferred until a stable local-console session is available.

## Automated evidence

| Check | Result |
|---|---|
| Protocol constructor/version/range/sequence | Pass |
| Bounded JSON-line round trip | Pass |
| Oversized message rejection | Pass |
| Supervisor stable-reset contract | Pass |
| Observer protocol constructor/round trip/epoch gate | Pass |
| Focused Foundation tests | Pass — 50/50 at this checkpoint |
| Full solution Release build | Pass — 0 warnings, 0 errors |
| App output closure | Pass — host and observer EXE, managed DLL, deps, and runtimeconfig present |

## Live checkpoint

- The product app started one `QuickPods.App` and one `QuickPods.TaskbarHost` process.
- Independent discovery returned `Place / NativePlacement`, DPI 168, taskbar 5120 x 84, 29 automation buttons, and one native obstacle.
- Independent class enumeration found exactly one `QuickPods.Taskbar.View` and zero `QuickPods.Floating.View` surfaces.
- A real native-surface wheel message crossed Host -> CurrentUserOnly pipe -> App -> Core Audio. Independent reads observed 10.0% -> 12.0% -> 10.0%, with mute remaining off.
- After one forced host exit, the app stayed alive, one replacement host and one native surface returned, and the same 10 -> 12 -> 10 IPC/audio round trip passed again.
- A host that remained connected for at least ten seconds reset the failure count as designed.
- Three forced host exits in one bounded short-failure sequence left App count 1, Host count 0, and surface count 0; no restart occurred after the session limit.
- Normal app close then left App, Host, native surface, and floating surface counts all zero.

## Observer and TaskbarCreated checkpoint

- One `QuickPods.TaskbarObserver` remained stable beside one App and one Host during a 15-second product run; no self-feedback churn occurred.
- Killing only the observer produced one replacement observer while preserving one Host and one display surface. A real wheel IPC round trip still restored 10.0% -> 12.0% -> 10.0%, mute off.
- Killing the Host caused its kill-on-close Job Object to retire the old observer within 700 ms. The App created exactly one replacement Host and observer generation.
- A synthetic registered `TaskbarCreated` message sent only to the product's hidden top-level control HWND retired observer PID 30564 and created PID 50104 in 2224 ms while preserving Host PID 6340 and the same control HWND. Exactly one Native HWND was recreated (`0xBB07A6` -> `0xBC07A6`), Floating remained zero, and the old observer was no longer alive.
- The synthetic gate exposed and fixed an observer teardown defect: closing the pipe before final `StreamWriter` flush had propagated `ObjectDisposedException` through the Host. Transport retirement is now idempotent and no longer restarts the Host.
- Every normal close after observer, host, and `TaskbarCreated` recovery left App, Host, Observer, Native, and Floating counts at zero.
- The independent Core Audio read after all runs remained exactly 10.0%, mute off.

## Command-input checkpoint

- The shared Native/Floating interaction session now emits all Phase 3B host
  commands: volume preview/commit, speaker-icon `ToggleMute`, strip-whitespace
  `OpenAudioFlyout`, and `WM_RBUTTONUP` `OpenContextMenu`.
- A bounded product Native preview received the three non-volume Win32 inputs
  through its real HWND and emitted monotonic sequences 0, 1, and 2 in that
  order. The preview was intentionally disconnected from the App/Core Audio,
  exited naturally, and left no Host process.
- The existing focused interaction test was extended rather than adding a new
  diagnostic suite. Foundation remains 50/50 and the Release solution build
  remains at zero warnings and zero errors.

## Remaining Phase 3B gates

- [x] implement the ADR-0001 observer helper and sanitized epoch/invalidation protocol;
- [x] receive `TaskbarCreated`, retire the old Explorer generation, and recover hidden-first;
- [x] verify observer/host ownership and no residue across forced shutdown;
- [x] route mute, flyout, and context-menu commands from both visible surfaces;
- [ ] run the bounded Explorer restart/resource test and record USER/GDI evidence;
- [x] retain the fail-closed center-aligned NoFit contract without an additional dedicated physical reproduction; product decision recorded 2026-08-07 and Issue #33 closed as not planned;
- [ ] rerun Start/Search continuity and user-visible input after the complete runtime is assembled.

Checkpoint status: **IPC/process recovery Go; Phase 3B overall remains in progress**.
