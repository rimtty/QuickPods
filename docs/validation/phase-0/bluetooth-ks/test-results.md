# Phase 0B 自動・読み取り専用検証結果

## 結果概要

| 項目 | 結果 |
|---|---|
| 実行日 | 2026-08-05～2026-08-06 |
| ブランチ | `codex/phase-0b-bluetooth-ks-spike` |
| .NET | SDK `10.0.302` / Runtime `10.0.10` |
| Release build | Pass、警告0、エラー0 |
| 自動試験 | Pass、Bluetooth KS 65件 + solution smoke 1件 |
| format / diff check | Pass |
| 読み取り専用inventory | Pass |
| KS Basic Support | Pass（Reconnect Render、Disconnect Render/Capture） |
| Reconnect / Disconnect | Gate A Go（有効な接続5/5、切断5/5） |

## 初回の読み取り専用inventory（2026-08-05）

`inventory`は外側の隔離プロセスとkill-on-close Job Object内で実行した。`IKsControl::KsProperty`は呼び出していない。

- AirPods audioのContainerを1件識別した。
- EndpointはRender 1件、Capture 1件で、観測時はいずれも`Unplugged`だった。
- DeviceTopology connector 0からRender候補1件、Capture候補1件を区別した。
- 操作対象の選択規則はRender候補がContainer内で1件、かつ全Containerで所有が一意の場合だけ許可する。
- Friendly Nameを取得できないEndpointは表示専用faultとして記録し、Topology所有権へ影響するfaultは観測されなかった。
- セッショントークンと24桁エイリアスは実行ごとに変わるため、本書には値を保存していない。
- 生のContainer ID、Endpoint ID、Adapter/PnP ID、MACアドレスは標準出力、標準エラー、文書へ保存していない。

## Issue #20修正後の読み取り専用inventory（2026-08-06）

現在のセッションはRemote Audioだけを公開しており、BluetoothオーディオContainerは0件だった。この構成で再実行し、次を確認した。

- `PKEY_Device_ContainerId`欠落は所有権へ影響しないglobal faultとして分類した。
- `IDeviceTopology`の`E_NOINTERFACE`は、失敗したEndpointのレポートスコープHMAC別名へ限定されたownership faultとして分類した。
- 生Endpoint ID、Container ID、PnP ID、MACアドレスは出力しなかった。
- targetが存在しないためKS Basic Support、Reconnect、Disconnectは0件だった。
- scoped faultまたは未帰属Adapterが選択対象と重ならない場合は完全な対象を妨げず、選択対象と重なる場合またはglobal faultの場合は従来どおりKS子プロセス開始前に拒否することを自動試験で確認した。

## 自動試験で確認した安全条件

- 引数確認前に探索またはKS操作を開始しない。
- 未知target、複数Render候補、複数Containerで共有される候補をfail closedで拒否する。
- globalまたは選択対象に重なる所有権fault、および選択対象と重なる未帰属AdapterではKS子プロセスを開始しない。
- 無関係EndpointへスコープされたTopology faultや無関係な未帰属Adapterは、完全に証明できた選択対象の操作能力へ波及させない。
- 0／1／複数／同名のBluetoothオーディオ候補をContainer単位へ集約し、Stereo/A2DPとHands-Free/HFPを1物理機器へまとめる。
- 選択と更新をKS操作層から構造的に分離し、選択機器が一時消失しても永続キーを保持する。
- Basic Supportの片方でも非対応ならReconnect／Disconnectを発行しない。
- KS HRESULTが失敗の場合、MMDeviceが期待状態へ変化しても成功扱いしない。
- 操作後の観測に所有権faultまたはContainer未帰属Adapterがあれば、残存EndpointがUnpluggedでも`Unknown`へ倒す。
- 操作前から目的状態だった試行は`AlreadyInDesiredState`とし、有効成功へ数えない。
- 同一Containerの操作は完全直列化し、古いgenerationの結果を破棄する。
- machine-wide固定名のcross-process Mutexにより全診断コマンドを直列化し、2つの実行プロセスを競合させた場合は2つ目が固定名Jobへ合流する前に拒否される。
- Mutex所有者の異常終了と待機cancelでは操作delegateを実行せず、名前付きJobの回収を確認するまで次の操作を拒否する。
- 停止子プロセスはJob Objectで終了し、PIDが消滅したことまで確認する。
- 名前付きJob内に実際の孫プロセスを残すsimulationで、`ActiveProcesses == 0`まで回収することを確認する。
- 同名Jobが既に存在する場合は参加・再設定せず、コマンド開始前に拒否する。
- 子の異常終了、欠落JSON、nonce／操作不一致、同意フラグ不足を成功へ読み替えない。
- `--ks-child`の未定義数値を実操作へフォールスルーさせない。
- 実KS子プロセスは、同一実行ファイルの親と操作に対応した同意capabilityを必要とする。

## 2026-08-06 操作者確認付き実機KS

- 初回Reconnectは`S_OK`後13.647秒でRender/Capture Activeとなり、Windows画面でもAirPods接続と選択外Bluetooth機器の維持を確認した。
- 旧Disconnect判定はRenderの一時Unpluggedだけで成功を返し、その後Activeへ戻る欠陥があった。別試行ではRenderだけがUnplugged、CaptureがActiveの部分切断も確認した。
- 切断対象を選択Container所有のRender/Capture両KS候補へ拡張し、切断観測も両flowを含め、5秒の連続非Activeを必須とした。期限切れ時に最後の一時状態で成功しない条件も追加した。
- 修正版Disconnectは7.167秒で両EndpointのUnpluggedを5秒維持しPassした。
- 直後のReconnectはKS `S_OK`でも15秒以内にActiveにならず`DeadlineExceeded`となった。AirPodsを再度到達可能にした上で反復を継続する。
- ペアリング済み・未接続かつ装着中の状態から再試行し、Reconnectは8.178秒でRender／Capture Activeとなった。続く修正版Disconnectは7.969秒で両Endpoint Unpluggedを5秒維持し、直後の新規inventoryでも両flowがUnpluggedだった。操作者もWindows画面で切断を確認した。
- 同じ到達可能期間の追加往復はReconnect 10.761秒、Disconnect 5.865秒でPassした。直後に接続待ちを再確認せず送ったReconnectはKS `S_OK`でも15秒間Unpluggedを維持し、`DeadlineExceeded`として終了した。自動再試行もDisconnectも送らず、この試行は到達可能前提を証明できないため`Blocked`とした。
- 操作者の次の準備後はinventory時点ですでにRender／Capture Activeだったため、Reconnectを送らず15msで`AlreadyInDesiredState`とした。この観測は接続試行に含めず、続くDisconnectだけを実施し、7.266秒で両Endpoint Unpluggedを5秒維持してPassした。
- 未接続かつ接続待ちを再確認した次の試行はReconnect 11.292秒でRender／Capture Active、続くDisconnect 5.721秒で両Endpoint Unpluggedを5秒維持し、ともにPassした。
- 最終の未接続・接続待ち確認済み試行はReconnect 9.256秒でPassし、直後の新規inventoryでもRender／Capture Activeを確認した。最終状態は操作者確認のため接続済みのまま残した。
- この実機不具合に直接対応する回帰試験2件だけを追加し、Bluetooth KS 65件が合格した。

## 後続の統合試験

修正版の有効試行は接続5回中5回、切断5回中5回がPassし、各15秒以内だった。テスト棚卸しにより同一実機の反復基準は各5回へ半減済みである。操作者は最終接続と選択外Bluetooth機器への影響0件を確認した。全Go条件が揃ったためGate AはGoとする。到達不能、他端末接続中、無線OFFはPhase 4で製品UIの部分状態・回復導線と合わせて確認する。
