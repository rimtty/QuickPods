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
| 自動試験／品質Gate | **Pass（Phase 0E）** — TaskbarHost 183件＋Smoke 1件、計184件。整理前368件から50.0%へ縮約し、Release build 0 warning／0 error、format／diff check合格。Phase 0Dの301件＋1件とPhase 0Cの216件＋1件は履歴値 |
| 純粋な区間・DPI計算 | **Pass** — 境界、負座標、4 DPI、fail-closed、交差0pxを自動試験 |
| 読み取り専用UIA探索 | **Pass（現在環境）** — 一意なStart、構成どおりのWidgets検出／欠落、fault 0 |
| 擬似親HWND | **Pass（自動試験）** — attach、親／DPI検証、親消失、通知race、破棄 |
| UIA watcher／復旧 | **Pass（Phase 0E対象経路）** — 不完全・競合・unsafe時のhide-firstを維持。既存可視hostだけは、完全観測由来anchorとfresh native証明が同一で、`DirectExpected`かつUIA faultが単独`StartButtonMissing`の場合に限り、最後の完全Startとfresh／previous障害物のunionで同じ矩形を継続する。他のfault・identity／parent／bounds／DPI／DWM変化は従来どおりfallback |
| floating退避／native再昇格 | **Pass** — Settings／Display／DPI時だけ保持geometryを破棄し、1秒cooldown＋500ms以上離れた同一candidate 2回＋watcher fence後にhidden-prepared nativeへ復帰。100／200% NoFitでfloatingの目視・入力・自然破棄・残留0に合格。native作成失敗3回でsession latchし、native／floating同時表示は禁止 |
| Phase 0D実機自動試験 | **Pass（DPI 168 / 175%）** — 45秒EXEでStart 12秒／Search 12秒入力、exit 0、残留0。少なくとも1回`External / StructureChanged`から`NativeVisible → FloatingFallback → NativePromoted`を確認。ただしログではStart／Searchのどちらかは特定不能 |
| 実タスクバーへの`WS_POPUP`表示 | **Pass／採用方式（100／125／150%およびStart／Search）** — `SetParent`後も`WS_POPUP`を維持。100%と150%の各120秒Phase 0E runでFloating遷移0、継続証明と表示中wheel入力に成功。ユーザーはStart／Search双方でタスクバー内保持を確認 |
| 実タスクバーへの`WS_CHILD`表示 | **比較完了／不採用** — 安定layoutでは表示・入力に合格したが、100%のStart／Search表示中はWindows側の一時状態とQuickPodsの安全判定により3～10秒程度でFloatingへ退避した。`WS_POPUP`だけが要求されたnative continuityを満たしたため最終方式には採用しない |
| DPI 150% | **Pass（採用Popup continuity）** — DPI 144、中央揃え、検索アイコン＋ラベル、Task View／Widgets ON。Popupのstyle／実親／ownerなし／DWMと入力の自動監査に合格。120秒のStart／Search目視runはnative継続3回、Floating遷移0、wheel 273件、drag開始／完了12／12、fatal 0、自然終了後残留0 |
| DPI 200% | **Pass（NoFit＋floating）** — 左揃え最小構成で`VerifiedNoFit / InsufficientWidth`。floatingのstyle／ownership／DWM／入力自動監査、ユーザー目視、drag 11回、wheel 125件、自然破棄、残留0に合格 |
| DPI 100／125% | **Pass（Place＋NoFit fallback）** — 125%両styleと100%検索4形式に合格。100% PopupはStart／Search native continuity、100%左揃えはNoFit floatingの目視、drag 9回、wheel 104件、自然破棄、残留0に合格 |
| Start左揃え | **Pass（100／200% NoFit fallback）** — 検索非表示／Task View OFF／Widgets OFFで探索complete、fault 0、`VerifiedNoFit`。両DPIで推測したnativeを作らず、安全なfloatingの目視・入力・破棄・残留0に合格 |
| 検索：非表示／アイコンのみ／ボックス | **Pass（100%中央揃え）** — 3形式の安定layoutは`Place / Standard`。Task View／Widgets ON検索ボックスも1088 samplesでhidden／destroyed／rect drift 0、手動確認・残留0に合格 |
| 検索：アイコン＋ラベル | **Pass（Phase 0E／100／150% Popup）** — Start／検索の双方を開いたままタスクバー内の同じ位置を維持し、表示中のwheel入力も成功。各120秒runでFloating遷移0、正常終了後残留0 |
| 透過・hit testing | **Partial Pass** — 4 DPIの共通座標に加え、両style各1000 frameのhandle安定性、各200回click jump、透明corner pixel、double-buffer転送を自動試験。残る実画面構成は未確認 |
| Explorer復旧policy | **Pass（現在経路）** — 100ms attachment health check、5秒Watchdog、500ms fallback rescanを組み合わせ、fresh identity／DPI／bounds／attachment／障害物安全性が揃わない状態ではnativeを表示しない |
| UIA watcher資源寿命 | **既知P2** — 採用Popupの10回試験でUSER objectがExplorer世代ごとに1増加（22→32）。GDI 10、通常HWND 5、message-only HWND 1、クラス構成は不変。discovery-onlyは増加0、watcher-onlyで再現し、強制GCでも不変。Issue #18で製品化前の隔離方式を追跡 |
| Explorer再起動10回 | **Pass（現在環境／採用Popup）** — 10/10回が10秒以内、最大5.395秒。全回で旧View消失、新Explorer世代、View／Control各1、Popup style／実親／DWMを確認。exit 0、重複・孤立・終了後残存0。200% NoFitと150%可視Childの各10回も履歴Pass |
| Child／Popup最終方式 | **PopupPreservedを選定** — Start／Search中のnative continuityを満たした唯一の方式。`WS_CHILD`は明示比較／rollback用に残す |
| Gate B判断 | Pending |

Issue #11の100／200% NoFit floating、Issue #12のちらつき、Issue #13のStart／Search native continuity、100／150%採用PopupのStart／Search目視、および採用PopupのExplorer再起動10回は実機確認まで合格した。Popupを最終styleとして一意化し、非ブロッキングのprovenance／race hardeningはIssue #15、Explorer世代ごとのUIA watcher資源寿命はIssue #18で追跡する。Gate Bは、ピン留めアプリ多数のstressとtray churn中のStart保持が未完了のためPendingのままとする。

実機host試験はapplication manifestが適用されるEXEまたは`dotnet run`で実施する。DLL直接起動はmanifest非適用のため、DPI／hostの証跡に使用しない。`--duration`はnative／floating／hiddenの遷移でresetせず、セッション開始から単調に測る。
