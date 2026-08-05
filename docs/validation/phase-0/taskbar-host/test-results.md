# Phase 0C 検証結果

## 結果概要

| 項目 | 結果 |
|---|---|
| ブランチ | `codex/phase-0c-taskbar-host-spike` |
| 検証対象 | 本ブランチのPhase 0C作業ツリー（コミット前検証） |
| Release build | **Pass** — 0 warning / 0 error |
| 自動試験 | **Pass** — TaskbarHost 188件、Smoke 1件、計189件 |
| format | **Pass** — `--verify-no-changes --severity info` |
| diff check | **Pass** — Phase 0C作業ツリー全体に空白エラーなし |
| 読み取り専用UIA探索 | **Pass（現在環境）** — Start／Widgetsを一意に取得、automation button 30件、native critical child 5件 |
| UIA監視・復旧 | **Pass（自動試験）／Partial Pass（現在環境）** — 即時無効化、fresh scan fence、hide-first復旧、churn guard、NoFit構成のExplorer再検出を確認。可視ホスト再接続は未確認 |
| 擬似親HWND | **Pass（自動試験）** — attach、実親／PMv2検証、hide/show、親消失、通知race、破棄 |
| 透過・入力 | **Pass（機構の自動試験）** — color-key、透明corner pixel、共通slider座標、hit target、GDI解放 |
| 実タスクバー可視試験 | **Partial Pass** — Child／Popupを各30秒実行してexit 0。目視品質と実入力は未確認 |
| 200%／Widgets ON | **Pass（Fail Closed）** — `VerifiedNoFit / InsufficientWidth`。可視ホストは作成しない |
| Explorer再起動 | **Partial Pass** — 200%／NoFit構成で10回すべて新しいExplorer世代を検出し、完全観測へ10秒以内に復帰。可視ホスト再接続は未確認 |
| 終了後残存 | **Pass（追加観測）** — View HWND 0、Control HWND 0、QuickPodsプロセス0 |

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
- 各方式で`External / BoundingRectangleChanged`を6回受信した。各イベント後にfresh scanを行い、同一タスクバー上の現在矩形が引き続き安全であることを確認したため、6回とも`recreated=False`で既存ホストを表示継続した。
- UIA watcherは専用MTA thread上で登録・解除を同一threadに限定し、callbackではsender-only cacheの識別メタデータだけを読む。taskbar root配下のproperty changeと、一意なStartの直近ControlView親配下のstructure changeを監視し、subscription epochで旧rootからの遅延callbackを棄却する。
- 無効化を受けるとホストを先に隠し、watcher世代で囲ったfresh scanを終えるまで再表示しない。UIA通知を補う5秒Watchdogも維持する。
- churn guardは連続10秒または30秒内6回でfail closedとする。sparse上限だけは、`External / BoundingRectangleChanged`、同一identity、fresh scanで既存矩形が安全、かつ`ShowVerifiedExisting`となる条件がすべて揃った場合に限り、そのsparse履歴をacknowledgeする。
- 200%／Widgets ONでは安全幅不足を`VerifiedNoFit / InsufficientWidth`として検出し、Child／Popupともホスト作成前に終了した。
- 同じ200%／NoFit構成でExplorerを10回再起動した。Shell_TrayWndは約0.24～0.36秒で新しい世代へ切り替わり、UIAを含む完全観測は全回10秒以内（最大3.897秒）に復帰した。全回で同じNoFit判定を再現し、終了後の独立列挙はView／Control HWNDとQuickPodsプロセスがすべて0件だった。
- Shell_TrayWnd復帰直後の単発観測は一度`TransientUnknown`となったため、Gateの判定は製品policyと同じ250ms再試行・最大10秒で行った。これは不完全観測を成功扱いに変えたものではない。
- 音量とBluetoothの状態は読み取りも変更もしていない。ユーザー許可のもとでExplorer再起動試験を行ったが、表示設定は変更していない。

目視による既存UIとの重なり、残像、描画負け、スライダー入力位置は未確認である。DPI 100／125%、200%でPlaceとなる構成、Start左寄せ、Widgets OFF、検索形式、空き不足、可視ホスト実行中のExplorer再起動10回、およびChild／Popupの最終方式選定も未実行であり、Gate BをGoとして扱わない。

以下は実画面を含まないサニタイズ済み配置図である。実画面スクリーンショットの代替ではなく、相対座標証跡の確認用とする。

![サニタイズ済みタスクバー配置](current-layout.svg)
