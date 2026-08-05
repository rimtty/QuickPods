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
| 自動試験／品質Gate | **Pass** — TaskbarHost 216件＋Smoke 1件、計217件、Release build 0 warning／0 error、format／diff check合格 |
| 純粋な区間・DPI計算 | **Pass** — 境界、負座標、4 DPI、fail-closed、交差0pxを自動試験 |
| 読み取り専用UIA探索 | **Pass（現在環境）** — 一意なStart、構成どおりのWidgets検出／欠落、fault 0 |
| 擬似親HWND | **Pass（自動試験）** — attach、親／DPI検証、親消失、通知race、破棄 |
| UIA watcher／復旧 | **Partial Pass（安全性合格／Issue #13）** — 不完全・競合・unsafe時のhide-firstと10秒timeoutはFail Closed。Start／検索一時UIで`PrimaryTaskbarMissing`となり、同じhostへ7.796秒／6.757秒で復帰するが、見かけ上のクラッシュとなる復帰UXは未完了 |
| 実タスクバーへの`WS_POPUP`表示 | **Partial Pass（100／125%合格）** — 125%の17秒10ms samplingと150回rapid click、100%の17秒固定HWND監視でhidden 0。100%のclick／drag／wheelと表示もユーザー確認で良好。Issue #12はClose済みで、最終方式は未選定 |
| 実タスクバーへの`WS_CHILD`表示 | **Partial Pass（安定layout合格／Issue #13）** — 125%と100%検索非表示／アイコンのみ／ボックスの監視・手動確認に合格。Task View／Widgets ON検索ボックスも1088 samplesでhidden 0。アイコン＋ラベルの一時UI復帰と最終方式は未完了 |
| DPI 150% | **Pass（現在のChild構成）** — PMv2／DPI 144を維持し、Childの描画・ドラッグ・ホイール成功。両styleのattachと終了後残留0を確認し、採用方式は別途未選定 |
| DPI 200% | **Partial Pass** — Widgets ONと検索／タスクビュー／Widgets OFFの最小構成がともに`VerifiedNoFit`。最小構成も最大gap 324px < Compact最小380px。フォールバック判断はIssue #11、目視・入力は未確認 |
| DPI 100／125% | **Partial Pass（安定layout合格／Issue #13）** — 125%両styleと100%検索非表示両style、アイコンのみ／ボックスChildの監視・入力に合格。100%左揃えはNoFitでFail Closed。アイコン＋ラベルの一時UI復帰と200%フォールバックは未完了 |
| Start左揃え | **Partial Pass（100% Fail Closed）** — 検索非表示／Task View OFF／Widgets OFFで探索complete、fault 0、boundsなしの`VerifiedNoFit`。Child／Popupともexit 3、View／Control／プロセス残留0。ネイティブ表示成功ではなくフォールバックが必要な証跡 |
| 検索：非表示／アイコンのみ／ボックス | **Pass（100%中央揃え）** — 3形式の安定layoutは`Place / Standard`。Task View／Widgets ON検索ボックスも1088 samplesでhidden／destroyed／rect drift 0、手動確認・残留0に合格 |
| 検索：アイコン＋ラベル | **Partial Pass（Defect / Issue #13）** — 初期`Place / Standard`、描画・入力は良好。Start／検索一時UIで安全にhideするが、同じhostへの復帰に最長7.796秒かかり、10秒超ではtimeoutする |
| 透過・hit testing | **Partial Pass** — 4 DPIの共通座標に加え、両style各1000 frameのhandle安定性、各200回click jump、透明corner pixel、double-buffer転送を自動試験。残る実画面構成は未確認 |
| Explorer復旧policy | **Pass（自動試験）** — 250ms再試行、10秒fail-closed、identity／DPI／bounds世代比較、5秒Watchdog |
| UIA churn guard | **Pass（自動試験）** — 連続10秒または30秒内6回でfail closed。厳密な外部bounds／同一identity／既存矩形safe／`ShowVerifiedExisting`時だけsparse履歴をacknowledge |
| Explorer再起動10回 | **Pass（現在環境）** — 200% NoFitと150%可視Childで各10/10回。可視Childは最大4.064秒で再生成、重複0、自然終了後残存0 |
| Child／Popup最終方式 | Pending |
| Gate B判断 | Pending |

Issue #12はClose済みで、100%の検索非表示／アイコンのみ／ボックス、およびTask View／Widgets ON検索ボックスは合格した。Issue #13のStart／検索一時UI復帰、Issue #11のフローティングフォールバック、ピン留めアプリ多数のstress、およびChild／Popup方式選定が未完了のため、Gate BはPendingのままとする。
