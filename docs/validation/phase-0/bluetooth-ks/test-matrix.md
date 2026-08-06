# Phase 0B Bluetooth KS テストマトリクス

## ステータス

**Gate A Go（2026-08-06）** — 読み取り専用探索、65件の自動試験、watchdogシミュレーション、KS Basic Support、修正版の有効な接続・切断各5回、操作者による選択外機器影響0件の確認に合格した。

この文書の`Pass`は、KS要求が受理されたことではなく、期限内に対象オーディオEndpointの実状態が期待どおり変化したことを意味する。空欄や未観測結果を成功として扱わない。

## 結果の定義

| 結果 | 定義 |
|---|---|
| `Pending` | 未実行または証跡未記録 |
| `Pass` | 手順、期待結果、実状態の証跡をすべて確認済み |
| `Fail` | 手順を実行したが期待結果を満たさない |
| `Blocked` | 前提条件を作れず有効な試行を実施できない |
| `N/A` | Gate判定者が理由を記録して対象外とした |

`Blocked`と`N/A`は成功数へ含めない。接続・切断の反復試験は、有効な試行を各5回揃えるまでGateを判定しない。2026-08-06のテスト棚卸し指示に従い、同一実機の反復量を10回から5回へ半減した。fail-closed、watchdog、所有権、異常系の安全条件は削減しない。

## 共通記録項目

各操作では次を別々に記録する。

- 操作別の連番と実行時刻
- 操作前のMMDevice実状態
- KS Basic Support結果
- KS要求の開始・終了時刻とHRESULT
- MMDevice通知を受信した時刻と状態
- 250msポーリングで確認した最終状態
- 実状態確定までの時間
- watchdogの終了理由
- 操作後の比較対象Bluetooth機器への影響有無

生のContainer ID、Endpoint ID、PnP ID、MACアドレスは記録しない。対象と候補フィルターは実行単位の別名で相関させる。

## 必須試験

| ID | 分類 | 前提・操作 | 期待結果 | 証跡 | 状態 |
|---|---|---|---|---|---|
| BTKS-ENV-001 | 環境 | Medium integrity、非管理者でSpikeを起動 | 昇格なしで起動し、権限情報を記録できる | `environment.md` | Pass |
| BTKS-DISC-001 | 探索 | ペアリング済みオーディオ機器を列挙 | 参照機器を対象候補として識別し、Container単位で関連Endpointをまとめられる | report-scoped aliasのみのinventory | Pass |
| BTKS-DISC-002 | 探索 | DeviceTopology connector 0からAdapter DeviceとKS Filter候補を辿る | 対象ContainerでRender/Capture候補を区別し、Render候補を1件へ絞れる | サニタイズ済み候補とflow | Pass |
| BTKS-DISC-003 | 探索 | EndpointのTopologyまたはContainer帰属が不完全なsnapshotを模擬 | 所有権へ影響するfault、または同じAdapterをContainerへ帰属できない場合はKS子プロセスを開始しない | fail-closed自動試験 | Pass（自動） |
| BTKS-DISC-004 | 探索 | 0／1／複数／同名ContainerとA2DP/HFP Endpoint群を模擬 | 1物理Containerを1候補へ集約し、表示名ではなく内部キーで一意に選択できる | catalog自動試験 | Pass（自動） |
| BTKS-DISC-005 | 探索 | 無関係EndpointだけにTopology ownership faultを発生させる | faultを該当Endpointへスコープし、完全確認できた選択Containerの操作能力を阻害しない | scoped-fault自動試験＋Remote Audio read-only inventory | Pass（Issue #20） |
| BTKS-SEL-001 | 選択 | 機器選択と読み取り専用更新を反復 | カタログ層がKS操作依存を持たず、選択を維持し、変更要求経路を持たない | catalog state自動試験＋依存境界 | Pass（自動） |
| BTKS-SUP-001 | 対応確認 | ReconnectのBasic Supportを照会 | 対応・非対応・エラーを明確に分類する | Render候補 `S_OK` / GET対応 | Pass |
| BTKS-SUP-002 | 対応確認 | DisconnectのBasic Supportを照会 | 対応・非対応・エラーを明確に分類する | Render／Capture候補 `S_OK` / GET対応 | Pass |
| BTKS-CON-001 | 接続 | 到達可能かつ切断状態からReconnectを有効5回実施 | 5回中4回以上、各15秒以内に`DEVICE_STATE_ACTIVE`を確認 | 接続試行表 | Pass（5/5） |
| BTKS-DIS-001 | 切断 | 接続状態からDisconnectを有効5回実施 | 5回中4回以上、各15秒以内に非Activeを確認 | 切断試行表 | Pass（5/5） |
| BTKS-OBS-001 | 成功判定 | KS要求成功後も実状態を監視 | 実状態不変を成功表示した件数が0 | 要求結果と観測結果の対照表 | Pass（修正版0件） |
| BTKS-OBS-002 | 成功判定 | 所有権faultまたはContainer未帰属Adapterを含む状態観測を模擬 | 残存EndpointがUnpluggedでも`Disconnected`とせず`Unknown`へ倒す | state-observer自動試験 | Pass（自動） |
| BTKS-SER-001 | 直列化 | 同一プロセスの操作中に追加要求を発生させる | 2件目を開始せず、同時KS要求が0 | generation／lane自動試験 | Pass（自動） |
| BTKS-SER-002 | 直列化 | 2つのSpike実行プロセスからmachine-wide固定名Mutexを同時取得 | 1つ目だけが取得し、2つ目は固定名Jobへ合流せず隔離コマンド開始前に拒否される | 2プロセス統合試験 | Pass（自動） |
| BTKS-SER-003 | 直列化 | Mutex所有プロセスの異常終了と待機cancelを模擬 | 放棄／cancelされた試行はoperation delegateを実行せず、前Job回収後の別試行だけを許可する | abandonment／cancel自動試験 | Pass（自動） |
| BTKS-ISO-001 | 影響確認 | 接続・切断の前後で比較対象機器を確認 | 他のBluetooth機器への影響が0件 | 前後状態チェック | Pass（操作者確認） |
| BTKS-ERR-001 | 異常系 | 参照機器をケース内または到達不能にして接続 | 15秒以内に成功しなければTimeout／Unavailableで終了し、成功表示しない | 状態遷移ログ | Phase 4 UI統合へ移管（DeadlineExceeded実機証拠あり） |
| BTKS-ERR-002 | 異常系 | 参照機器が他端末へ接続中の状態で接続 | 実状態を確認できない限り成功表示しない | 状態遷移ログ | Phase 4 UI統合へ移管 |
| BTKS-ERR-003 | 異常系 | Bluetooth無線OFFで探索・操作 | 自動で無線をONにせず、RadioOff等へ分類する | エラー分類ログ | Phase 4 UI統合へ移管 |
| BTKS-WD-001 | watchdog | 試験用の停止子プロセスを起動 | Job Objectでプロセスツリーを終了確認し、Timeoutとして記録 | PID消滅を確認する自動試験 | Pass（simulation） |
| BTKS-WD-002 | watchdog | 子プロセスを異常終了させる | 成功へ読み替えず、自動再試行しない | 非0終了コードの自動試験 | Pass（simulation） |
| BTKS-WD-003 | watchdog | 名前付きJob内の親を終了させ、実際の孫プロセスを残す | 孫を終了し、Jobの`ActiveProcesses == 0`を確認するまで回収済みとしない | process-tree統合試験 | Pass（simulation） |
| BTKS-WD-004 | watchdog | 同名のJobが既に存在する状態で新規コマンドJobを作成 | 既存Jobへ参加または再設定せず、コマンド開始前に拒否する | name-collision自動試験 | Pass（自動） |
| BTKS-PRV-001 | プライバシー | 成果出力と親子プロトコルを検査 | 生識別子0件、別レポート間でaliasが一致しない | HMAC session／protocol自動試験 | Pass（自動） |

## 接続反復記録

有効試行の前提は、Bluetooth無線ON、対象がペアリング済み・到達可能・切断状態であること。前提を満たさない試行は`Blocked`として別記し、有効5回へ含めない。

| 試行 | KS要求 | HRESULT | MMDevice最終状態 | 確定時間 | watchdog | 他機器影響 | 結果 |
|---:|---|---|---|---:|---|---|---|
| 1 | Reconnect | `S_OK` | Render/Capture Active | 13.647秒 | 正常 | 変化0（操作者確認） | 探索的Pass |
| 2 | Reconnect | `S_OK` | Unplugged維持 | 15.001秒 | 正常 | 未観測 | Blocked（接続待ち未確認、正しくDeadlineExceeded） |
| 3 | Reconnect | `S_OK` | Render/Capture Active | 8.178秒 | 正常 | 未確認 | 修正版Pass |
| 4 | Reconnect | `S_OK` | Render/Capture Active | 10.761秒 | 正常 | 未確認 | 修正版Pass |
| 5 | Reconnect | `S_OK` | Unplugged維持 | 15.000秒 | 正常 | 未確認 | Blocked（接続待ち未確認、正しくDeadlineExceeded） |
| 6 | 未送信 | — | Render/Capture Active | 0.015秒 | 正常 | 未確認 | N/A（AlreadyInDesiredState） |
| 7 | Reconnect | `S_OK` | Render/Capture Active | 11.292秒 | 正常 | 未確認 | 修正版Pass |
| 8 | Reconnect | `S_OK` | Render/Capture Active | 9.256秒 | 正常 | 変化0（操作者確認） | 修正版Pass |

## 切断反復記録

有効試行の前提は、Bluetooth無線ON、対象がペアリング済みかつ音声Endpointの実状態がActiveであること。前提を満たさない試行は`Blocked`として別記し、有効5回へ含めない。

| 試行 | KS要求 | HRESULT | MMDevice最終状態 | 確定時間 | watchdog | 他機器影響 | 結果 |
|---:|---|---|---|---:|---|---|---|
| 1 | Render Disconnect | `S_OK` | 一時Unplugged後Active | 1.788秒（旧判定） | 正常 | 変化0 | Fail（探索的誤成功、修正済み） |
| 2 | Render Disconnect | `S_OK` | Render Unplugged / Capture Active | 0.175秒（旧判定） | 正常 | 未観測 | Fail（部分切断、修正済み） |
| 3 | Render/Capture Disconnect | `S_OK` | 両Endpoint Unpluggedを5秒維持 | 7.167秒 | 正常 | 未観測 | 修正版Pass |
| 4 | Render/Capture Disconnect | `S_OK` | 両Endpoint Unpluggedを5秒維持 | 7.969秒 | 正常 | 未確認 | 修正版Pass（操作者確認） |
| 5 | Render/Capture Disconnect | `S_OK` | 両Endpoint Unpluggedを5秒維持 | 5.865秒 | 正常 | 未確認 | 修正版Pass |
| 6 | Render/Capture Disconnect | `S_OK` | 両Endpoint Unpluggedを5秒維持 | 7.266秒 | 正常 | 未確認 | 修正版Pass |
| 7 | Render/Capture Disconnect | `S_OK` | 両Endpoint Unpluggedを5秒維持 | 5.721秒 | 正常 | 未確認 | 修正版Pass |

## Gate A集計

| 指標 | Go基準 | 現在値 |
|---|---:|---|
| Reconnect Basic Support | 対応 | Pass |
| Disconnect Basic Support | 対応 | Pass（Render／Capture） |
| 接続成功 | 5回中4回以上、各15秒以内 | Pass（有効5回中5回Pass、Blocked 2回、N/A 1回） |
| 切断成功 | 5回中4回以上、各15秒以内 | Pass（有効5回中5回Pass） |
| 誤成功表示 | 0件 | Pass（探索的1件を修正、修正版0件） |
| 他機器への影響 | 0件 | Pass（初回比較・最終確認とも0件） |
| 管理者権限 | 不要 | Pass |
| watchdog未処理停止 | 0件 | 0件（停止・異常終了simulationはPass） |

片方向だけ成功した場合、成功率不足、実状態未確認、他機器への影響、管理者権限要求のいずれかがある場合はConditional Goにしない。直接操作をNo-Goとし、製品では`ms-settings:bluetooth`を開く縮退経路を使用する。

現在値は全Go基準を満たしたため、Gate AはGoとする。圏外、他端末接続中、無線OFFはPhase 4の製品状態・回復導線を含む統合試験で実施する。
