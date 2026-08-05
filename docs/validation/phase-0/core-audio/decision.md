# Core Audio feasibility decision

## Status

**Go — the public Core Audio path is approved for product implementation.**

Endpoint discovery, volume/mute reads and writes, callback registration, explicit MTA ownership, external-change observation, bounded latency, guarded restoration, and 1,000-operation stability passed on the current Windows 11 machine. Gate P0-A is satisfied and Phase 1/2 may use this architecture.

The final environment exposes only one render endpoint, so a physical default-endpoint A → B → A switch could not be performed. Per the user's direction, the gate accepts the implemented public-API design and deterministic evidence for this scenario. The missing multi-device hardware corroboration is explicitly non-blocking and tracked in [#19](https://github.com/rimtty/QuickPods/issues/19); it is not represented as an executed test.

## Architecture decision

- Use public MMDevice and EndpointVolume APIs only.
- Bind to the default `eRender` / `eConsole` endpoint.
- Own all COM objects on one synchronous MTA worker.
- Coalesce callbacks before they enter the worker and retain only the latest volume notification under burst load.
- Use a stable application event-context GUID to classify self-originated notifications.
- Advance a generation on each default-endpoint binding and reject stale callbacks.
- Guard every mutation with an original-endpoint lease. Mute first, restore the original scalar to the original endpoint, restore the original mute state last, and verify by reading the same endpoint.
- Treat unavailable endpoints and bounded rebind exhaustion as explicit unavailable states rather than guessed success.

## Gate evidence

| Requirement | Evidence | Result |
|---|---|---|
| Windows UI agreement within ±1 percentage point | Windows Settings and `status` both showed 64%; pulse readbacks were exact at 0%, 50%, and 100% | Pass |
| Set-operation p95 at most 100 ms | 1,000-operation run: 0.111 ms | Pass |
| External-notification p95 at most 250 ms | 100 public `SendInput` volume-key steps: 4.409 ms | Pass |
| Default endpoint changes followed without restart | `IMMNotificationClient` render/Console filtering, callback coalescing, bounded rebind, generation advance, stale-event rejection, and retained original-endpoint lease are implemented; physical switch deferred because Windows exposed one output | Accepted by design; follow-up #19 |
| 1,000 bounded changes without crash or continuously increasing resources | Completed; resource samples plateaued, all sustained-growth flags false, rebind count 0 | Pass |
| Restoration on success, failure, and cancellation | Physical success runs restored 64%/unmuted; the initial `SendInput` ABI failure also restored; deterministic cancellation test verifies restoration precedes rethrow | Pass |

The detailed measurements and sanitized logs are in `test-results.md`, `metrics.csv`, and `logs/`.

## Primary references

- [IAudioEndpointVolume::SetMasterVolumeLevelScalar](https://learn.microsoft.com/windows/win32/api/endpointvolume/nf-endpointvolume-iaudioendpointvolume-setmastervolumelevelscalar)
- [IAudioEndpointVolumeCallback](https://learn.microsoft.com/windows/win32/api/endpointvolume/nn-endpointvolume-iaudioendpointvolumecallback)
- [RegisterControlChangeNotify](https://learn.microsoft.com/windows/win32/api/endpointvolume/nf-endpointvolume-iaudioendpointvolume-registercontrolchangenotify)
- [IMMNotificationClient](https://learn.microsoft.com/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immnotificationclient)
- [CoInitializeEx](https://learn.microsoft.com/windows/win32/api/combaseapi/nf-combaseapi-coinitializeex)
