# Phase 5C — タスクバー・ホバーフライアウトUI fidelity

## 目的

Issue [#53](https://github.com/rimtty/QuickPods/issues/53)で、2026-08-06に再提示された3つのモックを製品UIの視覚基準へ昇格する。対象はタスクバー常設面、空Bluetooth一覧、複数デバイス一覧、音量・ミュート、主操作、Windows設定導線である。Bluetooth列挙／操作のOS境界はPhase 4のまま変更しない。

## タスクバー常設面

中央揃えをサポートするnative／floating面は40 DIPの中に、スピーカー／ミュート、短い音量trackとthumb、数値%、separator、ヘッドホンと状態dot、選択デバイス名・状態を描画する。未選択時も`BTデバイスなし`を表示する。standard幅では全要素を表示し、compact幅では同じ順序のまま末尾だけをellipsisする。描画とpointer hit-testは同じ`SliderGeometry`を使う。

## ホバー状態機械

TaskbarHostは`TrackMouseEvent`で一回のenter世代につき180 ms hoverを一度だけ発行する。drag中はpreviewを開かない。TaskbarHostは次をIPCで送る。

- `PreviewAudioFlyout`：非activateで表示
- `TaskbarPointerExited`：previewを閉じるgrace timerを開始
- `OpenAudioFlyout`：clickで表示・activate

flyout interactionにはTaskbarHostが検証済みscreen boundsを`TaskbarSurfaceAnchor`として付ける。WPFは現在のDPI scaleでdevice pixelからDIPへ変換し、surface中央の上6 DIPへflyoutを配置する。monitor中央はanchorを取得できない通知領域／既存instanceへの再起動要求だけのfallbackである。初回起動は引数の有無にかかわらずtray常駐で開始し、flyoutを表示しない。tray iconの生成自体に失敗した場合だけ、操作不能を避けるためflyoutをfallback表示する。

Taskbarからflyoutへpointerを移動できるよう、exitから420 msは閉じない。flyoutへenterするとtimerを停止し、preview状態でflyoutからもleaveした場合だけ再開する。hover表示はforeground focusを奪わず、click表示だけをactivateする。

## フライアウト

標準title barを持たない固定幅のrounded flyoutとし、Windows 11のdark surface、細いoutline、shadow、cyan accentを使用する。状態別構成は次のとおり。

- 空一覧：headphones＋Bluetooth glyph、`デバイスが見つかりません`、説明、compactな`再検索`
- 一覧あり：行全体を選択targetとするradio row、type glyph、名前、`ペアリング済み`、検証済み状態。選択行だけcyan outline
- 共通：volume label／数値／slider／mute、全幅の接続・切断action、状態に応じたサウンドまたはBluetooth設定導線

一覧選択、接続、切断、既定出力化、音量、mute、refresh、keyboard、automation nameの意味はPhase 5Aから変更しない。空一覧のdisabled主操作labelはモックどおり`接続`とする。詳細なQuickPods設定はflyoutへ含めず、tray iconの右クリックメニューから独立した設定windowを開く。

## 非対象とvalidation status

- 左揃えタスクバーは非対応policyを維持する。
- 実Bluetooth一覧と接続／切断は[#38](https://github.com/rimtty/QuickPods/issues/38)／[#41](https://github.com/rimtty/QuickPods/issues/41)で合格した。
- RDPで判定しなかった100～350% DPI／通常theme／keyboard／real-loginは[#43](https://github.com/rimtty/QuickPods/issues/43)のlocal-console Gateで合格した。High Contrast追加追試は除外した。
