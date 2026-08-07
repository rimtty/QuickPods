# QuickPods 0.1.0 compatibility

## Supported baseline

- Windows 11 x64, build 22000以降
- standard user権限。管理者権限は要求しない
- Windows 11 taskbarの中央揃え
- default render endpointのmaster volume／mute
- ユーザー単位の通知領域常駐と任意のログイン時自動起動

## Hardware-dependent capabilities

Bluetooth一覧はpaired association endpoint、MMDevice、Container IDの一致を読み取り専用で確認する。直接の接続／切断は対象driverが安全なKS Reconnect／Disconnect propertyを両方向とも公開する場合だけ有効になる。接続後の既定出力化は対象stereo render endpointを一意に確認できた場合だけConsole／Multimediaへ適用する。未対応または不確実な機器ではWindows Bluetooth／Sound settingsを案内し、推測したmutationを送らない。

参照するAirPods／MediaTek環境では、Issue #38で単一行列挙、同一ContainerのRender／Capture集約、選択復元と外部状態追従を確認し、Issue #41で直接接続／切断、Console／Multimedia既定出力、選択外影響0を確認した。別のBluetooth driverは同じ機器単位capability判定を通り、未対応ならWindows設定へ縮退する。RDPのRemote AudioはローカルBluetooth inventoryではないため、RDP中は物理対応判定を行わない。

## Display baseline

タスクバー表示は中央揃えだけをsupportする。左揃え、taskbar evidenceが不完全な状態、空き領域が安全に証明できない状態では、taskbar surfaceを表示せず通知領域へfallbackする。100～250%、mixed DPI、High Contrast、monitor hot-plugの最終matrixはIssue #43で確認する。
