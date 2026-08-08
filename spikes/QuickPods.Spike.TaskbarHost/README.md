# Taskbar-host feasibility project

This historical project evaluates Windows 11 taskbar discovery, safe empty-region calculation, DPI conversion, native and floating surfaces, input, Start/Search interaction, and Explorer recovery.

It is not the production taskbar host. Current code lives in `src/QuickPods.TaskbarHost`, `src/QuickPods.TaskbarObserver`, and the app's host supervisor.

The central safety rule remains relevant: QuickPods does not reserve taskbar space or move shell controls. A surface may appear only after taskbar identity, monitor, DPI, landmarks, obstacles, geometry, ownership, and Explorer generation are verified. Incomplete evidence must hide the native surface or use the documented fallback.

The diagnostic can create native windows and observe or restart Explorer in explicitly confirmed modes. Review the command options and source before use. Do not commit raw HWND values, Explorer process identifiers, localized notification content, or unreviewed screenshots.
