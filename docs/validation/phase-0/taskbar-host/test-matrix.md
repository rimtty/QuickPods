# Phase 0C 手動・自動試験マトリクス

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
- hide-first復旧、5秒Watchdog、連続／sparse churnのfail-closed判定と限定acknowledge
- 既知の非Button property senderだけの除外と、Button／未知sender／structure通知のfail-closed維持
- live hostのexact HWNDだけをnative child探索から除外し、別HWNDは障害物として維持
- stable Watchdogの表示継続、GDI double bufferの単一`BitBlt`、同一音量値のno-op
- Child／Popup各1000 frameのGDI／USER handle安定性、各200回の交互click jump、透明corner pixel

2026-08-05のRelease試験ではTaskbarHost 209件とsolution smoke 1件、計210件が合格した。Release buildは0 warning／0 error、formatとdiff checkも合格した。非interactive環境ではnative HWND試験を成功扱いで素通りさせず、明示的Skipとして報告する。

## 実機試験

| ケース | 期待結果 | 状態 |
|---|---|---|
| 読み取り専用UIA探索 | Startを一意に取得し、不完全時はUnknown | **Pass（現在環境）** |
| Start中央 | 安全な空きだけを使用 | **Partial Pass** — Widgets ON／OFFと150%／200%で交差0pxまたはNoFitを確認。150% Childは目視・入力成功、残る表示構成は未確認 |
| Start左寄せ | 安全な空きだけを使用 | Pending |
| Widgets ON | 候補レーンを再計算 | **Partial Pass** — 検出・計算・attach成功、目視未確認 |
| Widgets OFF | 候補レーンを再計算 | **Partial Pass** — 150%は`Place / Standard`、200%は`VerifiedNoFit`。Childの目視・入力成功 |
| 検索：非表示／アイコン／ボックス | 検索を覆わない | **Partial Pass** — 非表示は150% Place／200% NoFit、ボックスは初回150% Place／attachを確認。アイコンとボックスの目視は未確認 |
| タスクビュー ON／OFF | 位置変更へ追従 | **Partial Pass** — OFFは150% Place／200% NoFit、ONは初期150% Placeを確認。組合せ網羅は未完了 |
| ピン留めアプリ多数／空き不足 | コンパクトまたはVerifiedNoFit | Pending |
| UIA外部bounds変更 | Button／未知senderはhide-firstでfresh scanし、既知の非Button property senderだけを除外 | **Partial Pass** — 旧実装のChild／Popupで各6回の安全な復旧を確認。修正後は周期的なPane bounds通知を除外し、125%両方式で17秒間invalidation 0。異常・連続churnは自動試験のみ |
| DPI 150% | attach前後でDPI 144を維持し、描画と入力位置が一致 | **Partial Pass** — Childで描画、ドラッグ、ホイールを実機確認。Popupの自動attachは成功したが目視回答待ち |
| DPI 200% | 安全配置、または安全幅不足ならVerifiedNoFit | **Partial Pass** — Widgets ONと最小構成の双方で`VerifiedNoFit / InsufficientWidth`。最小構成も最大gap 324px < Compact最小380px。Place／フォールバックの目視・入力は未確認（Issue #11） |
| DPI 100／125% | 描画と入力位置が一致 | **Partial Pass（125%合格）** — 125%はChild／Popupの17秒10ms samplingとPopup 150回clickでhidden 0。連続click-to-jump／ホイールの手動確認もちらつきなしでIssue #12の受入完了。100%は未完了 |
| `WS_POPUP`比較 | 残像・描画負け・入力不良を記録 | **Partial Pass（125%合格）** — 125%で1104 samples、最大View 1、hidden 0、Watchdog 3、recovery／invalidation／残留0。300 message／150 clickもhidden 0。ユーザーの連続click-to-jump／ホイール手動確認でもちらつきなし（Issue #12受入完了） |
| `WS_CHILD`比較 | 残像・描画負け・入力不良を記録 | **Partial Pass（修正後の自動再試験合格）** — 125%で1105 samples、最大View 1、hidden 0、Watchdog 3、recovery／invalidation／残留0。既存のドラッグ／ホイール目視成功に加え、連続clickの手動再確認待ち |
| Explorer再起動10回 | 各10秒以内、重複・残骸0 | **Pass（現在環境）** — 200% NoFitに加え、150%可視Childで10/10回再生成。外部監査の最大4.064秒、重複0、自然終了後残存0 |
| アプリ終了 | ホスト残骸0 | **Pass（現在環境）** — Child／Popup実行中はView／Control各1、重複なし。正常終了後の独立列挙はView／Control／プロセス0件 |
| Child／Popup最終方式 | 実測で採用方式を一意に決定 | Pending |

Explorer再起動はユーザー許可のもとでNoFit構成と可視Child構成を各10回実行した。表示倍率とタスクバー設定はユーザーがWindows設定から変更し、Spikeは変更していない。実タスクバーへの可視ホスト表示は、明示確認フラグ付きの時間制限された診断コマンドでだけ実行する。

Issue #12の受入条件は満たしたが、100%、200%フォールバック、残るレイアウト、およびChild／Popup最終方式の試験は未完了であるため、Gate B全体はPendingのままとする。
