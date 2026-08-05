# Phase 0C／0D／0E 手動・自動試験マトリクス

## 自動試験

- 区間のclip、余白展開、merge、subtract、最大gap
- 負座標と境界値
- DPI 96／120／144／192でのDIP変換
- `Place`／`VerifiedNoFit`／`TransientUnknown`
- Start欠落、重複、UIA不完全時のfail closed
- 最終配置と障害物の交差面積0px、および既存矩形を継続利用できる厳密な安全条件
- 擬似親への親子付け、実親確認、PMv2確認、破棄
- `WS_POPUP`／`WS_CHILD`スタイル生成
- 専用MTA threadでのUIA登録・解除、sender-only cache、subscription epoch、fresh scan fence
- hide-first復旧、5秒Watchdog、fresh identity／attachment／既存矩形安全性の再証明
- 既知の非Button property senderだけの除外と、Button／未知sender／structure通知のfail-closed維持
- live hostのexact HWNDだけをnative child探索から除外し、別HWNDは障害物として維持
- stable Watchdogの表示継続、GDI double bufferの単一`BitBlt`、同一音量値のno-op
- Child／Popup各1000 frameのGDI／USER handle安定性、各200回の交互click jump、透明corner pixel
- nativeがfail closedとなった後の`FloatingFallback`／`HiddenFallback`選択
- 直前の完全検証済みprimary work-area／DPIの保持と、Settings／Display／DPI invalidation時の破棄
- unowned・non-topmost floating placement、native／floating同時表示禁止
- 1秒cooldown、500ms以上離れた同一candidate 2回、watcher-generation fenceによるnative promotion
- native作成失敗3回のsession latchと、surface遷移でresetしないduration
- complete discovery由来anchor、`DirectExpected` route、UIA前後native再証明、固定500ms deadline
- 単独`StartButtonMissing`だけのStart保持、fresh／previous obstacle union、現在bounds再検証、完全UIA成功時だけのanchor更新
- SetParent後のPopup style bit、hide／show後のstyle維持、外部Child変換のfail-closed

2026-08-05のPhase 0E Release試験ではTaskbarHost 183件とsolution smoke 1件、計184件が合格した。整理前368件から50.0%へ縮約し、Release buildは0 warning／0 error、formatとdiff checkも合格した。Phase 0Dの301件＋1件とPhase 0Cの216件＋1件はhistorical baselineとして保持する。非interactive環境ではnative HWND試験を成功扱いで素通りさせず、明示的Skipとして報告する。

## 実機試験

| ケース | 期待結果 | 状態 |
|---|---|---|
| 読み取り専用UIA探索 | Startを一意に取得し、不完全時はUnknown | **Pass（現在環境）** |
| Start中央 | 安全な空きだけを使用 | **Pass（試験済み構成）** — Widgets ON／OFFと100／125／150／200%で交差0pxまたはNoFitを確認。100% PopupはStart表示中も同じnative位置を保持し、表示中wheel入力に成功 |
| Start左寄せ | 安全な空きだけを使用 | **Pass（100／200% NoFit fallback）** — 検索非表示／Task View OFF／Widgets OFFで`VerifiedNoFit / InsufficientWidth`。100／200%ともfloatingの自動監査、ユーザー目視・入力、自然破棄、残留0に合格 |
| Widgets ON | 候補レーンを再計算 | **Pass（100%試験済み構成）** — 検索ボックスは`Place / Standard`、1088 samples、手動入力・表示、残留0に合格。アイコン＋ラベルもPopupでStart／Search native continuityとwheel入力に合格 |
| Widgets OFF | 候補レーンを再計算 | **Pass（試験済み経路）** — 中央揃え100／125／150%は`Place / Standard`、100／200%左揃えは`VerifiedNoFit`。nativeまたはfloatingで目視・入力・自然破棄・残留0に合格 |
| 検索：非表示／アイコンのみ／ボックス | 検索を覆わない | **Pass（100%中央揃え）** — 3形式の安定layoutは`Place / Standard`で標準要素との重なり0。検索ボックスはTask View／Widgets ON構成も1088 samples、手動表示・入力、残留0に合格 |
| 検索：アイコン＋ラベル | 初期配置と一時UIからの復帰 | **Pass（Phase 0E／100／150% Popup）** — 初期`Place / Standard`、Start／Search表示中もnative位置を保持し、表示中wheel入力と終了後残留0に合格 |
| タスクビュー ON／OFF | 位置変更へ追従 | **Pass（100%試験済み構成）** — OFFは検索非表示／アイコンのみ／ボックス、ONは検索ボックスとアイコン＋ラベルを確認。後者はPhase 0E Popupで一時UI native continuityにも合格 |
| ピン留めアプリ多数／空き不足 | コンパクトまたはVerifiedNoFit | **Pass（150%採用Popup）** — button 27→52、Start X 1919→1094でもfault 0、`Place / Standard`。自動監査と75秒手動runで重なり／ちらつきなし、wheel 18件、drag 18組、Floating／fatal／残留0 |
| UIA外部bounds変更 | Button／未知senderはhide-firstでfresh scanし、既知の非Button property senderだけを除外 | **Pass（Gate B範囲）** — 異常通知のfail-closed自動試験、Child／Popup各6回の安全復旧、125%の不要invalidation 0、多数ピン留めによるbutton 27→52の実レイアウト変更に合格。追加の順序race hardeningはIssue #15 |
| Start／検索の一時UI | native fail-closedを維持し、同一generationを直接再証明できる場合だけnativeを保持 | **Pass（Phase 0E／100／150% Popup）** — `DirectExpected`を含む継続証明に成功し、各120秒runでFloating遷移0。ユーザーは両UI中のnative保持を確認 |
| Start中の通知領域変動 | native attachmentと保守的な通知領域障害物を維持 | **Pass（150% Popup）** — 通知アイコンadd／delete各45回、`DirectExpected` 2回、Floating 0、wheel 67、drag 5組、watcher failure／fatal／残留0。ユーザー目視合格 |
| 一時UI表示中の操作 | Start／Searchを開いている間もstripが目視で継続し、入力を受ける | **Pass（wheel）** — 両UI中にwheelでsample volume変更成功。通常状態のclick／dragも同runで成功。click／dragはWindows transient UIを閉じる可能性があるため表示中の必須項目にはしない |
| 100% icon＋label個別再現 | StartとSearchを個別に開き、native保持と復帰を確認 | **Pass（Popup）** — ユーザー目視、Floating遷移0、正常終了後残留0 |
| DPI 150% | attach前後でDPI 144を維持し、描画と入力位置が一致 | **Pass（採用Popup continuity）** — 自動style／parent／owner／DWM／入力監査に合格。Start／Search目視runはnative継続3回、Floating遷移0、wheel 273件、drag 12組、fatal／残留0 |
| DPI 200% | 安全配置、または安全幅不足ならVerifiedNoFit | **Pass（NoFit＋floating）** — 左揃え・検索非表示・Task View／Widgets OFFで`VerifiedNoFit / InsufficientWidth`。floatingのstyle／ownership／DWM／入力自動監査、ユーザー目視、drag 11回、wheel 125件、自然破棄、残留0に合格（Issue #11） |
| DPI 100／125% | 描画と入力位置が一致 | **Pass（Place＋NoFit fallback）** — 125%両styleと100%の検索4形式に合格。100% Popupは一時UI native continuity、100%左揃えはNoFit floatingの目視、drag 9回、wheel 104件、自然破棄、残留0に合格 |
| `WS_POPUP`比較 | 残像・描画負け・入力不良を記録 | **Pass／採用** — 従来の100／125%安定性に加え、100% Start／Search中のnative保持とwheel入力、120秒runのFloating遷移0、正常破棄を確認 |
| `WS_CHILD`比較 | 残像・描画負け・入力不良を記録 | **比較完了／不採用** — 安定layoutでは合格したがStart／Search中のnative continuityを満たさず、3～10秒程度でFloatingへ退避 |
| Explorer再起動10回 | 各10秒以内、重複・残骸0 | **Pass（現在環境／採用Popup）** — 10/10回、最大5.395秒。全回で旧View消失、新Explorer世代、View／Control各1、Popup style／実親／DWMを確認し、exit 0、残留0。200% NoFitと150%可視Childの各10回も履歴Pass |
| Explorer世代ごとのGUI資源 | 長寿命product processで単調増加させない | **既知P2（Issue #18）** — Popup 10回でUSER 22→32、GDI 10→10。通常／message-only HWND数とクラス構成は不変。UIA event subscription経路に限定済みで、製品化前の隔離方式を別追跡 |
| アプリ終了 | ホスト残骸0 | **Pass（現在環境）** — Child／Popup実行中はView／Control各1、重複なし。100%の安定layoutとTask View／Widgets ON検索ボックスChildを含む正常終了後はView／Control／プロセス0件 |
| Child／Popup最終方式 | 実測で採用方式を一意に決定 | **PopupPreservedを選定** — Childは明示比較／rollback用に残す |

Explorer再起動はユーザー許可のもとでNoFit構成、可視Child構成、採用Popup構成を各10回実行した。表示倍率とタスクバー設定はユーザーがWindows設定から変更し、Spikeは変更していない。実タスクバーへの可視ホスト表示は、明示確認フラグ付きの時間制限された診断コマンドでだけ実行する。

Issue #11の100／200% NoFit floating、Issue #12のちらつき、Issue #13のStart／Search native continuity、100／150%採用PopupのStart／Search目視、多数ピン留めstress、Start中tray churn、採用PopupのExplorer再起動10回は実機確認まで合格した。PopupPreservedを採用方式に選定し、Gate BはGoとする。provenance／raceのP2強化はIssue #15、UIA watcherのExplorer世代別資源寿命はIssue #18でリリース前に追跡する。

実機host試験はmanifestが適用されるEXEまたは`dotnet run`で行う。DLL直接起動はmanifest非適用のため使用しない。
