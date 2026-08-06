# Phase 6B — 配布準備

## Format-independent package contract

全shipped executableは`Directory.Build.props`の`0.1.0` product／assembly／file metadataを共有し、RCの`-p:Version`だけがpre-release product versionを上書きする。package scriptは本体、TaskbarHost、TaskbarObserver、BluetoothWorkerのProductVersionが要求versionで始まることを検証し、各値をmanifestへ記録する。source revisionはSDKのInformationalVersion生成へ含める。

packageにはQuickPodsの`ThirdPartyNotices.txt`、固定SDKが提供する.NET license、.NET third-party noticesを必須fileとして同梱する。Ceilingについては、参照commitとMIT全文を保守的に収録する。legal fileもper-file SHA-256 manifestとdeterministic ZIPの対象である。

## Update and uninstall boundary

初期版はnetwork accessと自動更新を持たない。承認済みartifactを手動で置換し、user settings／logsは保持する。uninstaller向けの`QuickPods.exe --unregister-startup`はUI、single-instance、audio、Bluetooth、TaskbarHostを初期化せず、QuickPods所有のHKCU Run値を削除する。設定fileが既に存在する場合だけ`StartWithWindows=false`を保存し、存在しないuser dataを新規作成しない。

## Open distribution decision

MSI、MSIX、portable ZIP／per-user installerの最終選択とcode-signing証明書は計画書の未決事項である。外部installer toolchainまたはscript依存を承認なしに追加しない。このbranchはformat-independent assetsを準備できるが、clean-machine install／update／uninstall acceptanceと最終mergeは形式決定およびGate D #48の後に行う。

## Remaining gates

- installer形式と署名方針のuser decision
- clean environmentで通常userのinstall／launch／update／uninstall／auto-start残骸0件
- #38、#41、#43、#48
