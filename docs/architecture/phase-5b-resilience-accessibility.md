# Phase 5B — 復旧性・アクセシビリティ

## Windowsライフサイクル

`QuickPods.exe`は表示設定、ユーザー設定、電源、セッション、およびWPF system parameterの通知を受ける。連続通知はUI Dispatcher上で750msに集約し、一回の復旧処理でCore Audio再bind、Bluetooth再列挙、テーマ／高コントラスト、製品画面位置、およびTaskbarHost snapshotを再評価する。Suspend、session lock、console disconnect、Remote Desktop connectでは、画面外や保護画面上へ古い製品画面を残さないためWPF画面を隠す。Resume／unlock／session切替後は同じ集約経路から状態を読み直す。

ライフサイクル通知は変更要求ではない。Bluetooth再評価は既存のread-only catalogを通り、RDPではローカルBluetoothを列挙せず空カタログへ安全縮退する。電源／セッションイベントだけを理由に接続、切断、既定出力変更を送らない。

## TaskbarHostの失敗予算

TaskbarHostは既存の1、2秒backoffと3回の連続失敗上限を維持する。上限到達後は製品本体と通知領域を停止せず、そのセッションのtaskbar surfaceだけを無効化する。電源、セッション、表示環境が実際に変化した場合だけ失敗数を0へ戻し、新しい環境に対して一度の復旧系列を許可する。通常のtimerやUI操作では失敗予算を復元しない。状態遷移は件数と分類だけを構造化ログへ記録する。

## 設定破損の隔離

`settings.json`がJSONとして読み取れない場合、読み込みを繰り返したり元内容を上書きしたりしない。同じ`%LocalAppData%\QuickPods`内の`settings.corrupt-<UTC>-<random>.json`へ原文を移動し、schema 1の安全な既定設定を新しい`settings.json`として原子的に再生成する。診断画面とログには隔離ファイル名と例外分類だけを出し、ユーザーパスや機器識別子を出さない。I/O権限障害は破損と混同せず、既存の設定読込失敗としてUIへ返す。

## 例外境界

Core Audio、Bluetooth catalog／mutation、設定、Windows設定launcherは既存の個別境界で例外を表示状態へ変換する。Phase 5BではTaskbarHostからDispatcherへ渡す非同期操作と、Windowsライフサイクル復旧全体にも境界を設けた。失敗は分類済みログと再試行導線へ変換し、resident processを終了させない。任意のUI programming errorを無条件に握りつぶすglobal handlerは設けない。

## アクセシビリティと高コントラスト

Bluetooth一覧は名前付き単一選択listとして公開し、各項目は機器名、選択、接続、既定出力、能力を含む`AccessibleName`を持つ。上下矢印は選択だけを変更し、Enter／Spaceはフォーカス中のbutton、Escapeは画面の非表示、音量sliderの矢印／Home／End／Page keyはCore Audioへの確定まで行う。主操作とmuteのAutomation nameは現在の状態に合わせて更新し、診断状態はlive regionで通知する。色に加えてradio形状、状態文、icon、focus borderを使用する。

WPFはHigh Contrast中にWindowsのWindow、WindowText、Control、Highlight、GrayText色へ追従する。native taskbar rendererも描画時に`SPI_GETHIGHCONTRAST`を確認し、同じsystem colorsへ切り替える。追加のHigh Contrast目視追試は2026-08-08のオーナー判断で最終Gateから除外したため、通常themeのlocal-console合格と区別し、High Contrastを新たに実機合格したとは主張しない。

## Validation status

- 100～350% DPI、keyboard、通常theme、real-login auto-start：Issue #43で合格
- 実Bluetooth列挙／接続／切断／既定出力：Issues #38／#41で合格
- 承認されたresource期間（AC-025）：Issue #48で2.01時間、1,391 sampleを受入
- High Contrast追加追試：最終Gateから除外。mixed-DPI monitor移動とmonitor hot-plugの包括的matrixも本受入では主張しない

自動試験とRDPでの安全縮退を物理成立性へ読み替えず、合格項目はlocal-console／実機証拠で判定した。
