# Core Audio feasibility decision

## Status

**Pending hardware-changing validation — neither Go nor No-Go yet.**

The public API path is proven for endpoint discovery, volume/mute reads, callback registration, and explicit MTA ownership on the current Windows 11 machine. The automatic suite and read-only hardware checks pass. Roadmap Gate P0-A remains open because changing-volume, latency, resource, external-UI, and default-device-switch scenarios still require a playback-stopped validation session.

## Provisional architecture decision

- Use public MMDevice and EndpointVolume APIs only.
- Bind to the default `eRender` / `eConsole` endpoint.
- Own all COM objects on one synchronous MTA worker.
- Coalesce callbacks before they enter the worker and retain only the latest volume notification under burst load.
- Use a stable application event-context GUID to classify self-originated notifications.
- Advance a generation on each default-endpoint binding and reject stale callbacks.
- Guard every mutation with an original-endpoint lease. Mute first, restore the original scalar to the original endpoint, restore the original mute state last, and verify by reading the same endpoint.
- Treat unavailable endpoints and bounded rebind exhaustion as explicit unavailable states rather than guessed success.

## Required Go evidence

- Windows UI agreement within ±1 percentage point
- set-operation p95 at most 100 ms
- external-notification p95 at most 250 ms
- default endpoint changes followed without restart
- 1,000 bounded changes without crash or continuously increasing private bytes, handles, or threads
- verified restoration on success, failure, and cancellation

Failure of the public Core Audio path is a product-level No-Go and blocks Phase 1, as defined by the roadmap.

## Primary references

- [IAudioEndpointVolume::SetMasterVolumeLevelScalar](https://learn.microsoft.com/windows/win32/api/endpointvolume/nf-endpointvolume-iaudioendpointvolume-setmastervolumelevelscalar)
- [IAudioEndpointVolumeCallback](https://learn.microsoft.com/windows/win32/api/endpointvolume/nn-endpointvolume-iaudioendpointvolumecallback)
- [RegisterControlChangeNotify](https://learn.microsoft.com/windows/win32/api/endpointvolume/nf-endpointvolume-iaudioendpointvolume-registercontrolchangenotify)
- [IMMNotificationClient](https://learn.microsoft.com/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immnotificationclient)
- [CoInitializeEx](https://learn.microsoft.com/windows/win32/api/combaseapi/nf-combaseapi-coinitializeex)
