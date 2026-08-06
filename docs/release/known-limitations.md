# QuickPods 0.1.0 known limitations

- Windows 11 taskbarの左揃えではtaskbar操作バーを表示しない。通知領域と製品画面は利用できる。
- Bluetoothの直接接続／切断はdriver capability依存であり、未対応機器はWindows設定へfallbackする。
- RDP中はローカルBluetooth deviceを列挙・変更しない。Remote Audioの音量制御だけがCore Audioの既定endpointとして現れる場合がある。
- 正式installer形式はユーザー単位MSIに確定したが、RC MSIはcode signing証明書の準備まで未署名である。自動更新は行わない。
- local-consoleの実Bluetooth受入れ（#38、#41）、表示／電源受入れ（#43）、24時間resource／反復Gate（#48）は未完了である。
- RDP上の短時間予備計測では3 processの単純合算Working Setが約214MB、Private Memoryが約64MBだった。共有pageを重複計上するWorking Setだけで結論を出さず、#48で時間変化と150MB目標を正式評価する。

## Distribution

- RCのユーザー単位MSIはコード署名証明書の準備まで未署名である。正式releaseは署名済みartifactだけを対象とする。
- portable ZIPはbyte-for-byte deterministicである。MSIは決定的identity、同一database、同一payloadを再生成するが、WiX／Windows Installerのcompound storageとcabinet bindingによりMSI container全体のSHA-256はbuildごとに変わり得る。必ず同じCI runが出力した`SHA256SUMS.txt`を使用する。
- 固定per-user file packageはWiXの標準ICE64／ICE91がモデル化できないため、この2規則だけを抑止している。他のICE検証は有効である。
