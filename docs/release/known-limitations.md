# QuickPods 0.1.0 known limitations

- Windows 11 taskbarの左揃えではtaskbar操作バーを表示しない。通知領域と製品画面は利用できる。
- Bluetoothの直接接続／切断はdriver capability依存であり、未対応機器はWindows設定へfallbackする。
- RDP中はローカルBluetooth deviceを列挙・変更しない。Remote Audioの音量制御だけがCore Audioの既定endpointとして現れる場合がある。
- 正式installer形式はユーザー単位MSIに確定し、標準ユーザーでのinstall／常駐中upgrade／常駐中uninstallは合格した。RC MSIはcode signing証明書を用意していないため未署名であり、自動更新は行わない。
- local-consoleの実Bluetooth一覧・profile集約・接続／切断／既定出力は#38／#41で合格し、追加の50-cycle耐久は2026-08-07に不要と判断した。resource観測は2.01時間、1,391 sampleの短縮Gate Dを承認して#48を完了した。通常theme／real-login自動起動は#43、Explorer実再起動後の回復と世代resourceは#34／#18で完了した。High Contrast追加追試はオーナー判断で最終Gateから除外した。
- RDP上の短時間予備計測では3 processの単純合算Working Setが約214MB、Private Memoryが約64MBだった。local-console Gate Dでは2.01時間の時間変化をPrivate Memory、Handle、GDI、USERと併記して確認し、共有pageを重複計上するWorking Setの絶対値だけでは判定していない。

## Distribution

- RCのユーザー単位MSIは未署名である。非署名スコープの製品受入は完了しているが、一般公開用の署名済みartifact、timestamp、Authenticode `Valid`は未検証であり、将来のpublication Gateとして別途承認する。
- portable ZIPはbyte-for-byte deterministicである。MSIは決定的identity、同一database、同一payloadを再生成するが、WiX／Windows Installerのcompound storageとcabinet bindingによりMSI container全体のSHA-256はbuildごとに変わり得る。必ず同じCI runが出力した`SHA256SUMS.txt`を使用する。
- 固定per-user file packageはWiXの標準ICE64／ICE91がモデル化できないため、この2規則だけを抑止している。他のICE検証は有効である。
