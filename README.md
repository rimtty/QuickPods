# QuickPods

QuickPods is a Windows 11 desktop application for controlling the default output volume and selecting, connecting, disconnecting, and making a paired Bluetooth audio device the default output from the taskbar.

Phases 0–5A are integrated. Phase 5B hardens Windows lifecycle recovery, corrupt-settings quarantine, taskbar-host failure budgets, keyboard and screen-reader metadata, and high-contrast rendering. Physical Bluetooth acceptance remains deliberately deferred to Issues #38 and #41, and RDP cannot close the local display gate in Issue #43. See the [Phase 5B architecture](docs/architecture/phase-5b-resilience-accessibility.md) and [Phase 5B validation](docs/validation/phase-5b/test-results.md).

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
