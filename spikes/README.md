# Technical spikes

Phase 0 feasibility projects live under this directory. Each spike must remain isolated from production projects and record its environment, measurements, and decision under `docs/validation/phase-0/`.

Planned spikes:

- `QuickPods.Spike.CoreAudio`
- `QuickPods.Spike.BluetoothKs`
- `QuickPods.Spike.DefaultEndpointPolicy`
- `QuickPods.Spike.TaskbarHost`

Each spike branch contains only its own diagnostic until the focused pull request is integrated into `main`.

Spike code is evidence, not production code. Product implementations must be written from the validated behavior and contracts rather than promoted directly from a spike.
