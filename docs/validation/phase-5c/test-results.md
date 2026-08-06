# Phase 5C UI fidelity 検証結果

## 2026-08-06 自動検証

- `QuickPods.App` Release win-x64 `-warnaserror` build：警告0、error 0
- focused Foundation回帰：77／77 Pass（既存test数を増やさず、protocol／rendering／native interactionの既存3 testへ必要なassertionだけ追加）
- 変更後sourceから全solutionを再buildした回帰：377／377 Pass（Smoke 1、Foundation 77、DefaultEndpointPolicy 7、CoreAudio 44、TaskbarHost 183、BluetoothKs 65）
- GitHub Actions PR #54 `Windows / .NET 10 / Release`：Pass
- hover session：同一enter世代でarm／previewが各1回、leave後だけ再arm
- protocol：taskbar anchorを含むpreview interactionのJSON往復
- rendering：0～100%正規化、未選択`BTデバイスなし`、選択名・状態のprojection

## 現環境での視覚確認

Releaseのbackground起動でTaskbarHostがnative面へ接続し、タスクバー面はspeaker、slider、`100%`、separator、修正後headphones／status dot、`BTデバイスなし`を表示した。空一覧flyoutはborderless rounded surface、Bluetooth empty-state、再検索、volume、disabled接続、Bluetooth設定導線を表示した。空の診断TextBlock 3個が確保していた不要な下部領域をcollapseし、header refreshをtransparent surfaceへ調整した。

background起動後、実pointerをnative面（`left=662, top=2083, right=1187, bottom=2153` physical px）の中央へ移動してhoverを発生させた。flyoutは`left=573, top=1096, right=1277, bottom=2076`で表示され、surface中心`924.5 px`に対するflyout中心は`925 px`（差`0.5 px`）だった。monitor中央fallbackへの上書きは発生しなかった。

pointerをtaskbar面からflyout内部へ移した後800 ms待っても表示を維持し、外へ移して700 ms後には非表示となった。App／TaskbarHostはいずれも応答状態を維持した。現在はRDPセッションであるため、この結果はhover protocolと相対配置の合格証跡に使い、DPI差の最終合否には使用しない。

## 未確認

- ユーザー目視による最終的な余白・アイコンの主観確認
- 実Bluetooth 1台／複数台でのrow密度、long-name ellipsis、接続状態
- local-consoleでの100／125／150／200%最終描画
