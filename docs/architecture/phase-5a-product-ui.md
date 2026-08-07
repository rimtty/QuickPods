# Phase 5A — 製品UI・常駐・ユーザー設定

## 製品シェルの責務

`QuickPods.exe`はWPF製品画面、通知領域、単一インスタンス、設定、ログ、および子プロセスの寿命を所有する。`QuickPods.TaskbarHost.exe`、`QuickPods.TaskbarObserver.exe`、`QuickPods.BluetoothWorker.exe`をWindowsログイン時の自動起動へ直接登録しない。

画面はBluetoothやCore AudioのOS型を直接扱わない。Bluetooth一覧と操作中状態は`QuickPods.Presentation`の`BluetoothProductPresenter`で不変な表示状態へ射影し、WPFは明示選択、更新、主操作、音量、設定導線だけをバインドする。同名機器もopaqueな`BluetoothDeviceKey`で別行として保持するが、識別子そのものは表示、診断コピー、通常ログへ出さない。製品画面はプライマリタスクバーのwork area直上へ中央配置し、通知領域とタスクバーhostのどちらから開いても同じ画面を復元する。mixed-DPI／複数monitorの最終配置確認はPhase 5Bへ残す。

## Bluetooth表示と主操作

一覧更新と行選択は読み取り専用であり、接続、切断、既定出力変更を送信しない。主ボタンだけが状態に応じて次の一操作を要求する。

- 未接続かつ直接操作可能：接続
- 接続済み・非既定：既定出力へ設定
- 接続済み・既定：切断
- `SettingsOnly`または所有権不明：Windows Bluetooth設定
- 更新中、操作中、一時利用不可：無効

古いinventory generationに属する操作結果は現在行へ重ねない。接続後の既定出力設定をOFFにした場合は、接続確認後にPolicyConfigを呼ばず`Connected + NotDefault`を返す。既定値はONである。切断確認の既定値はOFFで、ONの場合だけWPFの確認を通過してからCore操作を一回送る。

## 常駐と単一インスタンス

WPFの`ShutdownMode`は`OnExplicitShutdown`である。通常のウィンドウCloseはキャンセルして非表示にし、通知領域メニューの明示的な「終了」だけが終了policyを立ててウィンドウ、TaskbarHost、Observer、Bluetooth worker境界を破棄する。通知領域では次を提供する。

- QuickPodsを開く
- 音声／Bluetooth状態を更新
- タスクバー操作バーの表示／非表示
- Windowsサウンド設定／Bluetooth設定
- 終了

単一インスタンス境界はユーザーセッション内の名前付きkernel objectで存在を保持し、別起動はAutoReset eventで既存インスタンスへ表示要求を渡して終了する。表示要求はWPF Dispatcherへ戻して既存画面を復元・前面化する。

## 設定と自動起動

`%LocalAppData%\QuickPods\settings.json`は一時ファイルからの置換で保存する。Bluetooth選択と製品設定は同じ`SemaphoreSlim`境界内でread-modify-writeし、並行更新で別項目を失わない。現在保存する項目は次のとおり。

- Bluetooth単一選択
- タスクバー表示（自動／通知領域のみ）
- 接続後の既定出力化
- ホイール刻み（1／2／5／10%）
- テーマ（Windows準拠／ダーク／ライト）
- 切断確認
- Windowsログイン時の自動起動
- 旧設定との互換性を保つ`PreferNativeTaskbarSurface`

値は読み込み時に正規化し、未知のenum、未対応のホイール刻みを安全な既定値へ戻す。自動起動のユーザー意図は設定JSONに保持し、起動時に所有するRun値を現在配置の本体パスへ冪等に修復・照合する。設定保存に失敗した変更は直前の登録状態へ戻す。

自動起動は`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`の`QuickPods`値だけを使用する。登録内容は現在配置の`QuickPods.exe`を引用符で囲み、`--background`を付けたものとする。初期値はOFFで、ON／OFFは冪等であり、管理者権限、Helperの個別登録、Bluetooth操作を必要としない。背景起動でもCore Audio、Bluetoothカタログ、TaskbarHostは初期化し、製品画面だけを表示しない。

## 表示モード・テーマ・ログ

通知領域のみへ変更すると、IPC snapshotの`SurfaceMode`を`Hidden`へ変更する。TaskbarHostは現在のnative／floating面とObserverを破棄し、再び自動へ戻した場合だけ新しい探索世代から面を生成する。左揃え非対応policyと安全なNoFit判定は変更しない。

テーマ変更はWPF resource brushへ反映する。Windows準拠は現在の`AppsUseLightTheme`を読み、動的なOSテーマ／高コントラスト追従はPhase 5Bのライフサイクル試験で扱う。WPFが既存のPerMonitorV2 manifestを所有し、WinFormsは`NotifyIcon`だけに限定する。RDP中のDPI／描画合否には使用しない。

構造化ログは`%LocalAppData%\QuickPods\logs\quickpods-YYYYMMDD.jsonl`へ追記する。通常ログは件数、generation、分類済み状態、設定値だけを記録し、Container／Endpoint／PnP／MAC／アカウント識別子を記録しない。画面からログフォルダーを開き、同じ制約の診断概要をクリップボードへコピーできる。

## Validation status

- 実Bluetooth列挙：Issue #38で合格
- 実AirPods接続／切断／既定出力：Issue #41で合格
- 通知領域、製品画面、DPI、配置、クリック／ホイール、通常theme、実ログイン自動起動：Issue #43で合格
- Explorer recovery：Issues #34／#18で合格。#33の専用NoFit再現は2026-08-07に不要と判断

これらは自動テストへ読み替えず、それぞれのlocal-console／実機証拠で判定した。High Contrast追加追試は2026-08-08のオーナー判断で最終Gateから除外した。
