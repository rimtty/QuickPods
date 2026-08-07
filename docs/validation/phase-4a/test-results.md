# Phase 4A 検証結果

## 自動検証

2026-08-06時点の作業ブランチで次を確認した。

- `dotnet build QuickPods.sln --no-restore`: 成功、警告0、エラー0
- Foundation focused tests: 55/55 Pass
- `dotnet format QuickPods.sln --verify-no-changes --no-restore`: Pass
- `git diff --check`: Pass

対象は0／1／複数／同名、物理キー集約、A2DP／Hands-Free profile集約、機器単位のfault isolation、明示選択、保存復元、選択機器消失、stale generation拒否、設定保存時の既存値保持、Container IDのopaque key化である。

## RDP安全縮退

RDPセッションからBluetooth Association Endpointを読み取り専用で照会したところ、複合queryおよびBluetooth Classic／LE個別queryはいずれも期限内に完了しなかった。製品ポートはRDPを検出すると9msでgenerationを維持した空カタログを返し、OSの音量、ミュート、接続、既定出力、Bluetooth無線へ変更要求を送らないことを確認した。

この結果はローカルBluetoothカタログの成立証拠には使用しない。物理AirPodsの列挙とA2DP／Hands-Free集約は[Issue #38](https://github.com/rimtty/QuickPods/issues/38)で追跡し、ローカルコンソールへ戻った後に実施する。DPI、画面解像度、レンダラー、Floating、クリック座標、ホイール入力の受け入れ試験は別Gateとして保留する。

## local-console予備確認（2026-08-07）

Windows 11の「Bluetoothとデバイス」へペアリング済みAirPods Proが表示される環境で、製品版カタログが同じ機器を1行として列挙し、選択状態を再起動後も復元することを確認した。Association Endpoint列挙だけへ依存せず、ConfigMgrのBluetooth PnP inventoryをContainer ID単位で集約し、Core Audio endpoint evidenceと結合する方式へ更新した。機器画像はWindowsの`DeviceInformation.GetThumbnailAsync`から取得し、取得不能時だけFluent glyphへ縮退する。

自己完結型`visual.72`では、ポップアップを表示する直前に読み取り専用のカタログ更新を行う。さらにCore Audioのデバイス追加・削除・状態・既定出力変更通知を450 msで集約して再取得するため、Windows設定から行った接続／切断も次回表示時には最新状態へ収束する。通知起点の更新はデバイス識別子を含めず`BluetoothTopologyRefreshRequested`として記録する。

正式画像伝播、処理対象行、通知burst集約を含む対象テスト7件、Release solution build（警告0／エラー0）、format、diff check、自己完結payloadの4 EXE／4 managed DLL／4 dependency manifest検査は合格した。操作者がWindows設定から接続／切断した際も、QuickPodsを開き直すと選択行と主操作が現行状態へ追従することを`visual.72`で目視確認した。AirPods Proの単一列挙と外部状態追従は成立したが、A2DP／Hands-FreeのContainer集約を示すsanitized profile証拠、および第二Bluetoothオーディオ機器を用いるfault isolationの物理確認が未完了のためIssue #38はopenのまま維持する。
