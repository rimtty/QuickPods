# Development environment and reproducibility

## Prerequisites

- Windows 11 x64
- Git for Windows and access to the private `rimtty/QuickPods` repository
- GitHub CLI authenticated as an authorized account
- .NET SDK `10.0.302`, or a compatible patch in the same feature band selected by `global.json`
- PowerShell on Windows; PowerShell 7 is recommended for direct script use
- Visual Studio is optional; the supported baseline is the repository's `dotnet` and PowerShell commands

WiX must not be installed globally. `installer/QuickPods.Setup/QuickPods.Setup.wixproj` restores WiX Toolset 6.0.2 and its Util extension through locked NuGet dependencies.

## Clone the private repository

Use a short local path to keep Windows tooling paths readable.

```powershell
Set-Location C:\src
gh auth status
gh repo clone rimtty/QuickPods
Set-Location .\QuickPods
git switch main
git pull --ff-only origin main
```

SSH is also valid when the new PC has its own authorized key:

```powershell
git clone git@github.com:rimtty/QuickPods.git C:\src\QuickPods
```

Do not copy another PC's `%USERPROFILE%\.ssh` directory or reuse its private key. Configure the new host independently.

## Verify the toolchain

```powershell
dotnet --version
dotnet --info
git status --short --branch
```

Expected SDK feature band: `10.0.3xx`. The working tree should be clean and track `origin/main`.

## CI-equivalent validation

Run from the repository root:

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_NOLOGO='1'

dotnet restore QuickPods.sln --locked-mode
dotnet restore installer/QuickPods.Setup/QuickPods.Setup.wixproj --locked-mode
./build/Test-DependencyVulnerabilities.ps1
dotnet format QuickPods.sln --verify-no-changes --no-restore --severity warn --verbosity minimal
dotnet build QuickPods.sln -c Release --no-restore -p:ContinuousIntegrationBuild=true
dotnet test QuickPods.sln -c Release --no-build --no-restore --logger "trx;LogFilePrefix=quickpods" --results-directory TestResults --collect:"XPlat Code Coverage" -- RunConfiguration.TreatNoTestsAsError=true
```

Expected test totals:

| Project | Tests |
|---|---:|
| Smoke | 1 |
| Default Endpoint Policy | 7 |
| Core Audio | 44 |
| Foundation | 115 |
| Taskbar Host | 183 |
| Bluetooth KS | 65 |
| Total | 415 |

## Build a self-contained RC and MSI

Generated output is intentionally ignored by Git.

```powershell
./build/Publish-ReleaseCandidate.ps1 -OutputDirectory artifacts/rc -Version 0.1.0-dev.1
./build/Publish-Installer.ps1 -OutputDirectory artifacts/installer -Version 0.1.0-dev.1 -PayloadDirectory artifacts/rc/QuickPods -SkipPayloadPublish -SkipRestore
```

Important output:

```text
artifacts/rc/QuickPods/                         self-contained application
artifacts/rc/QuickPods-*-win-x64.zip           portable diagnostic package
artifacts/rc/SHA256SUMS.txt                     RC hashes
artifacts/rc/QuickPods/artifact-manifest.json   payload identity
artifacts/installer/QuickPods-*-win-x64.msi     per-user MSI
artifacts/installer/installer-manifest.json     scope/version/signature identity
artifacts/installer/SHA256SUMS.txt              MSI hashes
```

The end-user package contains the .NET runtime. A runtime-install prompt means the wrong, framework-dependent output was launched.

## Run the candidate

```powershell
Start-Process .\artifacts\rc\QuickPods\QuickPods.exe -ArgumentList '--background'
```

Expected resident tree:

- `QuickPods.exe` — product state, tray, WPF flyout/settings, Core Audio, Bluetooth coordination
- `QuickPods.TaskbarHost.exe` — native taskbar surface and pointer/keyboard input
- `QuickPods.TaskbarObserver.exe` — short-lived Explorer/UI Automation generation observer
- `QuickPods.BluetoothWorker.exe` — only during bounded Bluetooth capability/mutation work

Runtime state:

```text
%LocalAppData%\QuickPods\settings.json
%LocalAppData%\QuickPods\logs\quickpods-YYYYMMDD.jsonl
HKCU\Software\Microsoft\Windows\CurrentVersion\Run\QuickPods
```

The Run value, when enabled, must be exactly a quoted path to the current `QuickPods.exe` followed by `--background`. Use the tray menu's explicit `終了` to test complete process cleanup.

## Session safety

| Operation | RDP | Local console |
|---|---|---|
| restore/build/test/package/static MSI inspection | Allowed | Allowed |
| settings JSON and HKCU Run objective inspection | Allowed | Allowed |
| log folder and sanitized diagnostic copy | Allowed | Allowed |
| local Bluetooth catalog or mutation acceptance | Not valid; product must fail safe | Required |
| taskbar DPI/geometry/theme/high contrast acceptance | Not valid | Required |
| real Explorer restart recovery evidence | Do not use for final Gate | Required |
| login/reboot auto-start acceptance | Do not use for final Gate | Required |
| clean standard-user MSI lifecycle | Do not substitute an active development profile | Disposable local host required |

`build/Test-InstallerLifecycle.ps1` changes installed-product state and requires both previous and current MSI files plus `-ConfirmDisposableEnvironment`. Never run it on a profile containing development state you need to preserve.

## Signing boundary

Normal CI is unsigned and contains no certificate secret. The protected `.github/workflows/release.yml` path is the only formal signing workflow. Keep the PFX outside the repository, pass its password as a secure secret, require Authenticode `Valid`, and verify the timestamped final MSI with `-RequireSignature`.
