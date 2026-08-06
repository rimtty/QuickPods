# Gate A — Bluetooth KS 判断記録

## 現在の判断

| 項目 | 値 |
|---|---|
| Status | **Go** |
| 対象 | GitHub Issue #5 |
| 対象ブランチ | `codex/phase-0b-bluetooth-ks-spike` |
| 判断 | Container単位の直接Reconnect／Disconnectを機器別Capabilityとして採用 |
| 判断日 | 2026-08-06 |
| 判断者 | 実機確認：ユーザー、証跡監査：Codex |

読み取り専用探索、Render候補の一意選択、純粋ロジック、Job Object watchdogシミュレーション、Reconnect／Disconnect Basic Support、操作者確認付き実機操作を確認した。探索的試験でRenderのみの一時Unpluggedを誤成功とする欠陥を検出し、Render/Capture両候補の切断、全Endpoint観測、5秒安定窓、期限切れ成功拒否へ修正した。前提を確認できた修正版の接続・切断は各5回すべて15秒以内にPassし、操作者は最終接続と選択外Bluetooth機器への影響0件を確認した。明示切断後に接続待ちを再確認しないまま行ったReconnect 2件は`S_OK`後もUnpluggedを維持し、正しく`DeadlineExceeded`になったが、有効試行には含めない。Windows側で既に接続済みだった1件は`AlreadyInDesiredState`としてKS要求を送らず、その後の切断だけを有効試行へ加えた。

## Gate Aの目的

選択したContainerだけを対象に、Bluetooth無線全体や他の機器へ影響せず、非管理者プロセスからBluetoothオーディオ接続を安全かつ再現可能に接続・切断できる方式と機器単位の能力判定を確定する。MediaTek Bluetooth Adapter／Bluetooth Audio DeviceとAirPods Proは最初の参照構成であり、製品対象を特定ブランドへ限定しない。

KS要求のHRESULTが成功しても、実際の接続・切断成功とはみなさない。対象のMMDevice実状態が期限内に変化した場合だけ、その試行を成功とする。

## Go条件

次をすべて満たした場合だけGoとする。

1. Medium integrityの非管理者プロセスでReconnectとDisconnectの両方を実行できる。
2. 両プロパティのBasic Supportを確認できる。
3. 接続は有効5回中4回以上、各15秒以内に`DEVICE_STATE_ACTIVE`を確認できる。
4. 切断は有効5回中4回以上、各15秒以内に非Activeを確認できる。
5. KS要求成功を、実状態未変化のまま成功表示した件数が0である。
6. キーボード、コントローラー等、対象外Bluetooth機器への影響が0件である。
7. 子プロセスwatchdogが停止・異常終了を捕捉し、親プロセスを拘束しない。
8. 証跡に生のContainer ID、Endpoint ID、PnP ID、MACアドレスまたは個人情報が含まれない。

接続または切断の片方向だけが成功する場合はConditional Goにせず、その機器の直接操作をNo-Goとする。他のContainerの能力判定には波及させない。

## No-Go条件

次のいずれかに該当した場合はNo-Goとする。

- ReconnectまたはDisconnectのBasic Supportを確認できない。
- どちらかの成功率が有効5回中4回を下回る。
- 15秒以内の実状態確認を安定して行えない。
- KS要求結果と実状態を区別できない。
- 対象外Bluetooth機器へ影響する。
- 管理者権限、Bluetooth無線全体の切り替え、再ペアリングが通常操作に必要となる。
- watchdogで隔離しても安全に反復できない停止またはクラッシュが発生する。

## 現在の証跡

| 検証項目 | 結果 | 備考 |
|---|---|---|
| 読み取り専用探索 | Pass | AirPods Container、Render/Capture、connector 0候補をreport-scoped aliasで識別 |
| 自動安全試験 | Pass | Bluetooth KS 65件、solution smoke 1件 |
| 対象スコープ所有権 | Pass | global／選択Endpointは遮断し、無関係Endpoint faultと未帰属Adapterは非波及 |
| 複数機器カタログ契約 | Pass | 0／1／複数／同名、A2DP/HFP集約、選択保持、操作層非依存 |
| Reconnect Basic Support | Pass | Render候補で`S_OK`、GET対応 |
| Disconnect Basic Support | Pass | Render／Capture両候補で`S_OK`、GET対応 |
| 接続5回 | Pass | 前提確認済み5回中5回Pass、接続待ち未確認2回Blocked、既接続1回N/A |
| 切断5回 | Pass | 修正版5回中5回Pass |
| 実状態確認 | Pass | 有効な接続・切断各5回で独立観測が一致 |
| 誤成功表示 | Pass | 探索的欠陥1件を修正・回帰試験化し、修正版の誤成功0件 |
| 他機器影響 | Pass | 操作者が初回比較と最終状態で変化0を確認 |
| 非管理者での実操作 | Pass | 昇格なしでBasic Support／Reconnect／Disconnectを実行 |
| 子プロセスwatchdog | Pass（simulation） | kill-on-close Job Object、Job全体の空状態、孫PID消滅、異常終了、machine-wide排他を確認 |
| 異常系 | Follow-up | DeadlineExceeded／AlreadyInDesiredStateは実機確認済み。圏外／他端末／無線OFFのUI回復はPhase 4統合試験へ移管 |

したがってGate AはGoとする。製品実装はBasic Supportと所有権をContainerごとに判定し、対応できない機器だけWindows設定導線へ縮退する。圏外、他端末接続中、無線OFFはKS方式の成立性ではなく製品のエラー表示・回復動作としてPhase 4で検証する。

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
3. 接続・切断の有効試行を各5回記録する。
4. 異常系と他機器影響を確認する。
5. 証跡をサニタイズし、禁止識別子がないことを確認する。
6. Go／No-Go条件と照合し、この文書へ判断者、日付、根拠コミットを記入する。
7. No-Goの場合は`ms-settings:bluetooth`への縮退を後続実装の確定要件にする。
