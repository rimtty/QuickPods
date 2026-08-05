# Gate A2 — 既定出力設定Spike計画

## Status

**Pending**。Bluetooth Gate Aで選択ContainerのActiveステレオ再生Endpointを一意に特定できる設計を確認した後、`codex/phase-0d-default-endpoint-policy-spike`で実施する。

## 目的

QuickPodsの`接続`操作でBluetooth音声接続を確認した後、同じ物理Containerのステレオ再生EndpointをWindowsのConsole／Multimedia既定出力へ設定し、結果を独立して検証できるかを確定する。

Microsoft Learnで公開されているCore Audio APIは既定Endpointの取得と変更通知を提供するが、一般デスクトップアプリ向けの設定メソッドは公開文書化されていない。第一候補として非公開COM境界`IPolicyConfig::SetDefaultEndpoint`を検証する。COM宣言とCLSIDsをWindows統合アダプターへ隔離し、OS更新で利用できない可能性を製品状態として扱う。

## Gate条件

### Go

- Medium integrityの通常ユーザープロセスで動作する。
- 選択ContainerのActiveなステレオ再生Endpointだけを指定する。
- Console／Multimediaの変更後、`OnDefaultDeviceChanged`と`GetDefaultAudioEndpoint`の双方が対象IDに一致する。
- Capture、Communications、選択外Endpointを変更しない。
- A→Bluetooth→A、既に既定、対象消失、途中失敗を安全に処理する。
- 生Endpoint IDをログやUIへ保存しない。

### No-Go

既定出力の自動設定だけを無効にする。Bluetooth接続結果を`接続済み・非既定`として表示し、`ms-settings:sound`または対象Endpointの設定ページへ案内する。接続成功を失敗と偽装せず、既定出力設定を成功とも偽装しない。

## 最小試験マトリクス

| ID | 操作 | 合格条件 |
|---|---|---|
| DEP-001 | Active Endpoint A→B | Console／MultimediaだけがBになり通知と再取得が一致 |
| DEP-002 | Bが既に既定 | 冪等に成功し、不要な通知ループがない |
| DEP-003 | Bluetooth接続確認直後 | 同じContainerのステレオEndpointを選びHands-Freeを選ばない |
| DEP-004 | 設定中に対象消失 | 完全成功にせず、部分状態と回復導線を返す |
| DEP-005 | 1ロール目成功、2ロール目失敗 | 部分適用を検出し、再取得結果を正確に返す |
| DEP-006 | 古いカタログ世代 | 現在の選択へ結果を誤適用しない |
| DEP-007 | A→B→Aを10回 | クラッシュ、ハンドル増加、状態ずれがない |

## 成果物

- 隔離された`IDefaultAudioEndpointPolicy`契約とSpike実装
- 自動試験とサニタイズ済み実機ログ
- Windowsビルド、実行権限、Endpoint構成
- Go／No-Go判断と製品フォールバック仕様
