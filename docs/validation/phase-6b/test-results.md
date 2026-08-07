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

2026-08-06、開発用framework-dependent `bin/Release`から起動したTaskbarHost／Observerがx64 .NET runtime未導入時にruntime取得を要求したことを受け、配布境界を再検証した。CI run 31109912595のRCは`includedFrameworks`として`Microsoft.NETCore.App 10.0.10`と`Microsoft.WindowsDesktop.App 10.0.10`を持ち、`hostfxr.dll`、`hostpolicy.dll`、`coreclr.dll`、`PresentationFramework.dll`を同梱し、ZIP checksumも一致した。`DOTNET_ROOT`／`DOTNET_ROOT_X64`を存在しないdirectoryへ固定し、multilevel lookupを無効にしたsmokeではTaskbarHostがhelp exit 0、Observer／BluetoothWorkerがそれぞれアプリ定義のinvalid-argument exit 2へ到達し、新しいruntime-missing eventは0件だった。

この境界を将来も維持するため、RC生成とMSI payload取込の双方で全4 runtime configに外部`framework`／`frameworks`参照がなく、必要な`includedFrameworks`とapp-local runtime fileが存在することを必須化した。MSI database検証も同じruntime file群のFile table収録を要求する。したがって利用者へ.NET 10 Desktop Runtimeの別途導入を要求しない。開発用`bin/Release`はこの保証対象ではない。

強化後の`0.1.0-rc.2`を再生成し、payload 495 files、manifest entries 494、PDB 0、ZIP SHA-256 `354c7132e762c9667d61abed05fa42127aaa2766b5a787494c356b2743a3d9ce`、checksum一致、一時build artifacts 0件を確認した。存在しない`DOTNET_ROOT`／`DOTNET_ROOT_X64`とmultilevel lookup無効の環境で、TaskbarHost helpはexit 0、Observer／BluetoothWorkerの不正引数は製品定義どおりexit 2、runtime-missing eventは0件だった。外部`framework`／`frameworks`参照へ差し替えたpayloadと`hostfxr.dll`欠落payloadは、どちらも意図した理由でMSI生成前に拒否された。

同じpayloadから生成したMSIは63,897,600 bytes、SHA-256 `049d8bbbae8d82aa9f7f5e279776362103957347d23ccf2628862ef12a698f4`、WiX警告0／error 0、File table 495件、per-user／x64／昇格不要、署名status `NotSigned`である。package検証は4 executable、4 runtime config、`hostfxr.dll`、`hostpolicy.dll`、`coreclr.dll`、`PresentationFramework.dll`の収録を確認した。これは未署名RCの構造検証であり、実証明書Gateやclean user lifecycle Gateの代替ではない。

固定per-user file packageに対するWiX公式既知制約のためICE64／ICE91だけを抑止し、他のICE検証は有効である。

## 2026-08-07 Phase 5C統合後の再検証

Phase 5C PR #54をSquash commit `cc6b99b`としてPhase 6Bへ統合し、同一commitからCI相当のローカル検証と配布物生成を再実行した。

- locked solution／WiX restore：成功
- NuGet vulnerability audit：20 projects、vulnerable entry 0
- format verification：差分なし
- Release `-warnaserror` build：警告0、error 0
- 全回帰：380／380 Pass
  - Smoke 1
  - Core Audio 44
  - Default Endpoint Policy 7
  - Foundation 80
  - Taskbar Host 183
  - Bluetooth KS 65
- self-contained RC ZIP：`QuickPods-0.1.0-ci.62001-win-x64.zip`
  - payload 495 files、manifest entries 494
  - SHA-256：`6e3b4ed5036b6db85494b5d0d77dd190ddbb4438ee00659b627e9fb144c9312b`
  - `selfContained: true`、`signed: false`
- per-user MSI：`QuickPods-0.1.0-ci.62001-win-x64.msi`
  - 63,905,792 bytes
  - SHA-256：`e8338c254a37d9453192da6b52ca7d1ffc7ef7a05f3581487abd36ed4291a4f8`
  - x64、昇格不要、`NotSigned`

GitHub Actions run 31118567631もrestore、toolchain、audit、format、build、test、RC／MSI生成、artifact uploadまで成功した。後続の文書同期commitに対するCIはGitHub Actionsのpartial outageによりrunner割当待ちであり、コード失敗とは判定しない。

## 2026-08-06時点で未実施だったGate

Phase 6B実装をPR #51（Main `645b3a8`）へ統合した時点では、clean local-console環境でのinstall／launch／update／uninstall／startup残骸確認をまだ実施していなかった。`build/Test-InstallerLifecycle.ps1`は既存user stateを拒否するため、通常の開発profileを代用せず、後述のdisposable標準ユーザーGateまで保留した。この段落は当時の判断履歴であり、現在の未完了一覧ではない。

実Bluetooth catalog [#38](https://github.com/rimtty/QuickPods/issues/38)、物理接続／切断／既定出力 [#41](https://github.com/rimtty/QuickPods/issues/41)、local-console session／通常theme／real-login [#43](https://github.com/rimtty/QuickPods/issues/43)、Explorer回復／generation resource [#18](https://github.com/rimtty/QuickPods/issues/18)／[#34](https://github.com/rimtty/QuickPods/issues/34)は後続Gateで完了した。High Contrast追加追試はオーナー判断で今回の最終Gateから除外した。

## 2026-08-08 clean standard-user MSI lifecycle

Main `d6cb13a2def45300262ef39136b3dd3ef0102ad8`から、同じx64／per-user／`requiresElevation=false`／495-file構造を持つ二つの未署名MSIを生成した。WiX warning／errorは両方0件だった。

| 役割 | artifact／MSI version | ProductCode | SHA-256 |
|---|---|---|---|
| previous | `0.1.0-ci.4301`／`0.0.4301` | `379C6F15-9BD6-60FB-3888-E087CC4EEE38` | `a4ea4925094e29c15b4d1da8c000ad54579ccbe107c063b277adc2efd070de92` |
| current | `0.1.0-ci.4302`／`0.0.4302` | `37881A31-07AE-E0F7-D0E0-9DC2BDBAE63F` | `6a88c454f6ddc3aa9264cccccb49e6c257eec800db5023c3dbe490163fa00203` |

versionは昇順でProductCodeは異なる。current MSIの生成済みFile tableとpayload manifestには`ThirdPartyNotices.txt`が含まれ、その本文にCeiling参照とMIT全文がある。したがってAC-027の法務同梱は、署名状態とは独立して実際の受入MSIで成立する。

検証専用のdisposable local標準ユーザーを作成し、Administrators非所属、interactive local `console`、Explorer 1件でrunnerを実行した。開始16:58:28 UTC、完了16:58:51 UTCで、結果は次のすべてがtrueだった。

- previous install／launch
- previous常駐中のcurrent major upgrade／current launch
- current常駐中のuninstall
- QuickPods user data保持
- unrelated HKCU Run value保持
- `failedStage=null`、`failureType=null`

runner自体も`StandardUser=true`、`UserInteractive=true`、`Status=pass`を記録した。install／upgrade／uninstallのverbose MSI logを保存した。試験後は切断状態で残った対象sessionだけをlogoffし、テストユーザー、profile、scheduled task、一時passwordを削除した。ユーザー、session、profile directory、password fileはいずれも残っておらず、証跡だけをignored `artifacts/gates/issue-50/lifecycle-output/`へ保持する。

以上により、clean standard-user lifecycleとAC-022を合格とする。code-signing certificate、timestamp付きAuthenticode、署名済みartifactは2026-08-08のオーナー判断で今回の完了目標から除外した。署名toolingのfail-closed契約は維持するが、`NotSigned`を`Valid`と読み替えない。

## 2026-08-07 cross-host handoff再現性確認

Phase 5C、local-console証拠、retained Start hardeningを統合したPhase 6B headで、別PCへ引き継ぐ直前のCI相当検証を再実行した。

- format verification：差分なし
- Release `-warnaserror` build：警告0、error 0
- 全回帰：398／398 Pass
- cross-process KS gate：10回反復、10／10 Pass
- self-contained RC：`0.1.0-handoff.1`、manifest entries 494、ZIP 83,592,565 bytes
- per-user MSI：`0.1.0-handoff.1`、63,913,984 bytes、x64、昇格不要、`NotSigned`
- MSI SHA-256：`8265b298b37c7b23c8b686ec991b663c1f43abf759a2c5110d313176e0573ba1`

PR #51の先行CI run 31174241336では、固定名KS gate ownerの3秒holdが高負荷runner上でcontender開始前に終了し、1件だけ失敗した。製品動作ではなくテスト同期の競合である。ownerをテストtimeoutより十分長く保持し、contender判定後にcleanupするよう変更した。上記10回反復と全398件で再現しないことを確認した。生成物は`artifacts/`配下のignored検証物であり、repositoryへは含めない。
