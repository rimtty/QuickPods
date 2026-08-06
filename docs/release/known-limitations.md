# QuickPods 0.1.0 known limitations

- Windows 11 taskbarの左揃えではtaskbar操作バーを表示しない。通知領域と製品画面は利用できる。
- Bluetoothの直接接続／切断はdriver capability依存であり、未対応機器はWindows設定へfallbackする。
- RDP中はローカルBluetooth deviceを列挙・変更しない。Remote Audioの音量制御だけがCore Audioの既定endpointとして現れる場合がある。
- code signing証明書と最終installer形式は未決定である。現行成果物はunsigned portable RC ZIPであり、自動更新を行わない。
- local-consoleの実Bluetooth受入れ（#38、#41）、表示／電源受入れ（#43）、24時間resource／反復Gate（#48）は未完了である。
- RDP上の短時間予備計測では3 processの単純合算Working Setが約214MB、Private Memoryが約64MBだった。共有pageを重複計上するWorking Setだけで結論を出さず、#48で時間変化と150MB目標を正式評価する。
