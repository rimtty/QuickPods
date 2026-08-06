# QuickPods Default Endpoint Policy Spike

This Phase 0D spike isolates the Windows default-output compatibility boundary required after a selected Bluetooth device is independently verified as connected.

The spike defines and verifies these fail-closed rules:

- only one active stereo render endpoint from the selected physical Container is eligible;
- Hands-Free, capture, inactive, missing, and ambiguous endpoints are rejected;
- only Console and Multimedia may be changed;
- a write is not successful until the matching generation notification and read-back agree;
- Communications must remain unchanged;
- already-default is idempotent, and partial or superseded work remains visible in the result.

The Windows adapter isolates the undocumented `IPolicyConfig::SetDefaultEndpoint`
vtable, registers `IMMNotificationClient` before each write, and verifies the
result through both notification and MMDevice read-back. The compatibility
boundary is never used by `inventory` or `probe`.

Commands:

- `inventory` lists only report-scoped Container/Endpoint aliases, state,
  form-factor classification, and current default roles. It does not create the
  mutation adapter.
- `probe` activates the isolated COM boundary and reads whether each default
  role exists. It does not call `SetDefaultEndpoint`.
- `apply --session TOKEN --container ALIAS --confirm-container ALIAS
  --confirm-default-endpoint-operation` resolves exactly one current active
  stereo render Endpoint and may write only Console and Multimedia.

`apply` remains an explicit Gate command. Do not run it until the selected
Bluetooth Container is independently verified as connected and the operator is
ready to observe and restore the prior defaults.
