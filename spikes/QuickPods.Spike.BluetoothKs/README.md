# Bluetooth kernel-streaming feasibility project

This historical project investigates how Windows exposes paired Bluetooth audio devices, profiles, physical-device identity, and driver-specific reconnect/disconnect operations.

It is not the production Bluetooth implementation. Current product code lives in `src/QuickPods.Windows/Bluetooth`, `src/QuickPods.Core`, and the bounded `QuickPods.BluetoothWorker` process.

The project intentionally separates read-only inventory from mutation. A driver request is allowed only for an explicitly selected, uniquely verified device and only after the driver reports the required capability. Calls run with timeouts and child-process ownership so a blocked driver cannot hang the main application.

Do not run mutation commands on an everyday Windows session without reviewing the CLI safety checks and understanding which physical device will be affected. Never commit raw Container IDs, Endpoint IDs, PnP IDs, Bluetooth addresses, or unsanitized inventory output.
