# QuickPods Default Endpoint Policy Spike

This Phase 0D spike isolates the Windows default-output compatibility boundary required after a selected Bluetooth device is independently verified as connected.

The first implementation slice contains no OS mutation command. It defines and verifies these fail-closed rules:

- only one active stereo render endpoint from the selected physical Container is eligible;
- Hands-Free, capture, inactive, missing, and ambiguous endpoints are rejected;
- only Console and Multimedia may be changed;
- a write is not successful until the matching generation notification and read-back agree;
- Communications must remain unchanged;
- already-default is idempotent, and partial or superseded work remains visible in the result.

The Windows COM adapter and explicit-confirmation diagnostic command are the next slice. Until those exist and Gate A identifies the selected connected endpoint, this executable cannot change the system default output.
