# Phase 5A 検証結果

## 自動検証

Phase 5Aでは過剰なedge-caseテストを増やさず、次の製品境界に絞ってFoundation試験を追加した。

- 同名を含むBluetooth行の表示状態と主操作
- `ConnectedNotDefault`、更新中、stale generationの表示
- 接続後の既定出力化をOFFにした際のPolicyConfig呼び出し0件
- 単一インスタンスのactivation handoffと明示終了policy
- HKCU Run adapterのON／OFF／照合（fake registry）
- Bluetooth選択と全製品設定のJSON round-trip／項目保持

2026-08-06の最終自動検証結果は次のとおり。

- `dotnet restore QuickPods.sln --locked-mode`：成功
- `dotnet format QuickPods.sln --verify-no-changes --no-restore`：差分なし
- Release `-warnaserror` build：警告0、エラー0
- 全回帰：374件すべてPass
  - Foundation 74
  - Bluetooth KS 65
  - Core Audio 44
  - Default Endpoint Policy 7
  - Taskbar Host 183
  - Smoke 1

初回GitHub Actionsでは既存Taskbar spikeのMTA watcher試験が、coverage付き並行実行中に5秒の偽タイムアウトとなった。専用MTAが完了した後の通知をThreadPool continuationへ戻していたことが原因であり、同期待機専用の通知だけをinline完了へ変更した。製品の5秒上限と試験ケースは緩和・削除していない。同型の製品Observer起動通知にも同じ修正を適用し、CIと同一のcoverage付き全374件がローカルでPassした。

## 非視覚プロセススモーク

2026-08-06、Releaseの`QuickPods.exe`をRDPセッションで`--background`起動し、次を確認した。

```text
SecondaryExitCode=0
MainWindowHandleAfterActivation=non-zero
CloseRequestAccepted=True
PrimaryAliveAfterClose=True
RemainingReleaseProcesses=0
```

二つ目の`QuickPods.exe`は既存プロセスへactivationを渡して終了した。既存画面へClose要求を送っても本体は終了せず常駐し、試験で起動した本体PIDを停止した後にTaskbarHost／Observer／BluetoothWorkerのReleaseプロセスが残らないことを確認した。試験終了は通知領域の明示終了ではなく、今回起動したPIDだけを停止するcleanupであるため、通知領域メニュー自体の目視はローカルコンソール確認へ残す。

## ログ

`%LocalAppData%\QuickPods\logs\quickpods-20260806.jsonl`の生成を確認した。観測したイベントは`ApplicationStarted`、Bluetoothカタログ件数／generation、製品設定であり、生のContainer ID、Endpoint ID、PnP ID、MACアドレス、アカウント識別子を含まない。

## ローカルコンソール検証（2026-08-07）

Issue [#43](https://github.com/rimtty/QuickPods/issues/43)の受け入れ確認を、Remote DesktopではないWindows 11のローカル`console`セッションで開始した。今回の部分検証条件は次のとおり。

- GPU／解像度：NVIDIA GeForce RTX 3080、3840×2160
- 拡大率：150%（`HKCU\Control Panel\Desktop\LogPixels=144`）
- タスクバー：中央揃え
- 候補：self-contained `visual.82`
- 実装SHA：`73cc515`（Phase 6Bへ`17111dd`としてmerge済み）
- 起動前提：`StartWithWindows=false`、HKCU Runの`QuickPods`値なし

キーボード操作は利用者の目視と実操作で次を確認した。

- `Tab`でフォーカスを移動できる
- 音量スライダーを矢印キーで変更できる
- `Space`でミュート／解除を切り替えられる
- `Escape`でflyoutを閉じられる

通知領域と常駐ライフタイムは、利用者が次の一連の操作を実施し、異常がないことを確認した。

- 通知領域アイコンの右クリックメニューを表示できる
- ダブルクリックと`QuickPods を開く`の両方が既存画面を表示する
- 画面の閉じる操作後もQuickPodsとタスクバー面が常駐する
- `タスクバー操作バーを表示`をOFFからONへ戻すと、面が重複せず復帰する

表示ON／OFF時のログには`DisplayMode=TrayOnly`、続いて`DisplayMode=Auto`の`SettingsApplied`が1回ずつ記録された。復帰後のプロセスは`QuickPods`、`QuickPods.TaskbarHost`、`QuickPods.TaskbarObserver`が各1件で、重複と孤立helperはなかった。ObserverのPID更新は表示面の再生成に伴う想定内の遷移である。

単一インスタンスは候補EXEを再度起動して非視覚的にも確認した。二つ目の`QuickPods.exe`（PID 19512）は5秒以内に終了コード0で終了し、既存の`QuickPods`、`QuickPods.TaskbarHost`、`QuickPods.TaskbarObserver`はそれぞれ同一PIDのまま1プロセスずつ維持された。重複プロセスと孤立helperは発生していない。

### live DPIマトリクス

150%から100%へ変更した後、非表示中のflyoutが保持していた古いWPF DPIで新しい物理pxアンカーを変換し、タスクバーから大きく離れる欠陥を[#71](https://github.com/rimtty/QuickPods/issues/71)で検出した。taskbar面が通知するアンカーへ取得時DPIを含め、そのDPIでDIPへ変換する修正版`visual.83`（実装SHA `ac1df61`、Phase 6Bへ`6d0ee48`としてmerge）を作成した。

QuickPodsを終了せず、Windowsが提示する次の全拡大率でflyoutのtaskbar追従と想定操作を利用者が目視確認し、すべて合格した。

- 100%、125%、150%、175%、200%、225%、250%、300%、350%

候補を15:14:06 JSTに起動した後の`quickpods-20260807.jsonl`には、初期点以降の8回の切替に対応する`DisplaySettingsChanged`／work-area回復が8回記録された。TaskbarHostは起動時の`Starting`から`Connected`へ1回遷移した後に再接続／失敗せず、Warning／Errorは0件だった。確認終了時も`visual.83`由来のApp、TaskbarHost、TaskbarObserverが各1プロセスだけ存在した。自動試験も同じ実効DPI 96／120／144／168／192／216／240／288／336を対象とする。

この結果によりDPI目視Gateは完了した。設定／自動起動の実レジストリ連携、明示終了、テーマ、およびExplorer回復は、この時点では未完了として[#43](https://github.com/rimtty/QuickPods/issues/43)を開いたままにした。

### 設定永続化・自動起動・明示終了（visual.86）

2026-08-07、RDPセッションで自己完結候補`visual.86`を使用し、画面の見た目ではなく設定JSON、実ユーザーのHKCU Run値、および候補配置に属するプロセスだけを客観確認した。利用者が通知領域の設定画面から次の値へ変更した。

- テーマ：ダーク
- ホイール刻み：5%
- 接続後に既定の音声デバイスへ設定：OFF
- Bluetooth切断前に確認：ON
- Windowsログイン時に自動起動：ON

`%LocalAppData%\QuickPods\settings.json`は順に`Theme=Dark (1)`、`MouseWheelStepPercent=5`、`SetConnectedDeviceAsDefault=false`、`ConfirmBluetoothDisconnect=true`、`StartWithWindows=true`を保持した。HKCU Runの`QuickPods`値は次の完全一致となった。

```text
"E:\tool\QuickPods\artifacts\taskbar-anchor-visual86\QuickPods\QuickPods.exe" --background
```

利用者が通知領域メニューの明示的な`終了`を選択すると、この候補配置に属する`QuickPods`、`QuickPods.TaskbarHost`、`QuickPods.TaskbarObserver`は0件となり、設定JSONとRun値は保持された。同じRun値の`--background`条件で再起動すると、3プロセスが各1件だけ再生成され、いずれも`MainWindowHandle=0`のまま、変更した5設定を保持した。

最後に利用者がテーマをWindows準拠、刻みを2%、既定化をON、切断確認をOFF、自動起動をOFFへ戻した。設定JSONは`Theme=System (0)`、`MouseWheelStepPercent=2`、`SetConnectedDeviceAsDefault=true`、`ConfirmBluetoothDisconnect=false`、`StartWithWindows=false`となり、HKCU Runの`QuickPods`値は削除された。

以上により、実ユーザー領域での即時保存、明示終了、背景再起動後の保持、Run登録／削除は合格とする。RDPではWindowsへの再ログインそのものとテーマの最終描画を証明できないため、その視覚・セッション項目は[#43](https://github.com/rimtty/QuickPods/issues/43)へ残す。

同じRDPセッションで通知領域の設定画面から`ログを開く`を実行し、QuickPodsログフォルダーが正常に開くことを確認した。`診断情報をコピー`の出力はversion、Audio capability、volume／mute、Bluetooth device count、selected device／status、display mode、theme、wheel step、startup stateだけを含み、生のContainer／Endpoint／PnP／MAC IDやアカウント識別子を含まなかった。補助操作とsanitizationを合格とする。

## 初回検証時に合否を保留した項目

2026-08-06の初回検証はRemote Desktopだったため、次をその時点のPhase 5A合格証拠として扱わなかった。DPI、通知領域、flyout入力は翌日のローカルコンソール証拠で補完したが、mixed DPIと物理Bluetoothの別機器条件は個別Issueで引き続き追跡する。

- mixed DPIとモニター間移動
- native／floatingの配置、描画、クリック位置、ホイール入力
- 通知領域アイコンとメニューの目視・キーボード操作
- 実Bluetoothオーディオの列挙、接続、切断、既定出力化

通知領域／製品画面の残項目は[#43](https://github.com/rimtty/QuickPods/issues/43)で管理する。実Bluetooth列挙と物理操作は[#38](https://github.com/rimtty/QuickPods/issues/38)／[#41](https://github.com/rimtty/QuickPods/issues/41)で完了済みである。RDP安全縮退を物理成立性の代替にしない。
