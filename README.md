# QuickPods

QuickPods is a Windows 11 desktop application for controlling the default output volume and selecting, connecting, disconnecting, and making a paired Bluetooth audio device the default output from the taskbar.

Phase 0 is complete: Core Audio, per-device Bluetooth control, default-output switching, and the taskbar host are approved with explicit fail-closed fallbacks. Product behavior is not implemented yet; the next branch is Phase 1 solution foundation. See the consolidated [Phase 0 decision](docs/validation/phase-0/decision.md).

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

See [the implementation plan](docs/QuickPods_実装計画書.md), [the branch roadmap](docs/QuickPods_ブランチ別実装ロードマップ.md), [the v2 UI baseline](docs/QuickPods%20UI%20Mockup%20v2.md), and [the Bluetooth selector specification](docs/QuickPods_Bluetoothオーディオ選択_機能仕様.md) for scope and quality gates.
