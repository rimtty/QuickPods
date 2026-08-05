# QuickPods

QuickPods is a Windows 11 desktop application for controlling the default output volume and a selected Bluetooth audio device from the taskbar.

The repository is currently in Phase 0. Product behavior is not implemented yet; this branch establishes the reproducible .NET 10 build and test foundation used by the technical spikes.

## Requirements

- Windows 11 x64
- .NET SDK 10.0.302 or a compatible patch in the same feature band

## Bootstrap validation

Run these commands from the repository root:

```powershell
dotnet restore QuickPods.sln
dotnet restore QuickPods.sln --locked-mode
dotnet format QuickPods.sln --verify-no-changes --no-restore --severity warn
dotnet build QuickPods.sln -c Release --no-restore -warnaserror
dotnet test QuickPods.sln -c Release --no-build --no-restore --logger "trx;LogFilePrefix=quickpods" --results-directory TestResults -- RunConfiguration.TreatNoTestsAsError=true
```

QuickPods supports x64 only. The solution's `Any CPU` configuration is retained as a .NET CLI and Visual Studio compatibility alias; `Directory.Build.props` always selects the x64 target.

## Repository layout

```text
docs/     Product plans, validation evidence, mockups, and branding
spikes/   Isolated technical feasibility projects
src/      Production projects
tests/    Automated test projects
```

See [the implementation plan](docs/QuickPods_実装計画書.md) and [the branch roadmap](docs/QuickPods_ブランチ別実装ロードマップ.md) for scope and quality gates.
