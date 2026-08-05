# QuickPods Taskbar Host feasibility spike

This isolated Phase 0C/0D/0E diagnostic evaluates whether a raw Win32 surface can be placed inside the primary Windows 11 taskbar without covering Shell controls, whether a safe floating surface can preserve continuity while native placement is unavailable, and whether an already verified native surface can remain attached during Start/Search transient UI. Phase 0E lives on `codex/phase-0e-native-continuity-spike`, stacked on Phase 0D and Phase 0C. It is evidence only; production code must be implemented separately after Gate B.

## Safety model

- The primary `Shell_TrayWnd` must be discovered uniquely.
- UI Automation must return one `StartButton`; `WidgetsButton` is optional.
- Localized UI names and window titles are never used for identity.
- Visible UIA button rectangles plus the native notification-area and clock rectangles are treated as obstacles. Structural containers such as the task-band and XAML-island host are not treated as a single blocking rectangle. Native discovery excludes only the exact HWND of the currently live host; every other unknown visible child remains an obstacle.
- Missing, duplicated, timed-out, or inconsistent observations return `TransientUnknown` and prohibit initial display. The only visible-continuity exception is a previously complete anchor revalidated through the strict `DirectExpected` proof described below.
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
- Phase 0C historically ended the bounded Spike after the 10-second native recovery timeout. Phase 0D instead keeps the process alive and moves to `FloatingFallback` or `HiddenFallback`; the native fail-closed rule itself is unchanged.

## Phase 0E native continuity

- Continuity can start only from a complete normal discovery that produced one primary taskbar and one unique Start landmark. It cannot discover or select an initial target.
- Top-level enumeration and class reads must succeed with no competing primary taskbar. When the exact anchored taskbar is temporarily absent from the top-level result, its retained HWND must independently pass class, root, Explorer process, bounds, DPI, primary monitor, work area, visibility, and DWM uncloaked checks. This route is `DirectExpected`.
- Native critical children must remain complete. The sole exception is a missing notification area on `DirectExpected`; its prior obstacle is retained, fresh native obstacles are added, and the existing host rectangle is checked against their conservative union.
- UI Automation must remain complete. The sole exception is exactly one `StartButtonMissing` fault on `DirectExpected`; the last Start from a complete UIA observation is combined with every fresh valid button, while all prior obstacles remain in the conservative union. Any additional UIA fault, invalid rectangle, contradictory Start, duplicate, timeout, or property failure rejects continuity.
- The live QuickPods view must either be found as the exact excluded child or pass a separate direct attachment proof: QuickPods view class, same process, exact taskbar parent, taskbar-contained bounds, matching DPI, visible, and DWM uncloaked. The runtime then independently verifies exact style, parent, bounds, DPI, visibility, and DWM state again.
- UIA is fenced by complete native probes before and after the out-of-process call. Route changes, watcher-generation changes, settings/display/DPI invalidation, identity drift, unsafe fresh obstacles, or the fixed 500 ms deadline cause immediate hide/fallback. `DirectExpected` is rescanned every 500 ms and the live surfaces are health-checked every 100 ms.
- A retained Start never becomes the new baseline. The anchor advances only after a fresh fault-free UIA observation of the same taskbar generation.
- `PopupPreserved` is the selected native style: `SetParent` keeps `WS_POPUP | WS_CLIPSIBLINGS`, with `WS_CHILD` retained only as an explicit comparison/rollback option.

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
dotnet run --project spikes/QuickPods.Spike.TaskbarHost -c Release -- host --fallback floating --duration 30 --confirm-live-host
```

The default native style is the selected Ceiling-compatible `popup` mode. Use `--style child` only for the explicit comparison/rollback path. `--fallback floating|hidden` selects the fail-closed fallback; the default is `floating`. Duration is bounded across all surface transitions; cancellation and every normal exit destroy every host HWND.

The application manifest is applied by the generated EXE (and by `dotnet run`), not by direct `dotnet QuickPods.Spike.TaskbarHost.dll` execution. Live DPI/host validation must therefore use the EXE or `dotnet run`.

The host draws a local sample slider only. It does not read or change Core Audio volume and does not access Bluetooth. Popup is the selected native style; the overall Gate B decision remains pending until the remaining stress and recovery matrix is complete.

## Gate evidence

Sanitized results belong in `docs/validation/phase-0/taskbar-host/`. Do not commit raw HWND values, Explorer PIDs, localized window names, notification content, or unreviewed screenshots.

The current Windows 11 evidence covers the Phase 0C 100%, 125%, and 150% Child and Popup runs, a 10-cycle visible-Child Explorer restart test, and 200% fail-closed NoFit checks. Phase 0D adds the explicit floating fallback and promotion path. Phase 0E passes a Release build with 0 warnings/errors, TaskbarHost 183 tests plus Smoke 1, format verification, and diff check. The suite is exactly 50.0% of its prior 368-case total after redundant diagnostic and implementation-detail matrices were removed.

At 100% on 1920×1080 and 150% on a 5120-pixel-wide primary taskbar, separate 120-second Popup EXE runs remained `NativeVisible` through Start and Search checks. Both produced zero Floating transitions, verified continuity, wheel input while each transient UI was open, normal native destruction, and no residual process. The 150% run recorded three continuity verifications, 273 wheel events, and 12 completed drags. Child did not meet that continuity requirement, so PopupPreserved is selected.

The selected Popup then recovered through 10/10 Explorer restarts within 10 seconds (maximum 5.395 seconds). Every cycle observed the old View disappear, restored exactly one View and one hidden Control under the new Explorer generation, reverified Popup style, actual parent, and DWM state, and left no residual process or HWND after exit. USER objects increased from 22 to 32 while GDI objects and the normal/message-only window inventory remained unchanged; this managed UI Automation event-subscription lifetime is tracked separately in Issue #18 and does not justify expanding the current unit-test matrix. The 100% and 200% left-aligned NoFit layouts also passed automated ownership/style/input audits and manual floating visual/input checks with natural cleanup and no residue. At 150%, many-pinned-app stress increased UIA buttons from 27 to 52 and moved Start from X 1919 to 1094; automated and manual Popup checks passed with zero Floating transitions, errors, or residue. Tray churn while Start is open is the final pending Gate B item.
