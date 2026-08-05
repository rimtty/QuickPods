# Core Audio spike environment

| Item | Observed value |
|---|---|
| Date | 2026-08-06 (Asia/Tokyo) |
| Branch | `codex/phase-0a-core-audio-spike` |
| Tracking issue | [#4](https://github.com/rimtty/QuickPods/issues/4) |
| Deferred multi-device check | [#19](https://github.com/rimtty/QuickPods/issues/19) |
| Windows edition | Windows 11 Pro 25H2 |
| Windows build | 26200.8973 |
| Process architecture | x64 |
| .NET SDK | 10.0.302 |
| Integrity/elevation | Medium integrity, non-administrator |
| Endpoint role | `eRender` / `eConsole` |
| Default endpoint state | Active (`0x00000001`) |
| Sanitized endpoint hash | `9EDC11A34A50` |
| Available render endpoints | One output exposed by Windows (`Remote Audio`) |
| Baseline state | 64%, unmuted |

The hash is the first 12 hexadecimal characters of SHA-256 over the endpoint ID. The full endpoint ID was not written to logs or documentation.

Windows Settings exposed only one render endpoint during the final session. A physical A → B → A default-endpoint switch was therefore not possible; the implementation/design evidence is accepted for Gate P0-A and hardware corroboration is retained in issue #19.
