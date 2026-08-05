# QuickPods Taskbar Host feasibility spike

This isolated Phase 0C diagnostic evaluates whether a raw Win32 surface can be placed inside the primary Windows 11 taskbar without covering Shell controls. It is evidence only; production code must be implemented separately after Gate B.

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
- Continuous invalidation for 10 seconds, or 6 sparse windows within 30 seconds, fails closed. The sparse limit is acknowledged only for an external bounding-rectangle change with unchanged identity, a fresh safe existing rectangle, and a `ShowVerifiedExisting` recovery decision. This acknowledgment never clears the continuous window.

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
dotnet run --project spikes/QuickPods.Spike.TaskbarHost -c Release -- host --style child --duration 30 --confirm-live-host
```

Use `--style popup` for the experimental Ceiling-compatible style comparison. Duration is bounded; cancellation and every normal exit destroy the host HWND.

The host draws a local sample slider only. It does not read or change Core Audio volume and does not access Bluetooth. Use `--style popup` for the bounded comparison run; the final Child versus Popup choice remains a Gate B decision.

## Gate evidence

Sanitized results belong in `docs/validation/phase-0/taskbar-host/`. Do not commit raw HWND values, Explorer PIDs, localized window names, notification content, or unreviewed screenshots.

The current Windows 11 evidence covers 150% Child and Popup runs, a 10-cycle visible-Child Explorer restart test, 200% fail-closed NoFit checks, and 125% Child and Popup input runs. After fixing periodic Pane notifications, self-obstacle discovery, and direct-GDI tear risk, 17-second 10ms visibility sampling found zero hidden intervals for both styles; 150 rapid Popup clicks also found zero hidden intervals. Manual rapid-click confirmation, 100%, fallback and remaining layout cases, and the final style decision are still pending, so Gate B remains Pending.
