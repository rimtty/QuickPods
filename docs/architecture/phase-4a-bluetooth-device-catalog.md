# Phase 4A — Bluetoothオーディオカタログ

## 境界

Phase 4Aは読み取り専用の物理デバイスカタログを提供する。接続、切断、既定出力変更、再ペアリング、Bluetooth無線の切り替えは行わない。

`BluetoothCatalogController`はWindows列挙と選択保存をポートの背後へ隔離し、次を保証する。

- Container IDをWindows境界内でSHA-256由来の`BluetoothDeviceKey`へ変換し、生のContainer／Endpoint／PnP／MAC識別子を外へ出さない。
- 同じ物理ContainerのA2DP／Hands-Free Endpointを一行へ集約する。
- 選択は利用者の明示操作だけで変更し、選択保存だけではOS状態を変更しない。
- 保存済み機器が消えた場合も別機器を自動選択せず、選択キーをstaleとして保持する。
- 並行refreshでは最新のinventory generationだけを公開する。
- 一つのEndpointの読取失敗を他の物理機器へ波及させない。

Windows実装はペアリング済みBluetooth Association EndpointのContainer IDとMMDeviceの`PKEY_Device_ContainerId`を結合する。表示名は表示用途に限定し、identity判定には使わない。

## アプリ統合

アプリ起動後、カタログをUIスレッドを塞がず初期化する。選択は`%LOCALAPPDATA%\QuickPods\settings.json`へ保存し、既存設定を保持する。選択済み機器が現在のカタログに存在するときだけ、既存のTaskbar snapshotへ名前と正規化済み状態を反映する。カタログ障害はマスター音量機能とアプリ寿命へ波及させない。

## Remote Desktop

RDPセッションではAssociation Endpoint列挙が完了せず、Remote AudioはローカルコンソールのBluetooth inventoryを表さないことを実機で確認した。RDP中はこの列挙を行わず、読み取り専用の空カタログへ即時縮退する。ローカルコンソールでの物理デバイス列挙、DPI、解像度、描画、入力座標の受け入れ確認は別Gateとして保留する。

## Phase 4B以降

KS Basic Supportに基づく直接操作能力の確定、接続／切断、既定出力変更、操作直列化はPhase 4Bで扱う。複数機器リストと操作ボタンを含むv2 UIはPhase 5で扱う。
