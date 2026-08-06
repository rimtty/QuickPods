# Phase 5B 検証結果

## 自動検証

過剰なedge-case試験を増やさず、今回新しく成立させた二つの安全境界だけを追加した。

- 不正JSONを原文のまま隔離し、安全なschema 1設定を再生成する
- 連続失敗で無効化したTaskbarHostを、環境変更時だけ新しい失敗予算へ戻す

2026-08-06のローカル自動検証結果は次のとおり。

- `dotnet format QuickPods.sln --verify-no-changes --no-restore`：差分なし
- Release `-warnaserror` build：警告0、エラー0
- 全回帰：376件すべてPass
  - Foundation 76
  - Bluetooth KS 65
  - Core Audio 44
  - Default Endpoint Policy 7
  - Taskbar Host 183
  - Smoke 1

## 非視覚プロセススモーク

Releaseの`QuickPods.exe`をRDPセッションで`--background`起動した。primaryは初期化後も生存し、二つ目の起動はexit code 0で既存インスタンスへactivationを渡した。非表示画面への`CloseMainWindow`はWindowsから受理されなかったが、primaryは継続して常駐した。試験で開始したprimary PIDを停止した後、同じRelease出力フォルダーのQuickPods／TaskbarHost／Observer／BluetoothWorker processが0件であることを確認した。

この試験は単一インスタンスと子process cleanupの非視覚証拠だけであり、通知領域、focus、keyboard、DPI、rendererの見た目を合格にしない。

## コード上の復旧経路

- display／theme／high-contrast／power／session通知を750msで集約
- suspend／lock／RemoteConnectで製品画面を非表示
- resume／unlock／session／display変更後にaudio、read-only Bluetooth catalog、theme、placement、taskbar snapshotを再評価
- taskbar hostは連続失敗上限後も本体を停止せず、実環境変更後だけ再試行
- taskbar interactionとlifecycle recoveryの例外を分類済みログへ変換
- WPFとnative taskbarのhigh-contrast system color経路
- list／button／slider／live statusのAutomation metadataとkeyboard確定経路

## 現環境で合否を出さない項目

現在はRemote Desktopであるため、次はIssue [#43](https://github.com/rimtty/QuickPods/issues/43)で別日のlocal-console試験に残す。

- 200%以上およびmixed DPIの見た目、hit position、wheel
- High Contrastの色、focus、screen reader読み上げ
- sleep／hibernate復帰、RDP接続解除、monitor抜き差し
- native／floatingの配置とrenderer

物理Bluetooth列挙は[#38](https://github.com/rimtty/QuickPods/issues/38)、接続／切断／既定出力は[#41](https://github.com/rimtty/QuickPods/issues/41)のまま未完了とする。24時間resource試験（AC-025）はPhase 6で行う。
