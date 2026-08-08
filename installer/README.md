# QuickPods per-user MSI

`QuickPods.Setup` uses WiX Toolset 6 to package the self-contained `win-x64` payload as a per-user MSI.

## Package behavior

- Installs without elevation under the current user's Programs directory.
- Creates a current-user Start menu shortcut.
- Closes a running QuickPods instance during upgrade or uninstall.
- Removes the QuickPods-owned sign-in startup value during a complete uninstall.
- Preserves `%LocalAppData%\QuickPods` settings and logs.
- Includes QuickPods and third-party legal notices in the payload.

End users do not need to install .NET separately.

## Build an unsigned test installer

From the repository root:

```powershell
dotnet restore QuickPods.sln --locked-mode
dotnet restore installer/QuickPods.Setup/QuickPods.Setup.wixproj --locked-mode
./build/Publish-Installer.ps1 -Version 0.1.0-rc.1
```

Outputs are written to `artifacts/installer` by default:

- `QuickPods-<version>-win-x64.msi`
- `SHA256SUMS.txt`
- `installer-manifest.json`
- `payload-artifact-manifest.json`

The build validates the self-contained runtime, installer scope, required files, deterministic package identity, Start menu shortcut, upgrade policy, and uninstall cleanup contract.

## Signing

Do not publish an unsigned MSI as a stable release. Keep the PFX outside the repository and pass the password as a `SecureString`:

```powershell
$password = Read-Host 'PFX password' -AsSecureString
./build/Publish-Installer.ps1 `
  -Version 0.1.0 `
  -SigningCertificatePath C:\secure\quickpods-signing.pfx `
  -SigningCertificatePassword $password `
  -RequireSignature
```

The GitHub `Signed release` workflow uses protected environment secrets and removes its temporary certificate file even when the job fails. See the [release guide](../docs/release/README.md).

## Lifecycle validation

Run lifecycle tests only in a disposable Windows 11 VM or test account where QuickPods has never stored real user data:

```powershell
./build/Publish-Installer.ps1 -OutputDirectory artifacts/msi-lifecycle/previous -Version 0.1.0-ci.1
./build/Publish-Installer.ps1 -OutputDirectory artifacts/msi-lifecycle/current -Version 0.1.0-rc.1
./build/Test-InstallerLifecycle.ps1 `
  -PreviousInstaller artifacts/msi-lifecycle/previous/QuickPods-0.1.0-ci.1-win-x64.msi `
  -CurrentInstaller artifacts/msi-lifecycle/current/QuickPods-0.1.0-rc.1-win-x64.msi `
  -OutputDirectory artifacts/msi-lifecycle/results `
  -ConfirmDisposableEnvironment
```

The verifier refuses to start when it detects an existing install, QuickPods process, startup registration, or user-data directory. MSI logs may contain local paths and account details; do not commit them.

## WiX terms

WiX is a build-time tool and is not shipped in the QuickPods runtime. Maintainers and distributors are responsible for reviewing the current WiX licensing and maintenance terms before producing release installers.
