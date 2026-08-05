# Gate B — タスクバー内ホスト判断記録

## 現在の判断

| 項目 | 値 |
|---|---|
| Status | **Pending** |
| 対象 | GitHub Issue #6 |
| 対象ブランチ | `codex/phase-0e-native-continuity-spike`（Phase 0Dからstack） |
| 判断 | 未判定 |
| 判断日 | 未定 |
| 判断者 | 未記入 |

安全領域計算、UI Automation探索、raw HWNDホスト、DPIおよび復旧機構の実装はSpikeで検証中である。残る実機・手動Gateを完了するまでは、ネイティブタスクバー表示を製品構成へ採用しない。

## Go条件

次をすべて満たした場合だけGoとする。

1. Windows標準要素との交差面積が全ケースで0pxである。
2. `TransientUnknown`では推測したタスクバー配置を使わず、ネイティブホストを非表示にする。直前の完全観測で検証済みのprimary work-area／DPIが有効な場合に限り、その範囲内のfloating fallbackで表示を継続できる。
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
| 自動試験／品質Gate | **Pass（Phase 0E）** — TaskbarHost 364件＋Smoke 1件、計365件、Release build 0 warning／0 error、format／diff check合格。Phase 0Dの301件＋1件とPhase 0Cの216件＋1件は履歴値 |
| 純粋な区間・DPI計算 | **Pass** — 境界、負座標、4 DPI、fail-closed、交差0pxを自動試験 |
| 読み取り専用UIA探索 | **Pass（現在環境）** — 一意なStart、構成どおりのWidgets検出／欠落、fault 0 |
| 擬似親HWND | **Pass（自動試験）** — attach、親／DPI検証、親消失、通知race、破棄 |
| UIA watcher／復旧 | **Pass（Phase 0E対象経路）** — 不完全・競合・unsafe時のhide-firstを維持。既存可視hostだけは、完全観測由来anchorとfresh native証明が同一で、`DirectExpected`かつUIA faultが単独`StartButtonMissing`の場合に限り、最後の完全Startとfresh／previous障害物のunionで同じ矩形を継続する。他のfault・identity／parent／bounds／DPI／DWM変化は従来どおりfallback |
| floating退避／native再昇格 | **Partial Pass（自動合格／手動待ち）** — Settings／Display／DPI時だけ保持geometryを破棄し、1秒cooldown＋500ms以上離れた同一candidate 2回＋watcher fence後にhidden-prepared nativeへ復帰。native作成失敗3回でsession latch。native／floating同時表示は禁止 |
| Phase 0D実機自動試験 | **Pass（DPI 168 / 175%）** — 45秒EXEでStart 12秒／Search 12秒入力、exit 0、残留0。少なくとも1回`External / StructureChanged`から`NativeVisible → FloatingFallback → NativePromoted`を確認。ただしログではStart／Searchのどちらかは特定不能 |
| 実タスクバーへの`WS_POPUP`表示 | **Pass／採用方式（100／125%およびStart／Search）** — `SetParent`後も`WS_POPUP`を維持。100%の120秒Phase 0E runはFloating遷移0、`DirectExpected`継続成功、wheel 80件、drag完了3件、正常破棄1件。ユーザーはStart／Search双方の表示中もタスクバー内保持とwheel入力成功を確認 |
| 実タスクバーへの`WS_CHILD`表示 | **比較完了／不採用** — 安定layoutでは表示・入力に合格したが、100%のStart／Search表示中はWindows側の一時状態とQuickPodsの安全判定により3～10秒程度でFloatingへ退避した。`WS_POPUP`だけが要求されたnative continuityを満たしたため最終方式には採用しない |
| DPI 150% | **Pass（基礎attach／Child入力）** — PMv2／DPI 144を維持し、Childの描画・ドラッグ・ホイール成功。両styleのattachと終了後残留0を確認。採用PopupのStart／Search continuityは残るDPI matrix項目 |
| DPI 200% | **Partial Pass** — Widgets ONと検索／タスクビュー／Widgets OFFの最小構成がともに`VerifiedNoFit`。最小構成も最大gap 324px < Compact最小380px。フォールバック判断はIssue #11、目視・入力は未確認 |
| DPI 100／125% | **Pass（試験済みPlace構成）** — 125%両styleと100%検索非表示両style、アイコンのみ／ボックスChildの監視・入力に合格。100% PopupはStart／Search表示中のnative continuityとwheel入力にも合格。100%左揃えはNoFitでFail Closed |
| Start左揃え | **Partial Pass（100% Fail Closed）** — 検索非表示／Task View OFF／Widgets OFFで探索complete、fault 0、boundsなしの`VerifiedNoFit`。Child／Popupともexit 3、View／Control／プロセス残留0。ネイティブ表示成功ではなくフォールバックが必要な証跡 |
| 検索：非表示／アイコンのみ／ボックス | **Pass（100%中央揃え）** — 3形式の安定layoutは`Place / Standard`。Task View／Widgets ON検索ボックスも1088 samplesでhidden／destroyed／rect drift 0、手動確認・残留0に合格 |
| 検索：アイコン＋ラベル | **Pass（Phase 0E／100% Popup）** — Start／検索の双方を開いたままタスクバー内の同じ位置を維持し、表示中のwheel入力も成功。120秒runでFloating遷移0、正常終了後残留0 |
| 透過・hit testing | **Partial Pass** — 4 DPIの共通座標に加え、両style各1000 frameのhandle安定性、各200回click jump、透明corner pixel、double-buffer転送を自動試験。残る実画面構成は未確認 |
| Explorer復旧policy | **Pass（自動試験）** — 250ms再試行、10秒fail-closed、identity／DPI／bounds世代比較、5秒Watchdog |
| UIA churn guard | **Pass（自動試験）** — 連続10秒または30秒内6回でfail closed。厳密な外部bounds／同一identity／既存矩形safe／`ShowVerifiedExisting`時だけsparse履歴をacknowledge |
| Explorer再起動10回 | **Pass（現在環境）** — 200% NoFitと150%可視Childで各10/10回。可視Childは最大4.064秒で再生成、重複0、自然終了後残存0 |
| Child／Popup最終方式 | **PopupPreservedを選定** — Start／Search中のnative continuityを満たした唯一の方式。`WS_CHILD`は明示比較／rollback用に残す |
| Gate B判断 | Pending |

Issue #12のちらつきとIssue #13のStart／Search native continuityは、100% PopupPreservedの実機確認まで合格し、採用styleをPopupに一意化した。非ブロッキングのprovenance／race hardeningはIssue #15で追跡する。Gate Bは、採用PopupでのExplorer再起動10回、ピン留めアプリ多数のstress、tray churn中のStart保持、および残るDPI／fallback目視項目が未完了のためPendingのままとする。

実機host試験はapplication manifestが適用されるEXEまたは`dotnet run`で実施する。DLL直接起動はmanifest非適用のため、DPI／hostの証跡に使用しない。`--duration`はnative／floating／hiddenの遷移でresetせず、セッション開始から単調に測る。
