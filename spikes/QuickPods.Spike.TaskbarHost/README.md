# QuickPods Taskbar Host feasibility spike

This isolated Phase 0C/0D diagnostic evaluates whether a raw Win32 surface can be placed inside the primary Windows 11 taskbar without covering Shell controls, and whether a safe floating surface can preserve continuity while the native surface is unavailable. Phase 0D lives on `codex/phase-0d-floating-fallback-spike`, stacked on `codex/phase-0c-taskbar-host-spike`. It is evidence only; production code must be implemented separately after Gate B.

## Safety model

- The primary `Shell_TrayWnd` must be discovered uniquely.
- UI Automation must return one `StartButton`; `WidgetsButton` is optional.
- Localized UI names and window titles are never used for identity.
- Visible UIA button rectangles plus the native notification-area and clock rectangles are treated as obstacles. Structural containers such as the task-band and XAML-island host are not treated as a single blocking rectangle. Native discovery excludes only the exact HWND of the currently live host; every other unknown visible child remains an obstacle.
- Missing, duplicated, timed-out, or inconsistent observations return `TransientUnknown` and prohibit display.
- A verified layout with less than the compact width returns `VerifiedNoFit`.
- A visible host is created only for `Place`, after a zero-intersection check against every collected obstacle.
- An existing host rectangle is retained only when the same fresh observation proves the identity unchanged, the full horizontal geometry valid, the expected vertical band and supported width exact, and all raw and margin-expanded obstacle intersections zero.
- The spike never injects code, installs hooks, subclasses Explorer windows, or edits Explorer memory.
- Explorer restart and display-setting changes are never initiated by the spike.
- The actual parent is verified with `GetAncestor(..., GA_PARENT)` after `SetParent`; the child-style path additionally requires `GetParent` to match. This distinction matters because `GetParent` returns an owner for a top-level popup.

## Invalidation and recovery

- UI Automation handlers are registered, replaced, and removed on one dedicated MTA thread.
- Property changes are watched from the taskbar root subtree. A property notification is filtered only when its cached sender type is known to be non-Button; Button and unknown sender types still invalidate fail-closed. Structure changes are narrowed to the immediate ControlView parent subtree of the unique Start element and always invalidate.
- Callback work is limited to sender-only cached identity metadata and an atomic invalidation signal. Runtime diagnostics record only event kind and `Owned`／`External`／`Unknown` source classification.
- Subscription epochs reject callbacks arriving late from a replaced taskbar root. A watcher-generation fence requires a fresh scan before a host can be shown.
- Recovery is hide-first: a retained invalidation hides the host before scanning, then shows the verified existing host or recreates it only from a fresh `Place` result.
- A 5-second watchdog remains as a fallback for missed notifications and parent, DPI, identity, or geometry drift. It keeps the host visible only when a fresh scan proves the same identity, valid native attachment, and a still-safe current rectangle; an incomplete, raced, changed, or unsafe observation escalates to hidden recovery.
- Continuous invalidation for 10 seconds, or 6 sparse windows within 30 seconds, still makes the native surface fail closed. The sparse limit is acknowledged only for an external bounding-rectangle change with unchanged identity, a fresh safe existing rectangle, and a `ShowVerifiedExisting` recovery decision. This acknowledgment never clears the continuous window.
- Phase 0C historically ended the bounded Spike after the 10-second native recovery timeout. Phase 0D instead keeps the process alive and moves to `FloatingFallback` or `HiddenFallback`; the native fail-closed rule itself is unchanged.

## Floating fallback and native promotion

- `VerifiedNoFit` or a sustained incomplete native observation hides the native surface first. If a previously complete observation still supplies a verified primary-monitor work area and DPI, the strip immediately moves to an unowned, non-topmost floating popup inside that work area. Without safe geometry it remains hidden.
- Start and Search can temporarily produce `PrimaryTaskbarMissing`. This condition does not invalidate the last complete primary-monitor geometry. Settings, display, or DPI invalidation does invalidate it, preventing reuse across a changed coordinate system.
- The floating surface is `WS_POPUP` with `WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE`; it is never parented, owned, or topmost. Native and floating surfaces are never visible simultaneously.
- Promotion back to native waits for a 1-second mode cooldown and two identical verified `Place` candidates at least 500 ms apart, behind a fresh UI Automation watcher-generation fence. The floating surface is hidden before the prepared native surface is shown.
- Three native creation failures latch native hosting off for the rest of the session. The configured duration is measured once and is never reset by surface transitions.

## Rendering stability

- Each frame is composed in a compatible memory DC and transferred to the host with one `SRCCOPY BitBlt`; the color-key background and complete slider are never exposed as separate on-screen draw steps.
- Compatible bitmaps and memory DCs have deterministic safe-handle cleanup. Child and Popup tests render 1000 frames each while checking GDI and USER handles remain stable.
- Assigning the same clamped volume fraction is a no-op. Alternating click-jump tests cover 200 updates per style, including 0 → 1 → 0 pixels and the transparent corner.

## Commands

Build first:

```powershell
dotnet build spikes/QuickPods.Spike.TaskbarHost/QuickPods.Spike.TaskbarHost.csproj -c Release
```

Read-only inspection:

```powershell
dotnet run --project spikes/QuickPods.Spike.TaskbarHost -c Release -- inspect
```

Temporarily attach the visual diagnostic host after explicit confirmation:

```powershell
dotnet run --project spikes/QuickPods.Spike.TaskbarHost -c Release -- host --style child --fallback floating --duration 30 --confirm-live-host
```

Use `--style popup` for the experimental Ceiling-compatible native style comparison. `--fallback floating|hidden` selects the fail-closed fallback; the default is `floating`. Duration is bounded across all surface transitions; cancellation and every normal exit destroy every host HWND.

The application manifest is applied by the generated EXE (and by `dotnet run`), not by direct `dotnet QuickPods.Spike.TaskbarHost.dll` execution. Live DPI/host validation must therefore use the EXE or `dotnet run`.

The host draws a local sample slider only. It does not read or change Core Audio volume and does not access Bluetooth. Use `--style popup` for the bounded comparison run; the final Child versus Popup choice remains a Gate B decision.

## Gate evidence

Sanitized results belong in `docs/validation/phase-0/taskbar-host/`. Do not commit raw HWND values, Explorer PIDs, localized window names, notification content, or unreviewed screenshots.

The current Windows 11 evidence covers the Phase 0C 100%, 125%, and 150% Child and Popup runs, a 10-cycle visible-Child Explorer restart test, and 200% fail-closed NoFit checks. Phase 0D passes a Release build with 0 warnings/errors, TaskbarHost 301 tests plus Smoke 1, format verification, and diff check. At DPI 168 (175%), a 45-second EXE run with automated 12-second Start and 12-second Search input exited 0 with no residual process and observed at least one `External / StructureChanged` transition through `NativeVisible → FloatingFallback → NativePromoted`.

The sanitized log cannot identify whether a particular transition was caused by Start or Search, and automation does not prove that the floating strip stayed visibly continuous or remained operable. Manual visual/input confirmation of the floating surface, plus separate 15-second Start and Search checks at 100% with icon-plus-label Search, remain Pending. Many-pinned-app stress and the final Child/Popup choice also remain Pending, so Gate B remains Pending.
