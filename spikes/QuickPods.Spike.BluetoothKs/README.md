# QuickPods Bluetooth KS spike

This isolated diagnostic determines whether the documented Windows KS and DeviceTopology path can safely control the selected Bluetooth audio device on the current driver stack. It is not production code.

## Safety boundaries

- `inventory` only discovers audio endpoints, Container IDs, topology links, and potential KS filters. It does not call `IKsControl::KsProperty`.
- Every non-help command runs inside a kill-on-close Windows Job Object, so a stalled discovery or topology call cannot hold the invoking process indefinitely. The named outer Job must report zero active processes before its machine-wide operation gate is released.
- All diagnostic commands share a machine-wide named Mutex because they also share the named containment Job. A concurrent, abandoned, cancelled, or uncontained prior command is rejected before another isolated command starts.
- `probe` requires the report session token and `--confirm-ks-operation`; each Basic Support request runs in a second watchdog-protected child process.
- `connect` and `disconnect` additionally require the report-scoped target alias to be repeated with `--confirm-target` and require `--confirm-playback-stopped`.
- A successful KS HRESULT is never treated as a successful connection change without an independent MMDevice state transition.
- Only one explicitly selected Container ID may be operated on. Display names are never identity keys.
- An unscoped topology fault still makes the entire snapshot ineligible. A scoped fault or unassigned adapter blocks only a selected Container whose endpoints or KS candidates overlap that incomplete evidence; unrelated endpoints and unsupported devices do not disable a complete target.
- MAC addresses and full Container, endpoint, adapter, and PnP identifiers are never printed or persisted.
- `inventory` creates a random report session token. All 24-character aliases use session-keyed HMAC, so an alias can be reused with that token but cannot be linked across reports.
- Internal child requests carry a version, nonce, exact operation, target alias, and consent flags. Real KS children also require a parent process running the same executable.
- The diagnostic never installs a driver, changes the registry, requests elevation, toggles the Bluetooth radio, or calls private OS APIs.

The Gate A decision remains pending until the MediaTek/AirPods hardware matrix is completed. A No-Go result falls back to the Windows Bluetooth settings page (`ms-settings:bluetooth`).

## Multi-device catalog contract

The `Catalog` folder is a pure Phase 0 contract for the v2 selector. It accepts endpoint facts from a future paired-device adapter and:

- excludes entries not explicitly marked as paired Bluetooth audio;
- groups stereo/A2DP and hands-free/HFP endpoint variants by Container key;
- keeps same-name physical devices distinct because display names are never identity;
- derives a conservative connection state from render endpoints;
- preserves an explicit selection across refresh and temporary disappearance;
- has no dependency on the KS child runner or command invoker, so selection and refresh cannot mutate Windows state.

The product implementation must populate `IsPaired`, `IsBluetooth`, display name, kind, profile, and Container identity from Windows device metadata. It must not infer Bluetooth identity from the friendly-name string.

Run `inventory` first, then copy its `session` value and target alias into a separately confirmed `probe`, `connect`, or `disconnect` command. Do not commit the session token or runtime output.
