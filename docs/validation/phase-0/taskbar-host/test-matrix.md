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

2026-08-05のRelease試験ではTaskbarHost 216件とsolution smoke 1件、計217件が合格した。Release buildは0 warning／0 error、formatとdiff checkも合格した。非interactive環境ではnative HWND試験を成功扱いで素通りさせず、明示的Skipとして報告する。

## 実機試験

| ケース | 期待結果 | 状態 |
|---|---|---|
| 読み取り専用UIA探索 | Startを一意に取得し、不完全時はUnknown | **Pass（現在環境）** |
| Start中央 | 安全な空きだけを使用 | **Partial Pass（Issue #13）** — Widgets ON／OFFと100／125／150／200%で交差0pxまたはNoFitを確認。100%検索ボックスはTask View／Widgets ONでもChildの目視・入力成功。Start一時UIからの復帰UXは未完了 |
| Start左寄せ | 安全な空きだけを使用 | **Partial Pass（100% Fail Closed）** — 検索非表示／Task View OFF／Widgets OFFの100%で`VerifiedNoFit / InsufficientWidth`。両styleともView／Controlを作らずexit 3、残留0。フォールバックは未確認 |
| Widgets ON | 候補レーンを再計算 | **Partial Pass（検索ボックス合格／Issue #13）** — 100%検索ボックスは`Place / Standard`、1088 samples、手動入力・表示、残留0に合格。アイコン＋ラベルは初期表示が良好だが一時UI復帰が未完了 |
| Widgets OFF | 候補レーンを再計算 | **Partial Pass** — 中央揃え100／125／150%は`Place / Standard`、100%左揃えと200%中央揃えは`VerifiedNoFit`。100%検索非表示／アイコンのみ／ボックスChild、検索非表示Popup、150% Childの目視・入力成功 |
| 検索：非表示／アイコンのみ／ボックス | 検索を覆わない | **Pass（100%中央揃え）** — 3形式の安定layoutは`Place / Standard`で標準要素との重なり0。検索ボックスはTask View／Widgets ON構成も1088 samples、手動表示・入力、残留0に合格 |
| 検索：アイコン＋ラベル | 初期配置と一時UIからの復帰 | **Partial Pass（Defect / Issue #13）** — 初期`Place / Standard`、描画・入力は良好。Start／検索の一時UIは`PrimaryTaskbarMissing`を再現し、安全にhideするが復帰UXは未完了 |
| タスクビュー ON／OFF | 位置変更へ追従 | **Partial Pass（Issue #13）** — OFFは100%検索非表示／アイコンのみ／ボックスを確認。ONは100%検索ボックスが固定HWND監視と手動確認に合格し、アイコン＋ラベルは初期表示成功。一時UI復帰は未完了 |
| ピン留めアプリ多数／空き不足 | コンパクトまたはVerifiedNoFit | Pending |
| UIA外部bounds変更 | Button／未知senderはhide-firstでfresh scanし、既知の非Button property senderだけを除外 | **Partial Pass** — 旧実装のChild／Popupで各6回の安全な復旧を確認。修正後は周期的なPane bounds通知を除外し、125%両方式で17秒間invalidation 0。異常・連続churnは自動試験のみ |
| Start／検索の一時UI | 不完全観測中はhideし、完全観測へ戻れば安全に同じhostを再表示 | **Partial Pass（Defect / Issue #13）** — 両UIで`PrimaryTaskbarMissing`を再現。隔離runは同じhostへStart 7.796秒、検索6.757秒で復帰。10秒超は既存timeoutでFail Closed。見かけ上のクラッシュでありrenderer／input flickerではない |
| DPI 150% | attach前後でDPI 144を維持し、描画と入力位置が一致 | **Pass（現在のChild構成）** — Childで描画、ドラッグ、ホイールを実機確認し、Child／Popupの自動attachと終了後残留0を確認。採用方式は別途未選定 |
| DPI 200% | 安全配置、または安全幅不足ならVerifiedNoFit | **Partial Pass** — Widgets ONと最小構成の双方で`VerifiedNoFit / InsufficientWidth`。最小構成も最大gap 324px < Compact最小380px。Place／フォールバックの目視・入力は未確認（Issue #11） |
| DPI 100／125% | 描画と入力位置が一致 | **Partial Pass（安定layout合格／Issue #13）** — 125%両styleと100%の検索非表示／アイコンのみ／ボックスに合格。100%左揃えはNoFitでFail Closed。100%アイコン＋ラベルの一時UI復帰は未完了 |
| `WS_POPUP`比較 | 残像・描画負け・入力不良を記録 | **Partial Pass（100／125%合格）** — 125%は1104 samplesと150 click、100%は1087 samplesでhidden 0。100%のinputは49／49 complete、`DragMoved` 593、`Wheel` 205。両倍率の手動確認も良好でIssue #12はClose済み。最終方式は未選定 |
| `WS_CHILD`比較 | 残像・描画負け・入力不良を記録 | **Partial Pass（安定layout合格／Issue #13）** — 125%と100%検索非表示／アイコンのみ／ボックスは監視・手動確認に合格。Task View／Widgets ON検索ボックスも1088 samplesでhidden 0。アイコン＋ラベルの一時UI復帰と最終方式は未完了 |
| Explorer再起動10回 | 各10秒以内、重複・残骸0 | **Pass（現在環境）** — 200% NoFitに加え、150%可視Childで10/10回再生成。外部監査の最大4.064秒、重複0、自然終了後残存0 |
| アプリ終了 | ホスト残骸0 | **Pass（現在環境）** — Child／Popup実行中はView／Control各1、重複なし。100%の安定layoutとTask View／Widgets ON検索ボックスChildを含む正常終了後はView／Control／プロセス0件 |
| Child／Popup最終方式 | 実測で採用方式を一意に決定 | Pending |

Explorer再起動はユーザー許可のもとでNoFit構成と可視Child構成を各10回実行した。表示倍率とタスクバー設定はユーザーがWindows設定から変更し、Spikeは変更していない。実タスクバーへの可視ホスト表示は、明示確認フラグ付きの時間制限された診断コマンドでだけ実行する。

Issue #12はClose済みで、100%の検索非表示／アイコンのみ／ボックスは合格した。Task View／Widgets ON検索ボックスも合格したが、Issue #13のStart／検索一時UI復帰、Issue #11のフローティングフォールバック、ピン留めアプリ多数のstress、およびChild／Popup最終方式は未完了であるため、Gate B全体はPendingのままとする。
