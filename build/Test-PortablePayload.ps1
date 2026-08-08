[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PayloadDirectory,

    [string]$ExpectedVersion
)

$ErrorActionPreference = "Stop"
$payloadRoot = [System.IO.Path]::GetFullPath($PayloadDirectory)
if (-not (Test-Path -LiteralPath $payloadRoot -PathType Container)) {
    throw "Portable payload directory does not exist: $payloadRoot"
}

$applicationRoot = Join-Path $payloadRoot "app"
$selfContainedProof = & (Join-Path $PSScriptRoot "Test-SelfContainedPayload.ps1") `
    -PayloadDirectory $applicationRoot
$requiredRootFiles = @(
    "QuickPods.exe",
    "README.txt",
    "artifact-manifest.json"
)
$requiredLicenseFiles = @(
    "licenses/LICENSE",
    "licenses/ThirdPartyNotices.txt",
    "licenses/DOTNET-LICENSE.txt",
    "licenses/DOTNET-THIRD-PARTY-NOTICES.txt"
)
foreach ($requiredFile in $requiredRootFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $payloadRoot $requiredFile) -PathType Leaf)) {
        throw "Portable payload is missing root file $requiredFile."
    }
}
foreach ($requiredFile in $requiredLicenseFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $payloadRoot $requiredFile) -PathType Leaf)) {
        throw "Portable payload is missing license file $requiredFile."
    }
}

$unexpectedRootFiles = @(
    Get-ChildItem -LiteralPath $payloadRoot -File |
        Where-Object { $_.Name -notin $requiredRootFiles }
)
if ($unexpectedRootFiles.Count -gt 0) {
    throw "Portable payload has unexpected root files: $($unexpectedRootFiles.Name -join ', ')"
}

$rootDirectories = @(Get-ChildItem -LiteralPath $payloadRoot -Directory | ForEach-Object Name)
$expectedRootDirectories = @("app", "licenses")
if ($rootDirectories.Count -ne $expectedRootDirectories.Count -or
    @($rootDirectories | Where-Object { $_ -notin $expectedRootDirectories }).Count -gt 0) {
    throw "Portable payload root must contain only the app and licenses directories."
}

$unexpectedLicenseFiles = @(
    Get-ChildItem -LiteralPath (Join-Path $payloadRoot "licenses") -File |
        Where-Object { "licenses/$($_.Name)" -notin $requiredLicenseFiles }
)
if ($unexpectedLicenseFiles.Count -gt 0) {
    throw "Portable payload has unexpected license files: $($unexpectedLicenseFiles.Name -join ', ')"
}

$manifestPath = Join-Path $payloadRoot "artifact-manifest.json"
try {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
} catch {
    throw "Portable artifact manifest is invalid JSON."
}
if ($manifest.layoutVersion -ne 2 -or
    $manifest.entryPoint -ne "QuickPods.exe" -or
    $manifest.applicationDirectory -ne "app" -or
    $manifest.licenseDirectory -ne "licenses" -or
    $manifest.runtimeIdentifier -ne "win-x64" -or
    $manifest.selfContained -ne $true) {
    throw "Portable artifact manifest has invalid layout metadata."
}
if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and
    $manifest.version -ne $ExpectedVersion) {
    throw "Portable artifact manifest version '$($manifest.version)' does not match '$ExpectedVersion'."
}

$expectedExecutables = @("QuickPods.exe") + @(
    $selfContainedProof.Executables |
        ForEach-Object { "app/$($_)" }
)
$manifestExecutables = @($manifest.executables | ForEach-Object path)
foreach ($expectedExecutable in $expectedExecutables) {
    if ($manifestExecutables -notcontains $expectedExecutable) {
        throw "Portable manifest is missing executable $expectedExecutable."
    }
}

[pscustomobject]@{
    PayloadDirectory = $payloadRoot
    ApplicationDirectory = $applicationRoot
    EntryPoint = "QuickPods.exe"
    Executables = $expectedExecutables
    RuntimeFiles = @($selfContainedProof.RuntimeFiles | ForEach-Object { "app/$($_)" })
    RuntimeConfigs = @($selfContainedProof.RuntimeConfigs | ForEach-Object { "app/$($_)" })
    IncludedFrameworks = $selfContainedProof.IncludedFrameworks
    RootFiles = $requiredRootFiles
    LicenseFiles = $requiredLicenseFiles
}
