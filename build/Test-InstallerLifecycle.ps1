[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PreviousInstaller,

    [Parameter(Mandatory = $true)]
    [string]$CurrentInstaller,

    [string]$OutputDirectory = "artifacts/msi-lifecycle",

    [switch]$ConfirmDisposableEnvironment
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 3.0
$stage = "preflight"
$passed = $false
$failureType = $null
$sentinelValueName = "QuickPodsLifecycleSentinel"
$runKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$processNames = @(
    "QuickPods",
    "QuickPods.TaskbarHost",
    "QuickPods.TaskbarObserver",
    "QuickPods.BluetoothWorker"
)

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-InstallerDescriptor {
    param([Parameter(Mandatory = $true)][string]$InstallerPath)

    $resolvedInstaller = (Resolve-Path -LiteralPath $InstallerPath).Path
    $manifestPath = Join-Path (Split-Path -Parent $resolvedInstaller) "installer-manifest.json"
    Assert-Condition `
        -Condition (Test-Path -LiteralPath $manifestPath -PathType Leaf) `
        -Message "installer-manifest.json must be next to each MSI."

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $actualHash = (Get-FileHash -LiteralPath $resolvedInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Condition `
        -Condition ($manifest.product -eq "QuickPods") `
        -Message "The installer manifest is not for QuickPods."
    Assert-Condition `
        -Condition ($manifest.scope -eq "perUser" -and -not [bool]$manifest.requiresElevation) `
        -Message "The lifecycle gate requires a non-elevated per-user MSI."
    Assert-Condition `
        -Condition ($actualHash -eq ([string]$manifest.sha256).ToLowerInvariant()) `
        -Message "The MSI checksum does not match installer-manifest.json."
    Assert-Condition `
        -Condition ([string]$manifest.productCode -match '^[0-9A-Fa-f-]{36}$') `
        -Message "The installer manifest has an invalid ProductCode."

    return [pscustomobject]@{
        Path = $resolvedInstaller
        ArtifactVersion = [string]$manifest.artifactVersion
        InstallerVersion = [Version]([string]$manifest.windowsInstallerVersion)
        ProductCode = "{$(([string]$manifest.productCode).ToUpperInvariant())}"
    }
}

function Get-ProductState {
    param([Parameter(Mandatory = $true)][string]$ProductCode)

    return [QuickPodsInstallerLifecycle.NativeMethods]::MsiQueryProductState($ProductCode)
}

function Get-QuickPodsProcesses {
    return @(
        Get-Process -Name $processNames -ErrorAction SilentlyContinue
    )
}

function Wait-QuickPodsExit {
    param([int]$TimeoutSeconds = 15)

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $remaining = @(Get-QuickPodsProcesses)
        if ($remaining.Count -eq 0) {
            return
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "QuickPods processes did not exit within the lifecycle timeout."
}

function Invoke-MsiExecution {
    param(
        [Parameter(Mandatory = $true)][ValidateSet("Install", "Uninstall")][string]$Operation,
        [Parameter(Mandatory = $true)][string]$Target,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    $operationArgument = if ($Operation -eq "Install") { "/i" } else { "/x" }
    $arguments = @(
        $operationArgument,
        ('"{0}"' -f $Target),
        "/qn",
        "/norestart",
        "/l*v",
        ('"{0}"' -f $LogPath)
    )
    $process = Start-Process -FilePath "msiexec.exe" -ArgumentList $arguments -Wait -PassThru
    Assert-Condition `
        -Condition ($process.ExitCode -eq 0) `
        -Message "Windows Installer returned a non-success exit code during $Operation."
}

function Stop-LifecycleProcesses {
    foreach ($process in @(Get-QuickPodsProcesses)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
}

if (-not $ConfirmDisposableEnvironment) {
    throw "Run only in a disposable clean standard-user environment and pass -ConfirmDisposableEnvironment."
}
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw "The MSI lifecycle gate requires Windows."
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$isElevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Assert-Condition `
    -Condition (-not $isElevated) `
    -Message "The MSI lifecycle gate must run from a non-elevated standard-user process."

if (-not ("QuickPodsInstallerLifecycle.NativeMethods" -as [type])) {
    Add-Type -TypeDefinition @"
using System.Runtime.InteropServices;

namespace QuickPodsInstallerLifecycle
{
    public static class NativeMethods
    {
        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        public static extern int MsiQueryProductState(string productCode);
    }
}
"@
}

$previous = Get-InstallerDescriptor -InstallerPath $PreviousInstaller
$current = Get-InstallerDescriptor -InstallerPath $CurrentInstaller
Assert-Condition `
    -Condition ($previous.ProductCode -ne $current.ProductCode) `
    -Message "Previous and current MSI files must have different ProductCodes."
Assert-Condition `
    -Condition ($previous.InstallerVersion -lt $current.InstallerVersion) `
    -Message "Current MSI version must be newer than previous MSI version."

$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
} else {
    [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputDirectory))
}
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$resultPath = Join-Path $outputRoot "result.json"
$installFolder = Join-Path $env:LOCALAPPDATA "Programs\QuickPods"
$installedExecutable = Join-Path $installFolder "QuickPods.exe"
$installedManifest = Join-Path $installFolder "artifact-manifest.json"
$settingsDirectory = Join-Path $env:LOCALAPPDATA "QuickPods"
$settingsPath = Join-Path $settingsDirectory "settings.json"
$retainedLogPath = Join-Path $settingsDirectory "logs\lifecycle-retention-sentinel.txt"
$shortcutPath = Join-Path ([Environment]::GetFolderPath("Programs")) "QuickPods\QuickPods.lnk"

Assert-Condition `
    -Condition ((Get-ProductState $previous.ProductCode) -ne 5) `
    -Message "The previous QuickPods product is already installed."
Assert-Condition `
    -Condition ((Get-ProductState $current.ProductCode) -ne 5) `
    -Message "The current QuickPods product is already installed."
Assert-Condition `
    -Condition (-not (Test-Path -LiteralPath $installFolder)) `
    -Message "The QuickPods install folder already exists."
Assert-Condition `
    -Condition (-not (Test-Path -LiteralPath $settingsDirectory)) `
    -Message "QuickPods user data already exists; use a clean disposable account."
Assert-Condition `
    -Condition (-not (Get-ItemProperty -LiteralPath $runKeyPath -Name "QuickPods" -ErrorAction SilentlyContinue)) `
    -Message "The QuickPods startup value already exists."
Assert-Condition `
    -Condition (@(Get-QuickPodsProcesses).Count -eq 0) `
    -Message "QuickPods processes are already running."

$checks = [ordered]@{}
try {
    $stage = "install-previous"
    Invoke-MsiExecution `
        -Operation Install `
        -Target $previous.Path `
        -LogPath (Join-Path $outputRoot "install-previous.log")
    Assert-Condition -Condition ((Get-ProductState $previous.ProductCode) -eq 5) -Message "Previous MSI did not register."
    Assert-Condition -Condition (Test-Path -LiteralPath $installedExecutable -PathType Leaf) -Message "QuickPods.exe was not installed."
    Assert-Condition -Condition (Test-Path -LiteralPath $shortcutPath -PathType Leaf) -Message "Start menu shortcut was not installed."
    $previousPayloadManifest = Get-Content -LiteralPath $installedManifest -Raw | ConvertFrom-Json
    Assert-Condition -Condition ($previousPayloadManifest.version -eq $previous.ArtifactVersion) -Message "Previous payload version is incorrect."
    $checks.previousInstall = $true

    $stage = "seed-user-state"
    New-Item -ItemType Directory -Path (Split-Path -Parent $retainedLogPath) -Force | Out-Null
    "retain" | Set-Content -LiteralPath $retainedLogPath -Encoding ascii
    $settings = [ordered]@{
        SelectedDevice = $null
        StartWithWindows = $true
        PreferNativeTaskbarSurface = $true
        DisplayMode = 0
        SetConnectedDeviceAsDefault = $true
        MouseWheelStepPercent = 2
        Theme = 0
        ConfirmBluetoothDisconnect = $false
        SchemaVersion = 1
    }
    $settings | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding utf8
    New-Item -Path $runKeyPath -Force | Out-Null
    $startupCommand = '"{0}" --background' -f $installedExecutable
    New-ItemProperty `
        -LiteralPath $runKeyPath `
        -Name "QuickPods" `
        -Value $startupCommand `
        -PropertyType String `
        -Force | Out-Null
    New-ItemProperty `
        -LiteralPath $runKeyPath `
        -Name $sentinelValueName `
        -Value "keep" `
        -PropertyType String `
        -Force | Out-Null

    $stage = "launch-previous"
    $previousProcess = Start-Process -FilePath $installedExecutable -ArgumentList "--background" -PassThru
    Start-Sleep -Seconds 3
    Assert-Condition -Condition (-not $previousProcess.HasExited) -Message "Previous QuickPods process did not remain resident."

    $stage = "upgrade-running-product"
    Invoke-MsiExecution `
        -Operation Install `
        -Target $current.Path `
        -LogPath (Join-Path $outputRoot "upgrade-current.log")
    Wait-QuickPodsExit
    Assert-Condition -Condition ((Get-ProductState $previous.ProductCode) -ne 5) -Message "Previous MSI remained registered after upgrade."
    Assert-Condition -Condition ((Get-ProductState $current.ProductCode) -eq 5) -Message "Current MSI did not register after upgrade."
    $currentPayloadManifest = Get-Content -LiteralPath $installedManifest -Raw | ConvertFrom-Json
    Assert-Condition -Condition ($currentPayloadManifest.version -eq $current.ArtifactVersion) -Message "Current payload version is incorrect."
    Assert-Condition -Condition ((Get-ItemPropertyValue -LiteralPath $runKeyPath -Name "QuickPods") -eq $startupCommand) -Message "Major Upgrade did not preserve startup registration."
    $upgradedSettings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    Assert-Condition -Condition ([bool]$upgradedSettings.StartWithWindows) -Message "Major Upgrade did not preserve startup intent."
    $checks.runningUpgrade = $true

    $stage = "launch-current"
    $currentProcess = Start-Process -FilePath $installedExecutable -ArgumentList "--background" -PassThru
    Start-Sleep -Seconds 3
    Assert-Condition -Condition (-not $currentProcess.HasExited) -Message "Current QuickPods process did not remain resident."

    $stage = "uninstall-running-product"
    Invoke-MsiExecution `
        -Operation Uninstall `
        -Target $current.ProductCode `
        -LogPath (Join-Path $outputRoot "uninstall-current.log")
    Wait-QuickPodsExit
    Assert-Condition -Condition ((Get-ProductState $current.ProductCode) -ne 5) -Message "Current MSI remained registered after uninstall."
    Assert-Condition -Condition (-not (Test-Path -LiteralPath $installFolder)) -Message "The MSI install folder remained after uninstall."
    Assert-Condition -Condition (-not (Test-Path -LiteralPath $shortcutPath)) -Message "The Start menu shortcut remained after uninstall."
    Assert-Condition -Condition (-not (Get-ItemProperty -LiteralPath $runKeyPath -Name "QuickPods" -ErrorAction SilentlyContinue)) -Message "QuickPods startup registration remained after uninstall."
    Assert-Condition -Condition ((Get-ItemPropertyValue -LiteralPath $runKeyPath -Name $sentinelValueName) -eq "keep") -Message "Uninstall modified an unrelated Run value."
    Assert-Condition -Condition (Test-Path -LiteralPath $retainedLogPath -PathType Leaf) -Message "Uninstall removed retained user logs."
    $uninstalledSettings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    Assert-Condition -Condition (-not [bool]$uninstalledSettings.StartWithWindows) -Message "Uninstall did not clear persisted startup intent."
    $checks.runningUninstall = $true
    $checks.userDataRetained = $true
    $checks.unrelatedRunValuePreserved = $true
    $passed = $true
} catch {
    $failureType = $_.Exception.GetType().Name
    throw
} finally {
    if (-not $passed) {
        Stop-LifecycleProcesses
        foreach ($descriptor in @($current, $previous)) {
            if ((Get-ProductState $descriptor.ProductCode) -eq 5) {
                try {
                    Invoke-MsiExecution `
                        -Operation Uninstall `
                        -Target $descriptor.ProductCode `
                        -LogPath (Join-Path $outputRoot "cleanup-$($descriptor.ArtifactVersion).log")
                } catch {
                    # The disposable environment remains the recovery boundary.
                }
            }
        }
    }

    Remove-ItemProperty `
        -LiteralPath $runKeyPath `
        -Name $sentinelValueName `
        -ErrorAction SilentlyContinue

    $result = [ordered]@{
        status = if ($passed) { "pass" } else { "fail" }
        failedStage = if ($passed) { $null } else { $stage }
        failureType = if ($passed) { $null } else { $failureType }
        previousArtifactVersion = $previous.ArtifactVersion
        currentArtifactVersion = $current.ArtifactVersion
        standardUser = -not $isElevated
        checks = $checks
    }
    $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $resultPath -Encoding utf8
}

[pscustomobject]@{
    ResultPath = $resultPath
    PreviousArtifactVersion = $previous.ArtifactVersion
    CurrentArtifactVersion = $current.ArtifactVersion
    Status = "pass"
}
