# Phase 6B — 配布準備

## Format-independent package contract

全shipped executableは`Directory.Build.props`の`0.1.0` product／assembly／file metadataを共有し、RCの`-p:Version`だけがpre-release product versionを上書きする。package scriptは本体、TaskbarHost、TaskbarObserver、BluetoothWorkerのProductVersionが要求versionで始まることを検証し、各値をmanifestへ記録する。source revisionはSDKのInformationalVersion生成へ含める。

packageにはQuickPodsの`ThirdPartyNotices.txt`、固定SDKが提供する.NET license、.NET third-party noticesを必須fileとして同梱する。Ceilingについては、参照commitとMIT全文を保守的に収録する。legal fileもper-file SHA-256 manifestとdeterministic ZIPの対象である。

## Update and uninstall boundary

初期版はnetwork accessと自動更新を持たない。承認済みartifactを手動で置換し、user settings／logsは保持する。uninstaller向けの`QuickPods.exe --unregister-startup`はUI、single-instance、audio、Bluetooth、TaskbarHostを初期化せず、QuickPods所有のHKCU Run値を削除する。設定fileが既に存在する場合だけ`StartWithWindows=false`を保存し、存在しないuser dataを新規作成しない。

## Per-user MSI decision

2026-08-06に配布形式をWiX Toolset 6.0.2によるx64ユーザー単位MSIへ確定し、toolchain追加のuser承認を得た。MSIは`PerUserProgramFilesFolder\QuickPods`へ通常user権限で導入し、現在のuserのスタートメニューへshortcutを作る。service、scheduled task、machine-wide registry、Program Files書込み、管理者権限は使用しない。

WiXの`Files` harvestingは固定per-user packageでICE64／ICE91を通過しないと公式に明記されているため使用しない。build scriptはpayloadをdirectory単位のComponentへ決定的に展開し、各ComponentへHKCU registry KeyPathを付与する。固定per-user配置そのものを拒否するICE64／ICE91だけを抑止し、それ以外のMSI検証は有効に保つ。

更新／uninstall時はWiX Util extensionで実行中の`QuickPods.exe`へ終了messageを送り、5秒後も残る場合だけprocessを終了してfile lockを解消する。uninstall時はinstalled copyの`QuickPods.exe --unregister-startup`をupgrade以外の完全削除時だけ実行し、その後MSI所有binary、Component marker、shortcut、ARP登録を削除する。user settingsとlogsは保持する。major upgradeではstartup設定を保持し、古いpackageをtransaction内で削除してから新しいpackageを導入する。pre-releaseは正式版より低く、各RC／CI buildで単調増加できるWindows Installer versionへ写像する。ProductCodeは完全なartifact versionから決定的に生成する。

WiX v6のOpen Source Maintenance Fee条件はrelease前に配布主体が確認する。WiX v7はEULAの明示承諾が必要なため自動更新しない。

コード署名証明書は後日用意する方針であり、通常CIのRC artifactはmanifestへ`NotSigned`を明示する。正式release専用workflowは保護された`release-signing` environmentからBase64 PFXとpasswordを受け取り、runner tempへだけ復元する。PowerShell certificate objectでSHA-256 Authenticode署名とHTTP timestampを付与するため、passwordを外部processのcommand lineへ渡さない。成功／失敗後はPFXを削除し、署名後のMSIが`Valid`かつ`-RequireSignature`検証済みになるまでartifactを公開しない。

## Remaining gates

- `build/Test-InstallerLifecycle.ps1`を使うclean standard-user環境でのinstall／launch／running update／running uninstall／auto-start残骸0件
- code-signing証明書の準備、`release-signing` secret登録、署名済み正式artifactのworkflow実行
- #43、#48（物理Bluetooth catalog／operation #38／#41は合格済み）
