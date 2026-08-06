# Phase 2 — Audio MVP validation

## Environment

| Item | Value |
|---|---|
| Date | 2026-08-06 |
| OS | Windows 11 Pro 25H2, build 26200.8973 |
| Architecture | x64 |
| Build | Release, .NET 10.0.302 |
| Issues | #27, #29, #31 |

## Automated evidence

| Check | Result |
|---|---|
| Release solution build | Pass — 0 warnings, 0 errors |
| Focused foundation tests | Pass — 23/23 |
| Rapid slider input | Pass — 100 previews, bounded writes, final value 73 |
| Generation | Pass — retired generation cannot overwrite current endpoint |
| External notification | Pass — state changes without feedback write |
| Self notification | Pass — optimistic preview is not rolled back |
| Faulted shutdown | Pass — write error reaches the operation once; subsequent disposal succeeds (Issue #29) |
| Event-context isolation | Pass — each product audio-port instance owns a unique Core Audio context and cannot suppress another process's notification (Issue #31) |
| Format / diff check | Pass |

## Initial live observation

The product diagnostic WPF executable started normally and remained responsive. Read-only Windows UI Automation and the independent Phase 0 Core Audio Spike agreed:

| Field | Product UI | Independent read |
|---|---|---|
| Default output | `スピーカー (3- Logitech PRO X Wireless Gaming Headset)` | Active Console endpoint, hash only in Spike output |
| Volume | 12% | 12.0% |
| Mute | Off | `false` |
| Capability | Available | `available=true`, `device_state=0x00000001` |

This proves AC-001 for the current endpoint without changing OS state.

## Live mutation gate

Playback must be stopped before this gate. Record and restore the original default endpoint, volume, and mute state.

- [x] AC-002: QuickPods set 0%, 50%, and 100%; independent Core Audio reads were exactly 0.0%, 50.0%, and 100.0%. Mute stayed enabled during the safety sequence. This supplements the Phase 0 Windows standard-UI scalar comparison.
- [x] AC-003: QuickPods toggled mute off and on; the UI and independent reads agreed at each step.
- [x] AC-004: Windows-side manual changes to 65%, 50%, mute, and 100% reached QuickPods. A three-step system volume-key regression also restored to 12% without `再取得` after Issue #31 was fixed.
- [x] AC-005: Console changed from Logitech to `DELL U3219Q (NVIDIA High Definition Audio)` and back. QuickPods rebound without restart and displayed each endpoint's own volume/mute state. Notification and read-back passed; Communications was unchanged.
- [x] AC-006: the default-endpoint round trip invalidated and retired the original binding, created a new binding, and restored the original binding without a crash or loss of responsiveness.
- [ ] Rapidly drag/click the slider and confirm no visible rollback or flicker
- [x] Close the window normally and confirm `QuickPods.App` exits without a residual process

The original state was restored after all mutations: Logitech Console/Multimedia default, 12%, mute off. The diagnostic window was then closed normally and the residual `QuickPods.App` process count was zero.

Gate status: **Pending one visual rapid-input check**.

## Issue #31 regression

Before the fix, the product and Phase 0 Spike shared one fixed event-context GUID. The Spike's final restoration write was therefore classified as product-originated and ignored: Core Audio had returned to 12%, while QuickPods remained at 10% for more than two seconds.

The product now generates one context per `WindowsCoreAudioEndpointPort` instance. The focused regression test verifies instance uniqueness and ownership matching. Repeating the same three system volume-key operations produced `restored=true`, an external-notification p95 of 6.665 ms, and matching final product/Core Audio state of 12%, mute off, without refresh.

## UI layout regression

At the live DPI, the original fixed-height window clipped the action row. The window now sizes vertically to its content and the card row is not shrinkable. Release build passed, UI Automation reported every action/status element inside the window bounds, and the corrected build was accepted by user screenshot review.
