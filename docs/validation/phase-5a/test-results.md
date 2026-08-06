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

## 現環境で合否を出さない項目

現在はRemote Desktopであるため、次をPhase 5A合格の証拠として扱わない。

- 100／125／150／200% DPI、解像度、mixed DPI
- native／floatingの配置、描画、クリック位置、ホイール入力
- 通知領域アイコンとメニューの目視・キーボード操作
- 実Bluetoothオーディオの列挙、接続、切断、既定出力化

通知領域／製品画面／DPIのローカル確認は[#43](https://github.com/rimtty/QuickPods/issues/43)、実Bluetooth列挙は[#38](https://github.com/rimtty/QuickPods/issues/38)、物理操作は[#41](https://github.com/rimtty/QuickPods/issues/41)で別日に確認する。RDP安全縮退を物理成立性の代替にしない。
