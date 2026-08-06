# QuickPods

QuickPods is a Windows 11 desktop application for controlling the default output volume and selecting, connecting, disconnecting, and making a paired Bluetooth audio device the default output from the taskbar.

Phases 0–6A are integrated. Phase 6B uses WiX 6.0.2 to produce an elevation-free per-user x64 MSI, alongside the diagnostic portable ZIP, centralized product versions, legal notices, dependency auditing, and offline update/uninstall contracts. RC installers remain explicitly unsigned until the planned signing certificate is available, and Phase 6B cannot merge before Gate D. Physical Bluetooth acceptance remains deferred to Issues #38 and #41; RDP cannot close the local display gate in Issue #43 or the 24-hour Gate D in Issue #48. See the [Phase 6B architecture](docs/architecture/phase-6b-distribution.md), [installer guide](installer/README.md), [update policy](docs/release/update-policy.md), [uninstall policy](docs/release/uninstall-policy.md), and [Phase 6B validation](docs/validation/phase-6b/test-results.md).

## Requirements

- Windows 11 x64
- .NET SDK 10.0.302 or a compatible patch in the same feature band
- WiX Toolset 6.0.2 is restored by the installer project; distributors must comply with its current OSMF terms

## Bootstrap validation

Run these commands from the repository root:

```powershell
dotnet restore QuickPods.sln
dotnet restore QuickPods.sln --locked-mode
dotnet format QuickPods.sln --verify-no-changes --no-restore --severity warn
dotnet build QuickPods.sln -c Release --no-restore -warnaserror
dotnet test QuickPods.sln -c Release --no-build --no-restore --logger "trx;LogFilePrefix=quickpods" --results-directory TestResults -- RunConfiguration.TreatNoTestsAsError=true
./build/Publish-Installer.ps1 -Version 0.1.0-rc.1
```

QuickPods supports x64 only. The solution's `Any CPU` configuration is retained as a .NET CLI and Visual Studio compatibility alias; `Directory.Build.props` always selects the x64 target.

## Repository layout

```text
docs/     Product plans, validation evidence, mockups, and branding
spikes/   Isolated technical feasibility projects
src/      Production projects
tests/    Automated test projects
```

See [the implementation plan](docs/QuickPods_実装計画書.md), [the branch roadmap](docs/QuickPods_ブランチ別実装ロードマップ.md), [the v2 UI baseline](docs/QuickPods%20UI%20Mockup%20v2.md), and [the Bluetooth selector specification](docs/QuickPods_Bluetoothオーディオ選択_機能仕様.md) for scope and quality gates.
