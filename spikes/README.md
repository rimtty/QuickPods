# Technical spikes

This directory retains isolated feasibility projects that informed QuickPods architecture. They are historical engineering references, not shipped product components or supported command-line tools.

| Project | Research area |
|---|---|
| `QuickPods.Spike.CoreAudio` | Core Audio endpoint, volume, mute, notifications, and restoration |
| `QuickPods.Spike.BluetoothKs` | Bluetooth audio discovery and bounded kernel-streaming operations |
| `QuickPods.Spike.DefaultEndpointPolicy` | Windows default-output compatibility boundary |
| `QuickPods.Spike.TaskbarHost` | Windows 11 taskbar discovery, placement, rendering, and recovery |

The spikes remain in the solution because their pure logic and safety contracts have regression tests. Production behavior lives under `src/` and may have evolved beyond a spike.

Some commands can change audio, Bluetooth, Explorer, or default-device state. Read the relevant source and local README before running one. Do not commit generated logs, raw device identifiers, window handles, process identifiers, or machine-specific evidence.
