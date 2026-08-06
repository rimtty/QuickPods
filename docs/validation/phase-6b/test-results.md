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
- NuGet vulnerability audit：solution 19 projectsとWiX installer project、vulnerable entry 0

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

## ユーザー単位MSI

2026-08-06にWiX Toolset 6.0.2を用いるx64ユーザー単位MSIを実装した。同一の495-file RC payloadから二回buildし、どちらもWiX buildは警告0、error 0、MSI内部検証は成功した。

- install scope：`perUser`、`ALLUSERS`なし、elevated privilege bitなし
- install root：`PerUserProgramFilesFolder\QuickPods`
- 必須実行file 4件とpayload manifestをMSI File tableで確認
- Start menu shortcut、Major Upgrade、決定的ProductCode／PackageCodeを確認
- service、ServiceControl、ODBC、environment variable tableなし
- 更新／uninstall時に実行中のQuickPodsへ終了messageを送り、5秒後も残る場合だけ終了するWiX metadataを確認
- 完全uninstall時だけ`QuickPods.exe --unregister-startup`をfile削除前に実行し、upgrade時は除外
- RC artifact version：`0.1.0-rc.1`
- Windows Installer version：`0.0.60001`（正式`0.1.0`より低いpre-release mapping）
- signature：`NotSigned`（証明書準備後の正式release gate）
- MSI file count：495
- lifecycle verifier safety preflight：既存`%LOCALAPPDATA%\QuickPods`を検出してMSI起動前に拒否し、install folder、Run値、QuickPods processはいずれも0件のまま

MSIの二回buildは正規化したProductCode、PackageCode、summary timestamp、およびdecompile後の全database tableが一致した。MSI containerのbyte列はWiX／Windows Installerのcompound storage／cabinet bindingにより一致せず、各buildのSHA-256はそれぞれ`8daba295b600b6ede8860f67c02ab91c4413ae390dd34b5ea15c848beb995268`と`667f93d69a7d84b56a55537fe6ec3aab998e80fae26bac920c4d9236041c938f`だった。したがってportable ZIPのbyte-for-byte deterministic保証は維持する一方、MSIは同一identity／同一database／同一payloadの再生成とbuildごとのchecksum発行を保証範囲とする。

2026-08-06に署名入力経路を追加した。`Publish-Installer.ps1`はPFXをephemeral user keyとして読み込み、private key、証明書有効期間、code-signing EKU、HTTP timestamp、最終`Valid` statusをfail-closedで検証する。`-RequireSignature`だけを指定した誤構成と、PFX passwordだけを指定した誤構成はMSI build前に拒否する。正式workflowは保護environmentのsecretを一時PFXへ復元し、常にcleanupした後、署名済みMSIだけをuploadする。通常CIはsecretを参照せず未署名検証を維持する。実証明書による署名結果は証明書準備後のrelease gateに残す。

同じ監査で、実行中のQuickPodsが既定のproject `bin`をロックするとローカルRC publishが失敗することを検出した。RC生成はoutput directory配下の一時`--artifacts-path`へrestore／publish中間成果物を隔離し、成功／失敗後に削除するよう変更した。これにより目視確認用QuickPodsを停止せず、配布payloadを別経路で再生成できる。

固定per-user file packageに対するWiX公式既知制約のためICE64／ICE91だけを抑止し、他のICE検証は有効である。

## 未実施

clean local-console環境でのinstall／launch／update／uninstall／startup残骸確認と、署名証明書を用いた署名検証は実施していない。`build/Test-InstallerLifecycle.ps1`は既存user stateを拒否した上で旧版install、常駐中upgrade、常駐中uninstall、startup／user-data境界を一回で検証するが、現ホストにはWindows Sandboxが導入されておらず、通常user環境を変更して結果を代用していない。Phase 6Bの最終mergeはロードマップどおりGate D [#48](https://github.com/rimtty/QuickPods/issues/48)通過後とする。

実Bluetooth [#38](https://github.com/rimtty/QuickPods/issues/38)／[#41](https://github.com/rimtty/QuickPods/issues/41)、local-console表示／電源 [#43](https://github.com/rimtty/QuickPods/issues/43)も未完了である。
