# Phase 3B — IPC and recovery validation

## Checkpoint environment

| Item | Value |
|---|---|
| Date | 2026-08-06 |
| OS | Windows 11 Pro 25H2, build 26200.8973 |
| Architecture | x64 |
| Taskbar | Center aligned, DPI 168, 5120 x 84 physical px |
| Audio safety | Playback stopped; original and restored state 10.0%, mute off |
| Issue | #34; #33 and #18 remain follow-up gates |

## Automated evidence

| Check | Result |
|---|---|
| Protocol constructor/version/range/sequence | Pass |
| Bounded JSON-line round trip | Pass |
| Oversized message rejection | Pass |
| Supervisor stable-reset contract | Pass |
| Focused Foundation tests | Pass — 49/49 at this checkpoint |
| App and TaskbarHost project builds | Pass — 0 warnings, 0 errors |
| App output closure | Pass — host EXE, managed DLL, deps, and runtimeconfig present |

## Live checkpoint

- The product app started one `QuickPods.App` and one `QuickPods.TaskbarHost` process.
- Independent discovery returned `Place / NativePlacement`, DPI 168, taskbar 5120 x 84, 29 automation buttons, and one native obstacle.
- Independent class enumeration found exactly one `QuickPods.Taskbar.View` and zero `QuickPods.Floating.View` surfaces.
- A real native-surface wheel message crossed Host -> CurrentUserOnly pipe -> App -> Core Audio. Independent reads observed 10.0% -> 12.0% -> 10.0%, with mute remaining off.
- After one forced host exit, the app stayed alive, one replacement host and one native surface returned, and the same 10 -> 12 -> 10 IPC/audio round trip passed again.
- A host that remained connected for at least ten seconds reset the failure count as designed.
- Three forced host exits in one bounded short-failure sequence left App count 1, Host count 0, and surface count 0; no restart occurred after the session limit.
- Normal app close then left App, Host, native surface, and floating surface counts all zero.

## Remaining Phase 3B gates

- [ ] implement the ADR-0001 observer helper and sanitized epoch/invalidation protocol;
- [ ] receive `TaskbarCreated`, retire the old Explorer generation, and recover hidden-first;
- [ ] verify observer/host ownership and no residue across forced shutdown;
- [ ] run the bounded Explorer restart/resource test and record USER/GDI evidence;
- [ ] resolve or safely downgrade center-aligned NoFit Floating z-order under Issue #33;
- [ ] rerun Start/Search continuity and user-visible input after the complete runtime is assembled.

Checkpoint status: **IPC/process recovery Go; Phase 3B overall remains in progress**.
