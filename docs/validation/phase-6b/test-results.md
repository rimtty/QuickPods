# Phase 6B 配布共通部分の検証結果

## 自動検証

2026-08-06のローカル結果は次のとおり。

- locked solution restore：成功
- format verification：差分なし
- Release `-warnaserror` build：警告0、error 0
- coverage付き全回帰：377／377 Pass
  - Foundation 77
  - Bluetooth KS 65
  - Core Audio 44
  - Default Endpoint Policy 7
  - Taskbar Host 183
  - Smoke 1
- NuGet vulnerability audit：19 projects、vulnerable entry 0

追加testはuninstaller境界の1件だけである。startup登録を無効化し、Bluetooth選択と他設定を保持したまま保存済み`StartWithWindows`だけをfalseへ変更することをfake registry／settings storeで確認した。実userのHKCU Run値は自動試験で変更していない。

## RC package

`0.1.0-rc.1`を同じsource／SDK／versionで二回生成した。

- ZIP byte数：83,569,731
- 1回目SHA-256：`03becf69bfd44de0a5b6a096f22dd10ac8dfba81af46b1a64ad49dd011535b75`
- 2回目SHA-256：`03becf69bfd44de0a5b6a096f22dd10ac8dfba81af46b1a64ad49dd011535b75`
- payload：495 files（manifestを含む）
- manifest entries：494
- legal files：3
- PDB：0

本体、TaskbarHost、TaskbarObserver、BluetoothWorkerのProductVersionはすべて`0.1.0-rc.1+<source revision>`、FileVersionは`0.1.0.0`で一致した。manifestはunsignedであることを明示する。packageには次を含む。

- `ThirdPartyNotices.txt`：Ceiling参照commitとMIT全文
- `DOTNET-LICENSE.txt`：固定SDKの.NET license
- `DOTNET-THIRD-PARTY-NOTICES.txt`：固定SDKのthird-party notices

## 未実施

installer形式とcode-signing方針は未決定であり、clean environmentのinstall／launch／update／uninstall／startup残骸確認は実施していない。Phase 6Bの最終mergeはロードマップどおりGate D [#48](https://github.com/rimtty/QuickPods/issues/48)通過後とする。

実Bluetooth [#38](https://github.com/rimtty/QuickPods/issues/38)／[#41](https://github.com/rimtty/QuickPods/issues/41)、local-console表示／電源 [#43](https://github.com/rimtty/QuickPods/issues/43)も未完了である。
