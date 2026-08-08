# Core Audio feasibility project

This historical project isolates Windows Core Audio experiments used to establish QuickPods volume, mute, endpoint-notification, generation, latency, and state-restoration behavior.

It is not the production audio implementation. Current product code lives in `src/QuickPods.Windows/Audio` and `src/QuickPods.Core`.

The CLI contains read-only observation and explicitly confirmed mutation exercises. Any command that changes volume or mute must preserve the original endpoint state on normal completion and failure. Run it only on a disposable or controlled local Windows session after reviewing the options in `CoreAudioOptions.cs`.

Generated CSV files and logs may reveal local endpoint names or paths and must remain outside the repository.
