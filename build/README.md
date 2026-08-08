# Build scripts

PowerShell scripts in this directory provide reproducible repository checks, packaging, installer validation, and focused Windows lifecycle tests.

## Common commands

| Script | Purpose |
|---|---|
| `Test-DependencyVulnerabilities.ps1` | Fail when restored NuGet dependencies have known vulnerabilities |
| `Test-RepositoryPublicReadiness.ps1` | Validate public documentation links and reject tracked private/generated material |
| `Publish-ReleaseCandidate.ps1` | Build a self-contained x64 payload, manifest, checksum, and portable ZIP |
| `Publish-Installer.ps1` | Build and optionally sign the per-user MSI |
| `Test-InstallerPackage.ps1` | Inspect MSI scope, identity, files, shortcuts, and signature requirements |
| `Test-InstallerLifecycle.ps1` | Exercise install, major upgrade, and uninstall in a disposable standard-user environment |
| `Test-ExplorerRecovery.ps1` | Exercise the taskbar helper boundary across Explorer recovery |
| `Measure-QuickPodsResources.ps1` | Capture bounded process resource samples |
| `Summarize-QuickPodsResources.ps1` | Summarize sanitized resource observations |

Scripts that mutate installation, startup, Bluetooth, audio, or Explorer state require explicit parameters and safety checks. Read their help and source before running them. Generated outputs belong under ignored `artifacts/` or `TestResults/` directories and must not be committed.
