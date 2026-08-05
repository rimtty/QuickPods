# Phase 0B Bluetooth KS 検証環境

## 記録情報

| 項目 | 値 |
|---|---|
| Status | **Partially validated** |
| 対象 | GitHub Issue #5 — Bluetooth KS接続・切断の成立性確認 |
| ブランチ | `codex/phase-0b-bluetooth-ks-spike` |
| 記録日 | 2026-08-05 |
| 実行日 | 2026-08-05～2026-08-06（読み取り専用・自動試験のみ） |
| 実行者 | Codex |
| 検証コミット | Phase 0Bブランチの当該実装コミット |

読み取り専用inventory、サニタイズ、純粋ロジック、Job Object watchdogの停止・異常終了シミュレーションまでは実行済みである。KS Basic Support、接続要求、切断要求は実行しておらず、実機のMMDevice状態遷移は未検証である。

2026-08-06の再検証時点では現在のWindowsセッションがRemote Audioだけを公開しており、BluetoothオーディオContainerは0件だった。この実行はIssue #20のEndpoint-scoped faultと非操作性の確認に使用し、AirPods実操作の証拠には使用しない。

## ホスト環境

| 項目 | 値 |
|---|---|
| OS | Windows 11 Pro 25H2 |
| OS build | `26200.8973` |
| Architecture | x64 |
| 実行権限 | Medium integrity、非管理者、非昇格 |
| .NET SDK / Runtime | SDK `10.0.302` / Microsoft.NETCore.App `10.0.10` |

管理者権限への昇格は試験手順に含めない。昇格しなければ成立しない結果はGate AのGo条件を満たさない。

## 対象ハードウェア

| 役割 | 製品・ドライバー | バージョン |
|---|---|---|
| 対象Bluetoothオーディオ機器 | AirPods Pro | 該当なし |
| Bluetoothアダプター | MediaTek Bluetooth Adapter | `1.1147.0.610` |
| Bluetoothオーディオドライバー | MediaTek Bluetooth Audio Device | `1.6.0.48` |

再ペアリング、Bluetooth無線全体のON/OFF、他のBluetooth機器の切断は通常の接続・切断反復試験に含めない。無線OFFは専用の異常系ケースとして、明示的な操作者確認のもとでのみ実施する。

## 識別情報の取り扱い

- 生のContainer ID、Endpoint ID、PnP ID、Bluetooth MACアドレスは、このディレクトリの文書、ログ、スクリーンショットへ保存しない。
- 検証コード内ではContainer IDを対応付けに利用してよいが、出力時はランダムなレポートセッションキーによる24桁HMACエイリアスへ置き換える。
- `inventory`が出力するセッショントークンは生識別子ではないが、実行レポート間の相関キーなのでコミットしない。
- Friendly Nameは製品名を確認する用途に限定し、ユーザー名、PC名、アカウント情報を出力しない。
- HRESULT、経過時間、MMDeviceの列挙状態、KSプロパティIDは診断情報として記録してよい。
- 証跡をコミットする前に、識別子と個人情報が含まれないことを再確認する。

## 実行前チェック

- [x] ブランチを記録した（コミットSHAはコミット後に確定）
- [x] 実行者と実行日時を記録した
- [x] .NET SDK / Runtimeを記録した
- [x] Medium integrityかつ非管理者であることを確認した
- [x] 2026-08-05の初回inventoryでAirPods Proのペアリング済みContainerを確認した（2026-08-06のRemote Audioセッションでは非公開）
- [ ] MediaTekの2つのドライバーバージョンを再確認した
- [x] Bluetooth無線がONであることを読み取り専用inventoryで確認した
- [x] 対象をレポートスコープのエイリアスへ割り当てられることを確認した
- [ ] 比較対象となる他のBluetooth機器の接続状態を、識別子を含めず記録した
- [ ] 音声再生や通話を停止し、接続・切断しても安全な状態にした
- [x] 出力ログのサニタイズとプロトコル必須フィールドを自動試験で確認した

## 子プロセスwatchdog方針

ドライバーまたはCOM呼び出しの停止が呼び出し元を拘束しないよう、二段の子プロセス隔離を使用する。

1. `inventory`、`probe`、`connect`、`disconnect`全体を外側の短命プロセスへ委譲し、kill-on-close Job Objectへ割り当てる。
2. 外側の上限はinventory 10秒、probe 15秒、接続・切断30秒とする。超過時は孫プロセスを含むツリーを終了し、部分出力を受理しない。
3. 各Basic Support／Reconnect／Disconnectは、1回の`KsProperty`だけを行う内側の短命プロセスで実行する。物理watchdogは4秒、論理watchdogは6秒で、同じ対象へ並列要求しない。
4. `inventory`を含む全診断コマンドをmachine-wide固定名のcross-process Mutexで囲み、固定名Jobへ複数のコマンドツリーを混在させない。別Windowsセッションからの同時要求、待機中または放棄されたMutexはfail closedで拒否する。
5. 内側のプロセスはversion、nonce、操作、対象エイリアス、同意フラグを結び付けた単一JSONフレームだけを受け付ける。実KS操作では同一実行ファイルの親も必須とする。
6. 外側のJob Objectにもmachine-wide固定名を付け、`ActiveProcesses == 0`を1秒以内に確認できない場合はcontainment failureとする。前所有者の異常終了後に名前付きJobが残る場合は、ツリーの終了確認ができるまで次のKS操作を開始しない。
7. KS HRESULTが`S_OK`であり、かつ250ms補助ポーリングでMMDevice実状態を15秒以内に確認した場合だけ成功とする。HRESULTと状態を別々に記録する。
8. タイムアウト、異常終了、nonce不一致、必須フィールド欠落、解析不能出力では自動再試行しない。
9. watchdogからBluetooth無線の切り替え、再ペアリング、管理者昇格を行わない。

watchdogによる強制終了が1回でも発生した場合、その試行を成功へ読み替えない。原因を記録し、安全に反復できない場合はGate AをNo-Goとする。
