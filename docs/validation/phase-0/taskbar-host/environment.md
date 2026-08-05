# Phase 0C 検証環境

## 自動取得対象

検証時に、個人情報やウィンドウタイトルを保存せず次を記録する。

- Windows edition、version、OS build
- プロセスarchitectureとintegrity（管理者権限を使用しない）
- .NET SDK／Runtime
- プライマリタスクバーの寸法、DPI、向き
- Start／WidgetsのAutomation ID有無とサニタイズ済み矩形
- Explorer世代は実行中比較だけに使用し、生PIDは証跡へ保存しない
- 検証コミットSHA

## 初回観測：150%（Widgets ON）

| 項目 | 値 |
|---|---|
| 検証日 | 2026-08-05 |
| ブランチ | `codex/phase-0c-taskbar-host-spike` |
| 検証対象 | 本ブランチのPhase 0C作業ツリー（コミット前検証） |
| Windows | Windows 11 Pro 10.0.26200（build 26200） |
| OS／プロセスarchitecture | x64／x64 |
| integrity | 非管理者（elevated=false） |
| .NET SDK | 10.0.302 |
| Windows Desktop Runtime | 10.0.10 |
| プライマリタスクバー | 3840×72 physical px、horizontal、DPI 144（150%） |
| Start | 一意に取得、相対矩形 `(1091, 0, 68, 72)` |
| Widgets | 一意に取得、相対矩形 `(9, 0, 228, 72)` |
| UIA button数 | 30 |
| native critical child観測数 | 5（既知構造要素を含む。未知の可視要素は障害物化） |
| Compact配置 | 相対矩形 `(249, 6, 299, 60)` |
| 初回探索時間 | Child 102.439 ms／Popup 98.565 ms（各30秒live runの初回scan） |

矩形はタスクバー左上を原点とする相対physical pxだけを記録した。生HWND、Explorer PID、ウィンドウタイトル、通知内容、一般ボタンのAutomation ID、外部要素のclass名は保存していない。UIA無効化の記録もevent kindと`Owned`／`External`／`Unknown`分類だけに限定した。別の可視タスクバー要素を検出したため、これを未知障害物として安全領域から除外した。実画面スクリーンショットは保存していない。

## 追加観測：200%構成

同日の後続セッションでは、プライマリ表示領域が3600×2260 physical px、タスクバーが3600×96 physical px、DPI 192（200%）だった。Startは相対矩形`(696, 0, 90, 96)`、Widgetsは`(12, 0, 304, 96)`、UIA button数は30、native critical child観測数は5だった。

このWidgets ON構成では、Startより左の検証済み候補レーンがCompact最小幅に届かず、`VerifiedNoFit / InsufficientWidth`となった。さらにユーザーが検索、タスクビュー、WidgetsをOFF、配置を中央揃えにした最小構成でもNoFitだった。最小構成の候補レーンは887pxだが、名前を保存しない外部native障害物のmargin込み区間240pxが中央を分断し、残る最大gapは324pxだった。DPI 192でのCompact最小幅380pxを下回るため、これは推測配置せず非表示を選ぶ期待どおりのFail Closed結果であり、200%での描画・入力成功を示すものではない。

## 追加観測：150%最小構成と可視復旧

ユーザーがWindows設定から検索を非表示、タスクビューとWidgetsをOFF、配置を中央揃えにしたまま150%へ変更した構成では、タスクバーは3840×72 physical px、DPI 144、Startは相対矩形`(1325, 0, 68, 72)`、Widgetsは欠落、UIA button数は27、native critical child観測数は5だった。探索はfault 0で完了し、相対矩形`(820, 6, 450, 60)`を`Place / Standard`と判定した。単発の初回探索は88.653ms、Explorer再起動試験後の再確認は93.791msだった。

この構成でChild／Popupを各30秒表示し、さらにChildを表示したままExplorerを10回再起動した。実行中の生PIDとHWNDは一意性・世代比較にだけ使用し、本文書には保存していない。再起動後もタスクバー、DPI、Start、button数、critical child数、最終配置判定は同じだった。

## 追加観測：125%最小構成

同じタスクバー設定のまま125%へ変更した構成では、タスクバーは3840×60 physical px、DPI 120、Startは相対矩形`(1424, 0, 56, 60)`、Widgetsは欠落、UIA button数は27、native critical child観測数は5だった。fault 0、探索89.121msで、相対矩形`(116, 5, 375, 50)`を`Place / Standard`と判定した。

この構成で確認されたちらつきは、5秒UIA scan時の既知`ControlType.Pane` bounds通知と、可視ホスト自身をnative探索が`UnknownObstacle`として再列挙するフィードバックによる不要なhide／recoveryが主因だった。修正後は既知の非Button property senderだけを除外し、native探索では実行中hostと完全一致するHWNDだけを除外した。Button、sender種別不明、structure通知、および別HWNDは安全側の無効化／障害物として維持している。副次的なdirect-GDI tearリスクには、memory DCへの全体描画と1回の`BitBlt`によるdouble buffer、および同一clamp済み音量値のno-opを適用した。

修正後の同一125%構成で、Popupを17秒間10ms間隔で監査して1104 samples、Childで1105 samplesを取得した。両方式ともView最大1、hidden interval 0、visible Watchdog 3回、recovery 0、invalidation 0、終了後残留0だった。さらにPopupへ300件のdirect messageを送り、150回のclick完了、hidden 0、visible Watchdog 3回、recovery 0、終了後残留0を確認した。その後、ユーザーが125% Popupの連続click-to-jumpとホイールを手動確認し、ちらつきが発生しないことを確認したため、Issue #12の受入条件を満たした。Gate B全体は残る実機試験があるためPendingである。
