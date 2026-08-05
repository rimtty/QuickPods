# Gate B — タスクバー内ホスト判断記録

## 現在の判断

| 項目 | 値 |
|---|---|
| Status | **Pending** |
| 対象 | GitHub Issue #6 |
| 対象ブランチ | `codex/phase-0c-taskbar-host-spike` |
| 判断 | 未判定 |
| 判断日 | 未定 |
| 判断者 | 未記入 |

安全領域計算、UI Automation探索、raw HWNDホスト、DPIおよび復旧機構の実装はSpikeで検証中である。残る実機・手動Gateを完了するまでは、ネイティブタスクバー表示を製品構成へ採用しない。

## Go条件

次をすべて満たした場合だけGoとする。

1. Windows標準要素との交差面積が全ケースで0pxである。
2. `TransientUnknown`では推測配置せず、ホストを非表示にする。
3. DPI 100／125／150／200%で描画位置と入力位置が一致する。
4. `WS_POPUP`維持または`WS_CHILD`の採用方式を実測で一意に決定できる。
5. Explorer再起動10回すべてで10秒以内に復旧し、重複・孤立HWNDが0件である。
6. WPF本体とは別プロセスに隔離でき、ExplorerのDPI Awarenessを本体へ波及させない。
7. ExplorerへDLL注入、フック、サブクラス化、メモリ改変を行わない。

## No-Go条件と縮退

いずれかのGo条件を再現可能に満たせない場合はNo-Goとし、初期リリースではフローティングストリップを標準表示、通知領域を最終退避先とする。ネイティブホストは製品コードへ含めない。

## 現在の証跡

| 検証項目 | 結果 |
|---|---|
| 純粋な区間・DPI計算 | **Pass** — 境界、負座標、4 DPI、fail-closed、交差0pxを自動試験 |
| 読み取り専用UIA探索 | **Pass（現在環境）** — 一意なStart、Widgets、fault 0 |
| 擬似親HWND | **Pass（自動試験）** — attach、親／DPI検証、親消失、通知race、破棄 |
| UIA watcher／復旧 | **Partial Pass** — 専用MTA、同一thread登録・解除、sender-only cache、subscription epoch、fresh scan fence、hide-firstを自動試験。現在環境の外部bounds変更とNoFit構成のExplorer再検出でも確認。可視ホスト再接続は未確認 |
| 実タスクバーへの`WS_POPUP`表示 | **Partial Pass** — 30秒、外部bounds変更6回、安全な既存矩形を維持、exit 0。目視・入力未確認 |
| 実タスクバーへの`WS_CHILD`表示 | **Partial Pass** — 30秒、外部bounds変更6回、安全な既存矩形を維持、exit 0。目視・入力未確認 |
| DPI 150% | **Partial Pass** — attach前後でPMv2／DPI 144を維持。目視・入力未確認 |
| DPI 200% | **Partial Pass** — Widgets ONでは安全幅不足を`VerifiedNoFit`として非表示。Place構成と目視・入力は未確認 |
| DPI 100／125% | Pending |
| 透過・hit testing | **Partial Pass** — 機構と4 DPIの共通座標は自動試験。実画面品質・実入力は未確認 |
| Explorer復旧policy | **Pass（自動試験）** — 250ms再試行、10秒fail-closed、identity／DPI／bounds世代比較、5秒Watchdog |
| UIA churn guard | **Pass（自動試験）** — 連続10秒または30秒内6回でfail closed。厳密な外部bounds／同一identity／既存矩形safe／`ShowVerifiedExisting`時だけsparse履歴をacknowledge |
| Explorer再起動10回 | **Partial Pass** — 200%／NoFit構成で10/10回、完全観測へ最大3.897秒、世代更新、終了後残存0。可視ホスト再接続は未確認 |
| Child／Popup最終方式 | Pending |
| Gate B判断 | Pending |

未実行の目視・実入力、残るDPI／表示構成、可視ホスト実行中のExplorer再起動、Child／Popup方式選定を成功として扱わず、Gate BはPendingのままとする。
