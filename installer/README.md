# QuickPods per-user MSI

`QuickPods.Setup`はWiX Toolset 6.0.2で、QuickPodsのself-contained `win-x64` payloadを昇格不要のユーザー単位MSIへ変換する。インストール先は`PerUserProgramFilesFolder\QuickPods`、ショートカットは現在のユーザーのスタートメニューである。

リポジトリrootから次を実行する。

```powershell
./build/Publish-Installer.ps1 -Version 0.1.0-rc.1
```

生成物は既定で`artifacts/installer`へ出力され、MSI、SHA-256、installer manifest、payload manifestを含む。MSI内部は`Test-InstallerPackage.ps1`により、per-user scope、必須file、upgrade identity、スタートメニューshortcut、更新／uninstall時のprocess終了policy、完全uninstall時のstartup解除commandを検証する。pre-releaseは正式版より低く、各RC／CI buildで単調増加できるWindows Installer versionへ写像する。ProductCodeは完全なartifact versionから決定的に生成するため、同一入力の再buildでpackage identityが変化しない。

WiX v6はbuild-time toolchainであり、QuickPods runtimeへ同梱しない。WiX v6にはOpen Source Maintenance Feeの条件が適用されるため、配布主体は利用時点の[公式条件](https://docs.firegiant.com/wix/osmf/)への適合をrelease前に確認する。WiX v7への更新ではEULAの明示承諾が必要なため、自動更新しない。

RC MSIはコード署名証明書の準備まで未署名である。証明書や秘密鍵をrepositoryへ保存してはならず、正式releaseでは署名後に`Test-InstallerPackage.ps1 -RequireSignature`を通す。

## Clean lifecycle gate

通常利用中のWindows accountでは実行しない。QuickPodsを一度も使用していない、非昇格の標準userを持つ使い捨てWindows 11 VM／test accountで、旧版と新版を別directoryへ生成してから次を実行する。

```powershell
./build/Publish-Installer.ps1 -OutputDirectory artifacts/msi-lifecycle/previous -Version 0.1.0-ci.1
./build/Publish-Installer.ps1 -OutputDirectory artifacts/msi-lifecycle/current -Version 0.1.0-rc.1
./build/Test-InstallerLifecycle.ps1 `
  -PreviousInstaller artifacts/msi-lifecycle/previous/QuickPods-0.1.0-ci.1-win-x64.msi `
  -CurrentInstaller artifacts/msi-lifecycle/current/QuickPods-0.1.0-rc.1-win-x64.msi `
  -OutputDirectory artifacts/msi-lifecycle/results `
  -ConfirmDisposableEnvironment
```

検証器は既存のinstall、process、startup登録、user dataを検出した場合は変更前に停止する。旧版install、常駐中のMajor Upgrade、startup設定保持、新版常駐中の完全uninstall、process／binary／shortcut／registration残骸0件、startup intent解除、user data保持、無関係なRun値の保持を一つのfocused gateとして確認する。MSI詳細logにはlocal pathやaccount情報が含まれ得るためrepositoryへ保存せず、sanitized `result.json`だけを確認後に試験記録へ転記する。
