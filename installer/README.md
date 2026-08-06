# QuickPods per-user MSI

`QuickPods.Setup`はWiX Toolset 6.0.2で、QuickPodsのself-contained `win-x64` payloadを昇格不要のユーザー単位MSIへ変換する。インストール先は`PerUserProgramFilesFolder\QuickPods`、ショートカットは現在のユーザーのスタートメニューである。

リポジトリrootから次を実行する。

```powershell
./build/Publish-Installer.ps1 -Version 0.1.0-rc.1
```

生成物は既定で`artifacts/installer`へ出力され、MSI、SHA-256、installer manifest、payload manifestを含む。MSI内部は`Test-InstallerPackage.ps1`により、per-user scope、必須file、upgrade identity、スタートメニューshortcut、更新／uninstall時のprocess終了policy、完全uninstall時のstartup解除commandを検証する。pre-releaseは正式版より低く、各RC／CI buildで単調増加できるWindows Installer versionへ写像する。ProductCodeは完全なartifact versionから決定的に生成するため、同一入力の再buildでpackage identityが変化しない。

WiX v6はbuild-time toolchainであり、QuickPods runtimeへ同梱しない。WiX v6にはOpen Source Maintenance Feeの条件が適用されるため、配布主体は利用時点の[公式条件](https://docs.firegiant.com/wix/osmf/)への適合をrelease前に確認する。WiX v7への更新ではEULAの明示承諾が必要なため、自動更新しない。

RC MSIはコード署名証明書の準備まで未署名である。証明書や秘密鍵をrepositoryへ保存してはならず、正式releaseでは署名後に`Test-InstallerPackage.ps1 -RequireSignature`を通す。
