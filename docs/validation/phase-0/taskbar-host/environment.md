# Phase 0C／0D／0E 検証環境

## 自動取得対象

検証時に、個人情報やウィンドウタイトルを保存せず次を記録する。

- Windows edition、version、OS build
- プロセスarchitectureとintegrity（管理者権限を使用しない）
- .NET SDK／Runtime
- プライマリタスクバーの寸法、DPI、向き
- Start／WidgetsのAutomation ID有無とサニタイズ済み矩形
- Explorer世代は実行中比較だけに使用し、生PIDは証跡へ保存しない
- 検証コミットSHA

## Phase 0D自動実機観測：175% floating fallback

| 項目 | 値 |
|---|---|
| 検証日 | 2026-08-05 |
| ブランチ | `codex/phase-0d-floating-fallback-spike`（Phase 0Cからstack） |
| Windows | Windows 11 Pro 10.0.26200（build 26200） |
| OS／プロセスarchitecture | x64／x64 |
| integrity | 非管理者（elevated=false） |
| .NET SDK | 10.0.302 |
| Windows Desktop Runtime | 10.0.10 |
| DPI | 168（175%） |
| 起動形態 | application manifestが適用されるEXE |
| 実行時間 | 45秒。surface遷移中もdurationはresetしない |
| 自動入力 | Start入力12秒／Search入力12秒 |
| 結果 | exit 0、終了後QuickPodsプロセス残留0 |
| 遷移証跡 | 少なくとも1回、`External / StructureChanged`から`NativeVisible → FloatingFallback → NativePromoted` |

この試験では、nativeが安全にhideした後、直前の完全検証済みprimary work-area／DPI上のunowned・non-topmost floatingへ退避し、1秒cooldown、500ms以上離れた同一candidate 2回、およびwatcher fenceを経てnativeへ復帰した。Settings／Display／DPI invalidation時だけ保持geometryを破棄する。このPhase 0Dログだけでは各遷移がStart／Searchのどちらによるものかを特定できず、floatingの目視上の継続と入力も証明しなかった。100% icon＋labelの個別手動試験は後続Phase 0EでPopup native continuityとして完了した。

実機host検証はEXEまたは`dotnet run`で行う。DLL直接起動ではapplication manifestが適用されないため、DPI／host証跡として使用しない。

## Phase 0E実機観測：100% Popup native continuity

| 項目 | 値 |
|---|---|
| 検証日 | 2026-08-05 |
| ブランチ | `codex/phase-0e-native-continuity-spike`（Phase 0Dからstack） |
| Windows | Windows 11 Pro 10.0.26200（build 26200） |
| OS／プロセスarchitecture | x64／x64 |
| integrity | 非管理者（elevated=false） |
| .NET SDK | 10.0.302 |
| Windows Desktop Runtime | 10.0.10 |
| 画面／DPI | 1920×1080、DPI 96（100%） |
| タスクバー | 1920×48 physical px、horizontal、中央揃え |
| タスクバー設定 | 検索アイコン＋ラベル、Task View／Widgets ON |
| native style | `PopupPreserved`（`SetParent`後も`WS_POPUP`維持） |
| 起動形態 | application manifestが適用されるEXE |
| 実行時間 | 120秒 |
| 一時UI | StartとSearchを個別に表示して保持 |
| 継続証明 | `DirectExpected`、retained notification area、retained Start、direct host attachment |
| surface遷移 | `Starting → NativeVisible`のみ。Floating遷移0 |
| 入力 | wheel 80件、drag完了3件。ユーザーは両一時UI表示中のwheel変更を確認 |
| 終了 | native正常破棄1件、終了後QuickPodsプロセス残留0 |

ユーザーはStart／Searchの双方で、ウィンドウが開いている間もPopup hostがタスクバー内の同じ位置に残り、wheelでsample volumeを変更できることを確認した。比較したChildは同じ要求を満たさず3～10秒程度でFloatingへ退避したため、PopupPreservedを採用方式に選定した。生HWND、Explorer PID、ウィンドウタイトル、通知内容は保存していない。

## Phase 0C historical：初回観測 150%（Widgets ON）

| 項目 | 値 |
|---|---|
| 検証日 | 2026-08-05 |
| ブランチ | `codex/phase-0c-taskbar-host-spike` |
| 検証対象 | Phase 0C historical evidence |
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

## 追加観測：100%中央揃え最小構成

同じタスクバー設定のまま100%へ変更した構成では、タスクバーは3840×48 physical px、DPI 96、Startは相対矩形`(1568, 0, 45, 48)`、UIA button数は24、native critical child観測数は5だった。読み取り専用探索は`discoveryComplete=true`、`faults=[]`で完了し、相対矩形`(190, 4, 300, 40)`を`Place / Standard`と判定した。

Popupは17秒間、起動時に確定した同一HWNDを監視して1087 samplesを取得し、hidden／destroyed／rect drift 0、最終View 1だった。ログはinteraction start／complete 49／49、`DragMoved` 593件、`Wheel` 205件、verified-visible Watchdog 23回で、invalidation／recovery／churnは0だった。Childも同じ固定HWND方式で17秒間監視して1089 samplesを取得し、hidden／destroyed／rect drift 0、最終View 1だった。ログはinteraction start／complete 50／50、`DragMoved` 506件、`Wheel` 515件、verified-visible Watchdog 11回で、invalidation／recoveryは0だった。

ユーザーは両styleのclick、drag、wheel、および表示を手動確認し、ちらつきや表示・入力上の問題がないことを確認した。両styleを各6秒で再実行した終了試験もexit 0、fatal 0で、終了後の独立列挙はView／Control HWNDとQuickPodsプロセスがすべて0件だった。これは現在の100%最小構成の合格を示すもので、未試験のlayoutやフォールバックまで合格とするものではない。Gate B全体はPendingである。

## 追加観測：100%左揃え最小構成

100%のまま検索を非表示、Task ViewとWidgetsをOFF、タスクバーを左揃えにした構成では、タスクバーは3840×48 physical px、DPI 96、Startは相対矩形`(0, 0, 45, 48)`、UIA button数は26、native critical child観測数は4だった。読み取り専用探索は`discoveryComplete=true`、`faults=[]`で完了したが、安全幅不足のため配置boundsを返さず、`VerifiedNoFit / InsufficientWidth`と判定した。

Child／Popupのhost試験はいずれもホストを作成せずexit 3で終了し、終了後の独立列挙はView／Control HWNDとQuickPodsプロセスがすべて0件だった。これは未知状態ではなく、完全な観測から安全幅不足を確定して推測配置しない期待どおりのFail Closed結果である。左揃えでネイティブ表示に成功した証跡ではなく、製品ではフォールバックが必要になる。Gate B全体はPendingである。

## 追加観測：100%中央揃え／検索アイコンのみ

100%で検索をアイコン表示、Task ViewとWidgetsをOFF、タスクバーを中央揃えにした構成では、タスクバーは3840×48 physical px、DPI 96、Startは相対矩形`(1502, 0, 45, 48)`、UIA button数は27、native critical child観測数は5だった。読み取り専用探索はfault 0で完了し、相対矩形`(173, 4, 300, 40)`を`Place / Standard`と判定した。

Childを17秒間、起動時に確定した同一HWNDで監視して1088 samplesを取得し、hidden／destroyed／rect drift 0だった。ログはinteraction start／complete 31／31、move 458件、wheel 285件、Watchdog 11回、invalidation／recovery 0だった。ユーザーはちらつき、標準要素との重なり、表示、入力のすべてに問題がないことを確認した。終了後の独立列挙はView／Control HWNDとQuickPodsプロセスがすべて0件だった。

## 追加観測：100%中央揃え／検索ボックス（Task View／Widgets OFF）

続いて検索をボックス表示にした同じ100%／中央揃え／Task View OFF／Widgets OFF構成では、Startは相対矩形`(1412, 0, 45, 48)`、UIA button数は27、native critical child観測数は5だった。読み取り専用探索はfault 0で完了し、相対矩形`(151, 4, 300, 40)`を`Place / Standard`と判定した。

Childを17秒間、起動時に確定した同一HWNDで監視して1088 samplesを取得し、hidden／destroyed／rect drift 0だった。ログはinteraction start／complete 10／10、move 295件、wheel 159件、Watchdog 11回、invalidation／recovery 0だった。ユーザーの手動確認でもちらつき、重なり、表示、入力にまったく問題はなかった。終了後の独立列挙はView／Control HWNDとQuickPodsプロセスがすべて0件だった。

## 追加観測：100%中央揃え／検索ボックス（Task View／Widgets ON）

Task ViewとWidgetsをONにした検索ボックス構成では、タスクバーは3840×48 physical px、DPI 96、Startは相対矩形`(1390, 0, 45, 48)`、Widgetsは`(6, 0, 152, 48)`、UIA button数は29、native critical child観測数は5だった。読み取り専用探索はfault 0で完了し、相対矩形`(264, 4, 300, 40)`を`Place / Standard`と判定した。

Childを17秒間、起動時に確定した同一HWNDで監視して1088 samplesを取得し、hidden／destroyed／rect drift 0だった。ログはinteraction start／complete 28／28、move 354件、wheel 70件、Watchdog 11回、invalidation／recovery 0だった。ユーザーの表示・入力確認も良好で、終了後のView／Control HWNDとQuickPodsプロセスはすべて0件だった。

## 追加観測：100%中央揃え／検索アイコン＋ラベル（Issue #13）

検索をアイコン＋ラベル表示、Task ViewとWidgetsをONにした構成では、タスクバーは3840×48 physical px、DPI 96、Startは相対矩形`(1449, 0, 45, 48)`、Widgetsは`(6, 0, 152, 48)`、UIA button数は29、native critical child観測数は5だった。初期の読み取り専用探索はcomplete、fault 0で、相対矩形`(1028, 4, 300, 40)`を`Place / Standard`と判定した。初期描画と入力はユーザー目視で良好だった。

Start／Windows一時UIと検索アイコン＋ラベルの検索一時UIを個別に開く制御runでは、どちらも一時的な`PrimaryTaskbarMissing`を再現した。再現区間の外部inspectは57/57回が`TransientUnknown / IncompleteObservation`であり、Runnerは不完全観測中に推測配置せずhostを安全にhideした。その後、Startでは7.796秒、検索では6.757秒で同じhostを再表示した。観測不能が10秒を超えるケースは既存のFail Closed timeoutへ到達して安全停止する。

Phase 0Cでは、10秒未満の制御runはhostプロセスが存続したまま安全に一時非表示となり、観測不能が10秒を超えるとSpikeは設計どおり安全停止した。この二つがユーザーには同じ「落ちた」ように見える復帰UXの問題だった。これはhistorical behaviorであり、Phase 0Dでは安全なfloatingまたはhidden fallbackへ遷移してセッションを継続する。Phase 0EではPopup native continuity、最終style選定、採用PopupのExplorer再起動10回、および100／200%左揃えNoFit floatingの目視・入力まで完了した。ピン留め多数、tray churn、および150%採用PopupのStart／Search目視が残るためGate BはPendingである。
