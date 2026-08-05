# Gate A — Bluetooth KS 判断記録

## 現在の判断

| 項目 | 値 |
|---|---|
| Status | **Pending** |
| 対象 | GitHub Issue #5 |
| 対象ブランチ | `codex/phase-0b-bluetooth-ks-spike` |
| 判断 | 未判定 |
| 判断日 | 未定 |
| 判断者 | 未記入 |

読み取り専用探索、Render候補の一意選択、純粋ロジック、Job Object watchdogシミュレーションは確認済みである。KS Basic Support、Reconnect、Disconnect、実機のMMDevice状態遷移、他機器への影響は未実行なので、GoまたはNo-Goを決める証拠はまだ不足している。

## Gate Aの目的

MediaTek Bluetooth Adapter／Bluetooth Audio DeviceとAirPods Proの組み合わせで、Bluetooth無線全体や他の機器へ影響せず、非管理者プロセスから対象Bluetoothオーディオ接続だけを安全かつ再現可能に接続・切断できるかを確定する。

KS要求のHRESULTが成功しても、実際の接続・切断成功とはみなさない。対象のMMDevice実状態が期限内に変化した場合だけ、その試行を成功とする。

## Go条件

次をすべて満たした場合だけGoとする。

1. Medium integrityの非管理者プロセスでReconnectとDisconnectの両方を実行できる。
2. 両プロパティのBasic Supportを確認できる。
3. 接続は有効10回中9回以上、各15秒以内に`DEVICE_STATE_ACTIVE`を確認できる。
4. 切断は有効10回中9回以上、各15秒以内に非Activeを確認できる。
5. KS要求成功を、実状態未変化のまま成功表示した件数が0である。
6. キーボード、コントローラー等、対象外Bluetooth機器への影響が0件である。
7. 子プロセスwatchdogが停止・異常終了を捕捉し、親プロセスを拘束しない。
8. 証跡に生のContainer ID、Endpoint ID、PnP ID、MACアドレスまたは個人情報が含まれない。

接続または切断の片方向だけが成功する場合はConditional Goにせず、直接操作全体をNo-Goとする。

## No-Go条件

次のいずれかに該当した場合はNo-Goとする。

- ReconnectまたはDisconnectのBasic Supportを確認できない。
- どちらかの成功率が有効10回中9回を下回る。
- 15秒以内の実状態確認を安定して行えない。
- KS要求結果と実状態を区別できない。
- 対象外Bluetooth機器へ影響する。
- 管理者権限、Bluetooth無線全体の切り替え、再ペアリングが通常操作に必要となる。
- watchdogで隔離しても安全に反復できない停止またはクラッシュが発生する。

## 現在の証跡

| 検証項目 | 結果 | 備考 |
|---|---|---|
| 読み取り専用探索 | Pass | AirPods Container、Render/Capture、connector 0候補をreport-scoped aliasで識別 |
| 自動安全試験 | Pass | Bluetooth KS 56件、solution smoke 1件 |
| Reconnect Basic Support | Pending | 未実行 |
| Disconnect Basic Support | Pending | 未実行 |
| 接続10回 | Pending | 0回実行 |
| 切断10回 | Pending | 0回実行 |
| 実状態確認 | Pending | 未観測 |
| 誤成功表示 | Pending | 未観測 |
| 他機器影響 | Pending | 未観測 |
| 非管理者での実操作 | Pending | 実操作未実行 |
| 子プロセスwatchdog | Pass（simulation） | kill-on-close Job Object、Job全体の空状態、孫PID消滅、異常終了、machine-wide排他を確認 |
| 異常系 | Pending | 未実行 |

したがってGate AはPendingのままとし、未実行項目を成功として扱わない。

## No-Go時の製品縮退

Gate AがNo-Goの場合も、QuickPodsのCore Audio音量制御、タスクバー表示、通知領域、設定機能の開発は継続する。

- 製品のBluetoothボタンは直接Reconnect／Disconnectを実行しない。
- ボタンまたはエラー導線からWindows Bluetooth設定の`ms-settings:bluetooth`を開く。
- UIには直接操作が未対応であることを表示し、接続済みと誤表示しない。
- Bluetooth無線の自動切り替え、再ペアリング、外部スクリプトによる代替操作を行わない。
- KS直接操作を後日再検証できるよう、OS buildとドライバーバージョンを互換性記録へ残す。

## watchdogを判定に含める理由

KSまたはCOM呼び出しが停止した場合でも常駐アプリ本体を巻き込まないことは、機能成立性と同じく必須である。探索を含むコマンド全体はkill-on-close Job Object内で実行し、上限はinventory 10秒、probe 15秒、接続・切断30秒とする。各`KsProperty`はさらに4秒の内側watchdogで隔離し、接続・切断の実状態確認期限は15秒とする。

watchdogによる終了、子プロセス異常終了、解析不能な応答は成功ではない。自動再試行せず、別の観測プロセスで現在状態を確認してから操作者判断で次へ進む。子プロセス終了コード`0`も単独では成功根拠にならない。

## 判断更新手順

1. `environment.md`の実行前チェックを完了する。
2. `test-matrix.md`の探索、Basic Support、watchdog試験を実施する。
3. 接続・切断の有効試行を各10回記録する。
4. 異常系と他機器影響を確認する。
5. 証跡をサニタイズし、禁止識別子がないことを確認する。
6. Go／No-Go条件と照合し、この文書へ判断者、日付、根拠コミットを記入する。
7. No-Goの場合は`ms-settings:bluetooth`への縮退を後続実装の確定要件にする。
