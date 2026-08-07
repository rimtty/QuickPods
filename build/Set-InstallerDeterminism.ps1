[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InstallerPath,

    [Parameter(Mandatory = $true)]
    [string]$PackageCode
)

$ErrorActionPreference = "Stop"
$resolvedInstaller = (Resolve-Path -LiteralPath $InstallerPath).Path
$normalizedPackageCode = "{$($PackageCode.Trim('{}').ToUpperInvariant())}"
$fixedTimestamp = [DateTime]::new(2000, 1, 1, 0, 0, 0)
$summaryScript = Join-Path $PSScriptRoot "Set-InstallerSummary.vbs"
& cscript.exe //nologo $summaryScript $resolvedInstaller $normalizedPackageCode
if ($LASTEXITCODE -ne 0) {
    throw "Failed to normalize MSI summary information."
}

$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase($resolvedInstaller, 0)
$summary = $database.SummaryInformation(0)
try {
    if ($summary.Property(9) -ne $normalizedPackageCode) {
        throw "MSI PackageCode normalization did not persist."
    }
    if ([DateTime]$summary.Property(12) -ne $fixedTimestamp -or
        [DateTime]$summary.Property(13) -ne $fixedTimestamp) {
        throw "MSI summary timestamps were not normalized."
    }
} finally {
    [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary)
    [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
    [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}

[pscustomobject]@{
    InstallerPath = $resolvedInstaller
    PackageCode = $normalizedPackageCode
    TimestampUtc = $fixedTimestamp
}
