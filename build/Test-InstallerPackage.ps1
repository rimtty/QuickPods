[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InstallerPath,

    [string]$ExpectedVersion = "0.1.0",

    [string]$ExpectedLanguage = "1033",

    [string]$ExpectedPackageCode,

    [switch]$RequireSignature
)

$ErrorActionPreference = "Stop"
$resolvedInstaller = (Resolve-Path -LiteralPath $InstallerPath).Path

function Get-MsiRows {
    param(
        [Parameter(Mandatory = $true)]$Database,
        [Parameter(Mandatory = $true)][string]$Query,
        [Parameter(Mandatory = $true)][int]$ColumnCount
    )

    $rows = [System.Collections.Generic.List[object[]]]::new()
    $view = $Database.GetType().InvokeMember(
        "OpenView",
        [System.Reflection.BindingFlags]::InvokeMethod,
        $null,
        $Database,
        @($Query))
    try {
        $view.GetType().InvokeMember(
            "Execute",
            [System.Reflection.BindingFlags]::InvokeMethod,
            $null,
            $view,
            $null) | Out-Null
        while ($true) {
            $record = $view.GetType().InvokeMember(
                "Fetch",
                [System.Reflection.BindingFlags]::InvokeMethod,
                $null,
                $view,
                $null)
            if ($null -eq $record) {
                break
            }

            $values = [object[]]::new($ColumnCount)
            for ($column = 1; $column -le $ColumnCount; $column++) {
                $values[$column - 1] = $record.GetType().InvokeMember(
                    "StringData",
                    [System.Reflection.BindingFlags]::GetProperty,
                    $null,
                    $record,
                    $column)
            }
            $rows.Add($values)
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
        }
    } finally {
        $view.GetType().InvokeMember(
            "Close",
            [System.Reflection.BindingFlags]::InvokeMethod,
            $null,
            $view,
            $null) | Out-Null
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    }
    return $rows
}

$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.GetType().InvokeMember(
    "OpenDatabase",
    [System.Reflection.BindingFlags]::InvokeMethod,
    $null,
    $installer,
    @($resolvedInstaller, 0))
try {
    $summary = $database.SummaryInformation(0)
    try {
        $template = [string]$summary.Property(7)
        $summaryPackageCode = [string]$summary.Property(9)
        $created = [DateTime]$summary.Property(12)
        $lastSaved = [DateTime]$summary.Property(13)
        $wordCount = [int]$summary.Property(15)
        if ($template -notlike "x64;*") {
            throw "MSI summary template is not x64: $template"
        }
        if (($wordCount -band 8) -eq 0) {
            throw "MSI summary requests elevated privileges."
        }
        if ($ExpectedPackageCode -and
            $summaryPackageCode.Trim('{}') -ne $ExpectedPackageCode.Trim('{}')) {
            throw "Unexpected MSI PackageCode: $summaryPackageCode"
        }
        if ($created -ne [DateTime]::new(2000, 1, 1) -or
            $lastSaved -ne [DateTime]::new(2000, 1, 1)) {
            throw "MSI summary timestamps are not deterministic."
        }
    } finally {
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary)
    }

    $properties = @{}
    foreach ($row in Get-MsiRows $database 'SELECT `Property`, `Value` FROM `Property`' 2) {
        $properties[$row[0]] = $row[1]
    }

    if ($properties.ProductName -ne "QuickPods") {
        throw "Unexpected ProductName: $($properties.ProductName)"
    }
    if ($properties.ProductVersion -ne $ExpectedVersion) {
        throw "Unexpected ProductVersion: $($properties.ProductVersion)"
    }
    if ($properties.ProductLanguage -ne $ExpectedLanguage) {
        throw "Unexpected ProductLanguage: $($properties.ProductLanguage)"
    }
    if ($properties.ALLUSERS) {
        throw "ALLUSERS must be absent for the fixed per-user package."
    }
    if (-not $properties.ProductCode -or -not $properties.UpgradeCode) {
        throw "ProductCode and UpgradeCode are required."
    }

    $tables = @(
        Get-MsiRows $database 'SELECT `Name` FROM `_Tables`' 1 |
            ForEach-Object { $_[0] }
    )
    foreach ($prohibitedTable in @("ServiceInstall", "ServiceControl", "ODBCDataSource", "Environment")) {
        if ($tables -contains $prohibitedTable) {
            throw "Per-user MSI contains prohibited table $prohibitedTable."
        }
    }
    if ($tables -notcontains "Upgrade") {
        throw "Major-upgrade metadata is missing."
    }
    if ($tables -notcontains "Wix4CloseApplication") {
        throw "QuickPods process shutdown metadata is missing."
    }

    $directories = @(Get-MsiRows $database 'SELECT `Directory`, `Directory_Parent` FROM `Directory`' 2)
    $installDirectory = @($directories | Where-Object { $_[0] -eq "INSTALLFOLDER" })
    if ($installDirectory.Count -ne 1 -or $installDirectory[0][1] -ne "PerUserProgramFilesFolder") {
        throw "INSTALLFOLDER must be rooted in PerUserProgramFilesFolder."
    }

    $files = @(Get-MsiRows $database 'SELECT `File`, `FileName` FROM `File`' 2)
    foreach ($requiredFile in @(
        "QuickPods.exe",
        "QuickPods.TaskbarHost.exe",
        "QuickPods.TaskbarObserver.exe",
        "QuickPods.BluetoothWorker.exe",
        "QuickPods.dll",
        "QuickPods.TaskbarHost.dll",
        "QuickPods.TaskbarObserver.dll",
        "QuickPods.BluetoothWorker.dll",
        "QuickPods.deps.json",
        "QuickPods.TaskbarHost.deps.json",
        "QuickPods.TaskbarObserver.deps.json",
        "QuickPods.BluetoothWorker.deps.json",
        "QuickPods.runtimeconfig.json",
        "QuickPods.TaskbarHost.runtimeconfig.json",
        "QuickPods.TaskbarObserver.runtimeconfig.json",
        "QuickPods.BluetoothWorker.runtimeconfig.json",
        "hostfxr.dll",
        "hostpolicy.dll",
        "coreclr.dll",
        "PresentationFramework.dll",
        "LICENSE",
        "ThirdPartyNotices.txt",
        "DOTNET-LICENSE.txt",
        "DOTNET-THIRD-PARTY-NOTICES.txt",
        "README.txt",
        "artifact-manifest.json"
    )) {
        if (-not ($files | Where-Object { ($_[1] -split '\|')[-1] -eq $requiredFile })) {
            throw "MSI File table is missing $requiredFile."
        }
    }

    $customActions = @(Get-MsiRows $database 'SELECT `Action`, `Source`, `Target` FROM `CustomAction`' 3)
    $cleanupAction = @($customActions | Where-Object { $_[0] -eq "UnregisterQuickPodsStartup" })
    if ($cleanupAction.Count -ne 1 -or
        $cleanupAction[0][1] -ne "INSTALLFOLDER" -or
        $cleanupAction[0][2] -notlike "*--unregister-startup*") {
        throw "Uninstall startup cleanup action is missing or malformed."
    }

    $sequenceRows = @(Get-MsiRows $database 'SELECT `Action`, `Condition`, `Sequence` FROM `InstallExecuteSequence`' 3)
    $cleanupSequence = @($sequenceRows | Where-Object { $_[0] -eq "UnregisterQuickPodsStartup" })
    if ($cleanupSequence.Count -ne 1 -or
        $cleanupSequence[0][1] -notlike "*REMOVE*ALL*" -or
        $cleanupSequence[0][1] -notlike "*UPGRADINGPRODUCTCODE*") {
        throw "Uninstall startup cleanup is not guarded against upgrade removal."
    }

    $closeApplications = @(Get-MsiRows $database 'SELECT `CloseApplication`, `Target`, `Condition`, `Attributes`, `TerminateExitCode`, `Timeout` FROM `Wix4CloseApplication`' 6)
    $closeQuickPods = @($closeApplications | Where-Object { $_[0] -eq "CloseQuickPods" })
    if ($closeQuickPods.Count -ne 1 -or
        $closeQuickPods[0][1] -ne "QuickPods.exe" -or
        $closeQuickPods[0][2] -ne "Installed" -or
        [int]$closeQuickPods[0][3] -ne 33 -or
        [int]$closeQuickPods[0][4] -ne 0 -or
        [int]$closeQuickPods[0][5] -ne 5000) {
        throw "QuickPods update/uninstall shutdown policy is missing or malformed."
    }

    $closeSequence = @($sequenceRows | Where-Object { $_[0] -eq "Wix4CloseApplications_X64" })
    $removeFilesSequence = @($sequenceRows | Where-Object { $_[0] -eq "RemoveFiles" })
    if ($closeSequence.Count -ne 1 -or
        $closeSequence[0][1] -notlike "*VersionNT*" -or
        $removeFilesSequence.Count -ne 1 -or
        [int]$closeSequence[0][2] -le 1500 -or
        [int]$closeSequence[0][2] -ge [int]$cleanupSequence[0][2] -or
        [int]$cleanupSequence[0][2] -ge [int]$removeFilesSequence[0][2]) {
        throw "QuickPods shutdown and startup cleanup must run after InstallInitialize and before RemoveFiles."
    }

    $shortcuts = @(Get-MsiRows $database 'SELECT `Shortcut`, `Name`, `Target` FROM `Shortcut`' 3)
    if (-not ($shortcuts | Where-Object {
        $_[0] -eq "QuickPodsStartMenuShortcut" -and $_[2] -like "*QuickPods.exe*"
    })) {
        throw "QuickPods Start menu shortcut is missing."
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $resolvedInstaller
    if ($RequireSignature -and $signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "A valid installer signature is required; actual status is $($signature.Status)."
    }

    [pscustomobject]@{
        InstallerPath = $resolvedInstaller
        ProductVersion = $properties.ProductVersion
        ProductLanguage = $properties.ProductLanguage
        PackageCode = $summaryPackageCode
        Scope = "perUser"
        FileCount = $files.Count
        SignatureStatus = $signature.Status
        Sha256 = (Get-FileHash -LiteralPath $resolvedInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
    }
} finally {
    [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
    [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}
