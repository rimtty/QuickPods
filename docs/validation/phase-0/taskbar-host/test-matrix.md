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

2026-08-05のRelease試験ではTaskbarHost 188件とsolution smoke 1件、計189件が合格した。Release buildは0 warning／0 errorだった。非interactive環境ではnative HWND試験を成功扱いで素通りさせず、明示的Skipとして報告する。

## 実機試験

| ケース | 期待結果 | 状態 |
|---|---|---|
| 読み取り専用UIA探索 | Startを一意に取得し、不完全時はUnknown | **Pass（現在環境）** |
| Start中央 | 安全な空きだけを使用 | **Partial Pass** — 未知native障害物を除外して計算・attach成功、目視未確認 |
| Start左寄せ | 安全な空きだけを使用 | Pending |
| Widgets ON | 候補レーンを再計算 | **Partial Pass** — 検出・計算・attach成功、目視未確認 |
| Widgets OFF | 候補レーンを再計算 | Pending |
| 検索：非表示／アイコン／ボックス | 検索を覆わない | Pending |
| タスクビュー ON／OFF | 位置変更へ追従 | Pending |
| ピン留めアプリ多数／空き不足 | コンパクトまたはVerifiedNoFit | Pending |
| UIA外部bounds変更 | hide-firstでfresh scanし、安全な既存矩形だけを再表示 | **Partial Pass** — Child／Popupで各6回検出し、全件で既存矩形safe、`recreated=False`。異常・連続churnは自動試験のみ |
| DPI 150% | attach前後でDPI 144を維持し、描画と入力位置が一致 | **Partial Pass** — attach前後のPMv2／DPIのみ。目視・入力未確認 |
| DPI 200% | 安全配置、または安全幅不足ならVerifiedNoFit | **Partial Pass** — Widgets ONで`VerifiedNoFit / InsufficientWidth`を再現。Place構成と描画・入力は未確認 |
| DPI 100／125% | 描画と入力位置が一致 | Pending |
| `WS_POPUP`比較 | 残像・描画負け・入力不良を記録 | **Partial Pass** — 30秒attach、6回の安全な復旧、exit 0。目視・入力未確認 |
| `WS_CHILD`比較 | 残像・描画負け・入力不良を記録 | **Partial Pass** — 30秒attach、6回の安全な復旧、exit 0。目視・入力未確認 |
| Explorer再起動10回 | 各10秒以内、重複・残骸0 | **Partial Pass** — 200%／NoFit構成で10/10回、完全観測へ最大3.897秒、世代更新、終了後残存0。可視ホスト再接続は未確認 |
| アプリ終了 | ホスト残骸0 | **Partial Pass** — 30秒実行の正常終了時にhide-firstで破棄。追加の独立列挙はView／Control／プロセス0件。実行中の個数確認は未完了 |
| Child／Popup最終方式 | 実測で採用方式を一意に決定 | Pending |

Explorer再起動はユーザー許可のもとでNoFit構成の再検出試験だけを実行した。表示設定変更と可視ホスト実行中の再起動は未実行である。実タスクバーへの可視ホスト表示は、明示確認フラグ付きの時間制限された診断コマンドでだけ実行する。
