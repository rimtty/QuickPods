# Gate A2 — 既定出力設定Spike計画

## Status

**In progress**。`codex/phase-0d-default-endpoint-policy-spike`で、OS変更を行わない隔離契約と状態機械を実装した。Windows COMアダプター、明示確認付き診断CLI、実機Gateは未実装／未実施である。Bluetooth Gate Aが選択Containerの接続済みActiveステレオ再生Endpointを確定するまでは、実OSの既定出力を変更しない。

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

| ID | 操作 | 合格条件 | 状態 |
|---|---|---|---|
| DEP-001 | Active Endpoint A→B | Console／MultimediaだけがBになり通知と再取得が一致 | 契約Pass、実機Pending |
| DEP-002 | Bが既に既定 | 冪等に成功し、不要な通知ループがない | 契約Pass、実機Pending |
| DEP-003 | Bluetooth接続確認直後 | 同じContainerのステレオEndpointを選びHands-Freeを選ばない | 契約Pass、実機Pending |
| DEP-004 | 設定中に対象消失 | 完全成功にせず、部分状態と回復導線を返す | 世代変更Pass、対象消失Pending |
| DEP-005 | 1ロール目成功、2ロール目失敗 | 部分適用を検出し、再取得結果を正確に返す | 契約Pass、実機Pending |
| DEP-006 | 古いカタログ世代 | 現在の選択へ結果を誤適用しない | Pass |
| DEP-007 | A→B→Aを10回 | クラッシュ、ハンドル増加、状態ずれがない | Pending |

## 実装済み契約

- Endpoint／Containerの内部ハンドルは`ToString()`で実IDを露出しない。
- 対象解決は選択Containerに属するActive／Render／Stereoの一意候補だけを受理する。
- Hands-Free、Capture、非Active、0件、複数件は選択しない。
- 変更対象はConsoleとMultimediaに固定し、Communicationsの変更を検出した場合は成功にしない。
- 書き込み受理だけでは成功にせず、同一世代の通知と再取得の両方を要求する。
- 途中拒否、通知／再取得不一致、世代更新は、既に受理された変更を含む証拠を保持して完全成功と区別する。
- 既に両ロールが対象の場合は書き込みも通知待機も行わない。

## 成果物

- 隔離された`IDefaultAudioEndpointPolicy`契約とSpike実装
- 自動試験とサニタイズ済み実機ログ
- Windowsビルド、実行権限、Endpoint構成
- Go／No-Go判断と製品フォールバック仕様
