# QuickPods UI Mockup v2

![QuickPods Bluetooth audio selector](./QuickPods%20UI%20Mockup%20v2.png)

## Status

This mockup is the approved UI baseline for the initial release. It supersedes the AirPods-specific device card and fixed Bluetooth action in v1. The visual treatment is a reference; the state and safety rules in `QuickPods_Bluetoothオーディオ選択_機能仕様.md` are normative.

## Flyout anatomy

1. Header: `オーディオ` and a refresh action.
2. Bluetooth audio selector: one row for every paired Bluetooth audio device, grouped by Windows Container ID.
3. Selected row: radio semantics, accent outline, device icon, display name, pairing text, and verified connection state.
4. Volume area: the Windows default playback endpoint volume and mute state.
5. Primary action: `接続` or `切断` for the selected device, or a safe disabled/fallback action.
6. Footer: `サウンド設定を開く`.
7. Taskbar strip: compact master-volume control and the selected Bluetooth device name/state.

The three approved 2026-08-06 reference states refine this baseline: an empty-device card with a retry action, the populated radio list, and the compact taskbar strip directly below the flyout. The product window is a borderless flyout, not a conventional titled application window.

## Normative interaction rules

- Selecting a row changes only the selected target. It never connects the new target or disconnects the previous target automatically.
- Refresh re-enumerates state and invalidates stale observations. It never sends a KS connect/disconnect request.
- After a connect is verified, QuickPods sets that device's active render endpoint as the Windows default output and verifies the change before reporting the complete action as successful.
- The primary action is enabled only when the selected device has a verified, actionable state and the relevant operation is supported.
- During connection, disconnection, default-output switching, or an unknown state, duplicate operations are disabled.
- Volume and mute always control the current Windows default playback endpoint. After a successful Bluetooth connect flow, that endpoint is the selected Bluetooth device.
- New pairing and unpairing remain Windows Settings responsibilities.
- A short pointer hover over the QuickPods taskbar surface opens the flyout without stealing foreground focus. The flyout is centered over the QuickPods surface, not over the monitor, and a short leave grace period allows the pointer to move from the taskbar into the flyout.
- A taskbar click outside the slider opens and activates the same flyout. Slider drag, click-to-seek, wheel, and speaker mute remain direct taskbar operations.
- The empty catalog shows a headphones/Bluetooth glyph, `デバイスが見つかりません`, explanatory pairing text, and `再検索`.

## List presentation

- Use generic device-type glyphs such as headphones, headset, earbuds, or speaker. Do not require brand-specific artwork.
- Deduplicate A2DP, HFP, and endpoint variants into one row per physical Container ID.
- Show a bounded, scrollable list when all rows do not fit.
- Preserve keyboard focus and expose the list as a single-select radio group to UI Automation.
- Truncate long names visually while exposing the full display name to accessibility and tooltip text.
- Device names are presentation only; identity and commands use the stable internal Container ID-backed key.

## Display states

| Selected device state | Row/status | Primary action |
|---|---|---|
| No selection | Empty catalog uses `Bluetoothオーディオ`; a populated unselected catalog uses `Bluetoothオーディオを選択` | Disabled: `接続` |
| Disconnected and supported | `未接続` | `接続` |
| Connected and default output | `接続済み・既定` | `切断` |
| Connected but default switch pending | `既定の出力へ切替中` | Disabled |
| Connecting | `接続中` | Disabled |
| Disconnecting | `切断中` | Disabled |
| Unavailable or unknown | Short reason and retry affordance | Disabled until refresh resolves it |
| Direct control unsupported | `直接操作は未対応` | `Bluetooth設定を開く` |
| Default switch unsupported/failed | Connection remains honest; show a retry/settings action | `サウンド設定を開く` |

## Taskbar strip

The strip always shows speaker/mute, a short volume slider, the numeric percentage, a separator, and the Bluetooth device area. Examples are `AirPods Pro 未接続`, `Bluetooth Speaker 接続済み・既定`, and `BTデバイスなし`. The name may be elided in compact mode, but the verified state must remain distinguishable by icon, accessible name, and tooltip.
