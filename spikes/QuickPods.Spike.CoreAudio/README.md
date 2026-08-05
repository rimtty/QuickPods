# QuickPods Core Audio spike

This isolated console diagnostic proves the public Windows Core Audio path used by QuickPods. It is not production code and is not referenced by a product project.

## Read-only commands

```powershell
dotnet run --project spikes/QuickPods.Spike.CoreAudio -c Release -- status
dotnet run --project spikes/QuickPods.Spike.CoreAudio -c Release -- watch --seconds 120
```

Output contains only a short SHA-256 endpoint hash. Full endpoint identifiers are retained in process memory only when required to restore the exact original endpoint.

## Hardware-changing commands

Stop playback before using a mutating command. Each mutating command requires an explicit confirmation flag, mutes the original endpoint before changing its volume, and restores the original endpoint's scalar volume followed by its original mute state.

```powershell
dotnet run --project spikes/QuickPods.Spike.CoreAudio -c Release -- pulse --percent 50 --confirm-playback-stopped
dotnet run --project spikes/QuickPods.Spike.CoreAudio -c Release -- exercise --iterations 1000 --delta-percent 1 --csv docs/validation/phase-0/core-audio/metrics.csv --confirm-playback-stopped
dotnet run --project spikes/QuickPods.Spike.CoreAudio -c Release -- key-latency --iterations 100 --confirm-playback-stopped
```

The mutation lease remains attached to the endpoint that was default when the command began. A default-device change aborts further mutations and restoration still targets the original endpoint; the new default endpoint is never used as a restoration target.

`key-latency` uses the public `SendInput` API to alternate bounded system volume-down/up key steps. It measures callback latency only for notifications classified as external, then restores and verifies the original endpoint state.

## Safety invariants

- The default render endpoint role is `Console`.
- COM objects are created, used, unregistered, and released on one explicit MTA worker.
- Native callbacks only copy bounded data, coalesce repeated signals, and queue non-blocking work.
- Volume notification mailboxes retain at most the latest value.
- Endpoint IDs are never printed or persisted.
- A failed or unverifiable restoration makes the command fail.
- Hardware-changing commands are never executed by CI.
