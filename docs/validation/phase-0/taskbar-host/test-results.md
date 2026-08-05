# Phase 0C 検証結果

## 結果概要

| 項目 | 結果 |
|---|---|
| ブランチ | `codex/phase-0c-taskbar-host-spike` |
| 検証対象 | 本ブランチのPhase 0C作業ツリー（コミット前検証） |
| Release build | **Pass** — 0 warning / 0 error |
| 自動試験 | **Pass** — TaskbarHost 216件、Smoke 1件、計217件 |
| format | **Pass** — `--verify-no-changes --severity info` |
| diff check | **Pass** — Phase 0C作業ツリー全体に空白エラーなし |
| 読み取り専用UIA探索 | **Pass（現在環境）** — Widgets ONではStart／Widgetsを一意に取得し、150%でbutton 30件、100%の検索ボックス／アイコン＋ラベルでbutton 29件・critical child 5件。Widgets OFFでは100%検索非表示でbutton 24件、アイコンのみ／ボックスで各27件、左揃えで26件 |
| UIA監視・復旧 | **Partial Pass（安全性合格／Issue #13）** — hide-first、fresh scan、NoFit再検出、Explorer再生成は合格。Start／Windowsと検索アイコン＋ラベルの一時UIは`PrimaryTaskbarMissing`を再現し、同じhostへ7.796秒／6.757秒で復帰するが、10秒超では既存timeoutにより安全停止する。復帰UXは未完了 |
| 擬似親HWND | **Pass（自動試験）** — attach、実親／PMv2検証、hide/show、親消失、通知race、破棄 |
| 透過・入力 | **Pass（機構の自動試験）** — color-key、透明corner pixel、共通slider座標、hit target、GDI解放 |
| `WS_CHILD`可視試験 | **Partial Pass（安定layout合格／Issue #13）** — 125%と100%検索非表示、アイコンのみ、ボックスの固定HWND監視に合格。Task View／Widgets ONの検索ボックスも1088 samplesでhidden／destroyed／rect drift 0。アイコン＋ラベルは初期描画・入力が良好だが一時UIからの復帰が未完了 |
| `WS_POPUP`可視試験 | **Partial Pass（100／125%合格）** — 125%の17秒可視性samplingと150回のrapid click、100%の17秒固定HWND監視でhidden／destroyed／rect drift 0。両倍率のユーザー手動確認でもちらつき・入力・表示に問題なし。Issue #12はClose済みで、最終方式は未選定 |
| 100% | **Partial Pass（安定layout合格／Issue #13）** — 検索非表示、アイコンのみ、ボックスは表示・入力・残留確認に合格し、左揃えNoFitもFail Closed。アイコン＋ラベルは初期配置・入力に成功するが、Start／検索の一時UIからの復帰が未完了 |
| 検索：非表示／アイコンのみ／ボックス | **Pass（100%中央揃え）** — 3形式の安定layoutは標準要素との重なりなし。アイコンのみ／ボックスのTask View／Widgets OFF Childと、ボックスのTask View／Widgets ON Childは各1088 samplesでhidden／destroyed／rect drift 0 |
| 検索：アイコン＋ラベル | **Partial Pass（Defect / Issue #13）** — 初期`Place / Standard`と描画・入力は良好。Start／検索の一時UI中は安全側に非表示となり、同じhostへの復帰に最長7.796秒。10秒超は既存timeoutでFail Closed |
| 125% | **Pass（現在構成）** — `Place / Standard`。Child／Popupの17秒samplingとPopup 150回clickでhidden 0、重複・残留0。ユーザーの連続click-to-jump／ホイールでもちらつきなし。Issue #12はClose済み |
| 200% | **Pass（Fail Closed）** — Widgets ONと検索／タスクビュー／Widgets OFFの最小構成がともに`VerifiedNoFit / InsufficientWidth`。可視ホストは作成しない |
| Explorer再起動 | **Pass（現在環境）** — 200% NoFitと150%可視Childで各10回。可視Childは全回再生成、外部監査で最大4.064秒、重複・timeout・churn fail closed 0 |
| 終了後残存 | **Pass（追加観測）** — 実行中View／Control各1、正常終了後は両HWNDとQuickPodsプロセス0 |

## 実行コマンドと観測

```powershell
dotnet restore QuickPods.sln --locked-mode
dotnet format QuickPods.sln --no-restore --verify-no-changes --severity info
dotnet build QuickPods.sln -c Release --no-restore -p:ContinuousIntegrationBuild=true
dotnet test QuickPods.sln -c Release --no-build --no-restore --logger "trx" --collect:"XPlat Code Coverage"
dotnet run --project spikes/QuickPods.Spike.TaskbarHost -c Release --no-build -- inspect
dotnet run --project spikes/QuickPods.Spike.TaskbarHost -c Release --no-build -- host --style child --duration 30 --confirm-live-host
dotnet run --project spikes/QuickPods.Spike.TaskbarHost -c Release --no-build -- host --style popup --duration 30 --confirm-live-host
```

- 読み取り専用探索はStartとWidgetsを一意に取得し、未知の可視native要素も障害物化して交差0pxを再検証したうえで、相対矩形`(249, 6, 299, 60)`を`Place / Compact`と判定した。初回scanはChildで102.439 ms、Popupで98.565 msだった。
- ChildとPopupの両方式で検証済み矩形へattachし、attach前後ともPer-Monitor V2／DPI 144を維持した。各30秒の実行はexit 0で終了した。
- 修正前の各方式で`External / BoundingRectangleChanged`を6回受信した。各イベント後にfresh scanを行い、同一タスクバー上の現在矩形が引き続き安全であることを確認したため、6回とも`recreated=False`で同じホストを再表示した。ただしhide／recovery自体は発生しており、後続のちらつき調査対象になった。
- UIA watcherは専用MTA thread上で登録・解除を同一threadに限定し、callbackではsender-only cacheの識別メタデータだけを読む。taskbar root配下のproperty changeと、一意なStartの直近ControlView親配下のstructure changeを監視し、subscription epochで旧rootからの遅延callbackを棄却する。既知の非Button senderによるproperty通知だけを除外し、Button、sender種別不明、およびstructure通知は安全側の無効化として維持する。
- 無効化を受けるとホストを先に隠し、watcher世代で囲ったfresh scanを終えるまで再表示しない。UIA通知を補う5秒Watchdogは、同一identity、native attachment有効、現在矩形safeをfresh scanで確認できた場合は表示を維持し、不完全・競合・unsafe時だけhide-first復旧へ移る。
- churn guardは連続10秒または30秒内6回でfail closedとする。sparse上限だけは、`External / BoundingRectangleChanged`、同一identity、fresh scanで既存矩形が安全、かつ`ShowVerifiedExisting`となる条件がすべて揃った場合に限り、そのsparse履歴をacknowledgeする。
- 200%ではWidgets ONに加え、検索／タスクビュー／Widgets OFFの最小構成でも安全幅不足を`VerifiedNoFit / InsufficientWidth`として検出し、Child／Popupともホスト作成前に終了した。最小構成の候補レーン887pxは外部native障害物で分断され、最大gap 324pxがCompact最小380pxを下回った。フォールバック判断はIssue #11で追跡する。
- 同じ200%／NoFit構成でExplorerを10回再起動した。Shell_TrayWndは約0.24～0.36秒で新しい世代へ切り替わり、UIAを含む完全観測は全回10秒以内（最大3.897秒）に復帰した。全回で同じNoFit判定を再現し、終了後の独立列挙はView／Control HWNDとQuickPodsプロセスがすべて0件だった。
- Shell_TrayWnd復帰直後の単発観測は一度`TransientUnknown`となったため、Gateの判定は製品policyと同じ250ms再試行・最大10秒で行った。これは不完全観測を成功扱いに変えたものではない。
- 検索、タスクビュー、WidgetsをOFF、中央揃えにした150%構成では、DPI 144、Start相対X 1325、UIA button 27件、native critical child 5件を取得し、相対矩形`(820, 6, 450, 60)`を`Place / Standard`と判定した。
- この150%構成でChild／Popupを各30秒実行した。独立列挙は両方式とも実行中View／Control各1、最大各1、重複0、exit 0、終了後0だった。ユーザーはChildのドラッグとホイールが正常と確認し、提供された静止画にも重なりや描画崩れは見られなかった。Popupの目視・入力所見は未回答として残す。
- 同じ150%構成で可視Childを維持したままExplorerを10回再起動した。全回でView消失、新しいExplorer世代、可視Childの再生成、非表示top-level Control 1件を独立監査し、重複は0だった。停止開始から300ms連続安定まで3.492～4.064秒、Runner内部のhide-first復旧は1.402～1.604秒で、10回とも`recreated=True`、`identity-changed=True`、`forced=True`だった。
- 再起動中に旧配置が安全でない3回は、新しいfresh scanの安全な矩形へ再生成した。残る回も新しいExplorer世代のため必ず再生成し、全10回で`current-bounds-safe`または新規配置の交差0px検証を通過した。120秒の自然終了はexit 0で、timeout 0、UIA churn fail closed 0、終了後のView／Control／QuickPodsプロセスは0、Explorerは1プロセスだった。
- 125%最小構成ではDPI 120、Start相対矩形`(1424, 0, 56, 60)`を取得し、相対矩形`(116, 5, 375, 50)`を`Place / Standard`と判定した。Childの初回30秒runは終了時刻と安全再検証が重なりexit 4でFail Closedしたが、View／Control最大各1、timeout 0、churn failure 0、残留0で、入力はwheel 180件、drag complete 31件まで正常だった。直後の再試験はexit 0、wheel 238件、drag complete 9件、残留プロセス0だった。
- 125% Popupは実タスクバーをactual parentとして検証し、View／Control各1、最大各1、重複0、wheel 208件、drag complete 24件、timeout 0、churn failure 0、exit 0、終了後View／Control 0だった。一方、ユーザーが連続click-to-jump時に時々ちらつくことを確認したため、Issue #12として原因調査と修正を行った。
- 原因は、5秒UIA scanのたびに届く`ControlType.Pane`のbounds通知と、可視ホスト自身がnative探索で`UnknownObstacle`として再列挙されるフィードバックにより、不要なhide／recoveryが発生していたことだった。whole-window blinkの主因はこの表示遷移であり、direct GDI描画にも副次的なtearリスクがあった。
- 修正では、既知の非Button senderによるproperty通知だけを除外し、native child探索から現在のlive hostと完全一致するHWNDだけを除外した。Button、未知sender、structure通知、および別HWNDは従来どおりfail closedで扱う。安定したWatchdogは表示を維持し、GDIはcompatible memory DCへ全体描画後に1回の`BitBlt`で転送するdouble bufferへ変更した。同じclamp済み音量値の再設定は再描画しない。
- 修正後の125% Popupを17秒間10ms間隔で監査し、1104 samples、View最大1、hidden interval 0、visible Watchdog 3回、recovery 0、invalidation 0、残留0を確認した。Childも同条件で1105 samples、View最大1、hidden interval 0、visible Watchdog 3回、recovery 0、invalidation 0、残留0だった。
- Popupへdirect messageを300件送るrapid-click試験ではclick complete 150回、hidden 0、visible Watchdog 3回、recovery 0、残留0だった。その後、ユーザーが同じ125%構成で連続click-to-jumpとホイールを手動確認し、ちらつきが発生しないことを確認したため、Issue #12の受入条件を満たした。
- 同じ最小タスクバー設定の100%構成では、読み取り専用探索が`discoveryComplete=true`、`faults=[]`で完了した。タスクバーは3840×48 physical px、DPI 96、Start相対矩形は`(1568, 0, 45, 48)`、UIA button 24件、native critical child 5件で、相対矩形`(190, 4, 300, 40)`を`Place / Standard`と判定した。
- 100% Popupを17秒間、起動時に確定した同一HWNDで監視し、1087 samples、hidden／destroyed／rect drift 0、最終View 1を確認した。入力ログはinteraction start／complete 49／49、`DragMoved` 593件、`Wheel` 205件、verified-visible Watchdog 23回で、invalidation／recovery／churnは0だった。ユーザーのclick／drag／wheelと表示の手動確認もすべて良好だった。
- 100% Childも同じ固定HWND方式で17秒間監視し、1089 samples、hidden／destroyed／rect drift 0、最終View 1を確認した。入力ログはinteraction start／complete 50／50、`DragMoved` 506件、`Wheel` 515件、verified-visible Watchdog 11回で、invalidation／recoveryは0だった。ユーザーの手動確認でもちらつき、入力、表示に問題はなかった。
- 100%のChild／Popupを各6秒で再実行した終了試験は、両方式ともexit 0、fatal 0だった。終了後の独立列挙ではView／Control HWNDとQuickPodsプロセスがすべて0件だった。
- その後100%のまま、検索非表示、Task View OFF、Widgets OFF、左揃えへ変更した構成を探索した。タスクバーは3840×48 physical px、DPI 96、Start相対矩形は`(0, 0, 45, 48)`、UIA button 26件、native critical child 4件だった。探索は`discoveryComplete=true`、`faults=[]`で完了したが、安全幅不足のためboundsなしの`VerifiedNoFit / InsufficientWidth`となった。
- この100%左揃え構成でChild／Popupを起動すると、両方式ともホスト作成前にexit 3で終了した。終了後の独立列挙はView／Control HWNDとQuickPodsプロセスがすべて0件であり、推測配置しないFail Closedは期待どおりだった。これはネイティブ表示の成功ではなく、左揃えではフォールバックが必要になる証跡である。
- 100%のまま中央揃えへ戻し、検索アイコン、Task View OFF、Widgets OFFとした構成では、タスクバー3840×48 physical px、DPI 96、Start相対矩形`(1502, 0, 45, 48)`、UIA button 27件、native critical child 5件、fault 0を取得し、相対矩形`(173, 4, 300, 40)`を`Place / Standard`と判定した。Childの17秒固定HWND監視は1088 samples、hidden／destroyed／rect drift 0だった。入力ログはinteraction start／complete 31／31、move 458件、wheel 285件、Watchdog 11回、invalidation／recovery 0で、終了後のView／Control HWNDとQuickPodsプロセスも0件だった。ユーザーはちらつき、標準要素との重なり、表示、入力のすべてに問題がないことを確認した。
- 続いて検索ボックス、中央揃え、Task View OFF、Widgets OFFとした100%構成では、Start相対矩形`(1412, 0, 45, 48)`、UIA button 27件、native critical child 5件、fault 0を取得し、相対矩形`(151, 4, 300, 40)`を`Place / Standard`と判定した。Childの17秒固定HWND監視は1088 samples、hidden／destroyed／rect drift 0だった。入力ログはinteraction start／complete 10／10、move 295件、wheel 159件、Watchdog 11回、invalidation／recovery 0で、終了後残留も0件だった。ユーザーの手動確認でもちらつき、重なり、表示、入力のすべてに問題はなかった。
- Task ViewとWidgetsをONにした100%中央揃えの検索ボックス構成でも、Start相対矩形`(1390, 0, 45, 48)`、Widgets相対矩形`(6, 0, 152, 48)`、UIA button 29件、native critical child 5件、fault 0を取得し、相対矩形`(264, 4, 300, 40)`を`Place / Standard`と判定した。Childの17秒固定HWND監視は1088 samples、hidden／destroyed／rect drift 0だった。入力ログはinteraction start／complete 28／28、move 354件、wheel 70件、Watchdog 11回、invalidation／recovery 0で、ユーザーの表示・入力確認も良好、終了後残留0だった。
- 同じ100%中央揃えで検索をアイコン＋ラベル、Task ViewとWidgetsをONにした初期探索はcomplete、fault 0だった。Start相対矩形は`(1449, 0, 45, 48)`、Widgetsは`(6, 0, 152, 48)`、UIA button 29件、native critical child 5件で、相対矩形`(1028, 4, 300, 40)`を`Place / Standard`と判定した。初期描画と入力はユーザー目視で良好だった。
- ただし、Start／Windows一時UIと検索アイコン＋ラベルの検索一時UIは、どちらもタスクバー観測が一時的に`PrimaryTaskbarMissing`となる同じ事象を再現した。再現区間の外部inspectは57/57回が`TransientUnknown / IncompleteObservation`で、Runnerは推測配置せずhostを安全にhideした。隔離した制御runではStartが7.796秒、検索が6.757秒で同じhostへ復帰した一方、観測不能が10秒を超えると既存のFail Closed timeoutに達する。
- Issue #13の切り分け用に、探索完了、配置decision／reason、fault code、UIA button／native critical child件数、scan中のinvalidation、exact live-host除外一致のboolだけを保持するサニタイズ済みscan signatureを追加した。同一signatureの連続retryは初回と反復回数へ集約し、反復要約を復帰／timeout結果より先に出力する。raw HWND、PID、表示名、Automation ID、座標は出力しない。予期しないUIA側失敗でも、Win32探索済みのhost除外一致boolを失わない。
- 10秒未満の制御runではプロセスが存続したまま安全にhideし、10秒超ではSpikeが意図したFail Closedとして終了する。renderer／input flickerや予期しない即時クラッシュではないが、ユーザーにはどちらも「落ちた」ように見える復帰UXの問題である。フローティングフォールバック、10秒以内の一貫した復帰方針、および最終方式の判断は未完了である。
- 音量とBluetoothの状態は読み取りも変更もしていない。Explorer再起動はユーザー許可のGate B試験だけで行い、表示倍率とタスクバー設定はユーザーがWindows設定から変更した。Spike自身はExplorer再起動や設定変更を実行しない。

### 150%／WS_CHILD可視ホストのExplorer復旧

| Cycle | 外部監査の復旧時間 | Runner内部の復旧時間 |
|---:|---:|---:|
| 1 | 4063.743 ms | 1603.729 ms |
| 2 | 3542.965 ms | 1404.785 ms |
| 3 | 3491.972 ms | 1425.960 ms |
| 4 | 3527.358 ms | 1446.234 ms |
| 5 | 3548.686 ms | 1443.521 ms |
| 6 | 3544.118 ms | 1401.675 ms |
| 7 | 3493.850 ms | 1419.901 ms |
| 8 | 3671.977 ms | 1432.558 ms |
| 9 | 3626.727 ms | 1488.543 ms |
| 10 | 3645.586 ms | 1517.309 ms |

外部監査時間はShell停止直前から、新しいExplorer世代の配下でView 1件と、非表示top-level Control 1件を300ms連続して確認するまでを測った。全cycleで旧Viewの消失、新しいExplorer世代、`recreated=True`、`identity-changed=True`、`forced=True`を確認した。生PIDとHWNDは実行中比較にだけ使用し、保存していない。

外部監査は接続、可視性、現Shellとの親子関係、一意性、残留を独立検証したもので、UIA安全領域を再計算してはいない。各cycleの配置安全性はRunner内部のfresh complete scan、`SafeRegionCalculator`の最終交差検証、およびnative側の実bounds／parent／DPI検証を根拠とする。世代ごとの推奨X座標変化は、旧矩形がunsafeなら安全な新矩形へ再生成し、旧矩形がsafeならsticky placementで不要な移動を抑えるpolicyどおりだった。

Childの150%での静止画とドラッグ／ホイール、および125%両方式の入力ログは確認済みで、Issue #12はClose済みである。100%の検索非表示／アイコンのみ／ボックス、およびTask View／Widgets ON検索ボックスは固定HWND監視、入力ログ、ユーザー手動確認、終了後残存0に合格し、100%左揃えNoFitもFail Closedに合格した。一方、Start／Windowsと検索アイコン＋ラベルの一時UI復帰はIssue #13で未解決である。Issue #11のフローティングフォールバック、Issue #13、ピン留めアプリ多数のstress、およびChild／Popup最終方式選定が未完了であり、Gate BをGoとして扱わない。

以下は実画面を含まないサニタイズ済み配置図である。実画面スクリーンショットの代替ではなく、相対座標証跡の確認用とする。

![サニタイズ済みタスクバー配置](current-layout.svg)
