# TaskbarObserver lifecycle diagnostics

## 2026-08-07 short-run observation

The local-console `visual.84` resource sampler was intentionally stopped after
1,391 samples and 2.01 hours when the operator accepted the shortened aging
scope. The three-process tree stayed supervised, but one sample contained two
processes while the original TaskbarObserver generation retired. A replacement
Observer was running approximately 1.6 seconds later under the same App and
TaskbarHost. Explorer and TaskbarHost did not restart.

The Windows Application log contained no matching Application Error, .NET
Runtime, Windows Error Reporting, or Application Hang event. The application
JSONL also contained no Warning or Error. These facts rule out an OS-reported
process crash, but they do not prove whether the retirement came from a
`TaskbarCreated` broadcast, the Observer's normal `ExplorerGenerationChanged`
terminal batch, an `ObserverFaulted` batch, or an unannounced pipe disconnect.
The pre-fix host consumed those states only in memory and did not persist the
reason.

Resource totals stepped upward after the Observer replacement and a later
Bluetooth catalog refresh/UI activation interval. Because those events overlap,
the aggregate CSV cannot assign the allocation step to the Observer retirement
or establish monotonic leakage. The preserved short-run evidence is therefore
review evidence, not a causal diagnosis.

## Issue #77 correction

Protocol version 3 adds four host-to-app lifecycle notification kinds:

- `TaskbarObserverTaskbarCreated`;
- `TaskbarObserverGenerationChanged`;
- `TaskbarObserverFaulted`;
- `TaskbarObserverDisconnected`.

Each notification requires only a nonnegative generation ordinal. Volume,
anchor, process ID, Explorer identity, HWND, command line, user, and device data
are forbidden. TaskbarHost sends the notification before retiring the owned
Observer. QuickPods records `TaskbarObserverRetired` at Information level for
normal shell generations and Warning level for a fault or an unannounced
disconnect. The message returns immediately after logging and cannot enter an
audio, Bluetooth, flyout, or settings action.

The existing fail-closed retirement, Job ownership, and supervised replacement
behavior are unchanged.

## Validation

- focused lifecycle/protocol tests: 12 passed;
- complete regression: 397 passed;
- Release build: 0 warnings, 0 errors;
- `dotnet format --verify-no-changes`: passed;
- `git diff --check`: passed.

A later real shell-generation event can now prove its exact sanitized reason in
the application JSONL. The historical `visual.84` retirement remains
indeterminate by design and is not relabeled after the fact.
