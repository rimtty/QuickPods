[CmdletBinding()]
param(
    [string]$OutputDirectory = "artifacts/installer",
    [string]$Version = "0.1.0-rc.1",
    [string]$PayloadDirectory,
    [switch]$SkipPayloadPublish,
    [switch]$SkipRestore
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
}
$repositoryPrefix = $repositoryRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
if (-not $outputRoot.StartsWith($repositoryPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must be inside the QuickPods repository."
}
if ($outputRoot -eq $repositoryRoot) {
    throw "OutputDirectory must not be the repository root."
}

if ($Version -notmatch '^(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:[-+].*)?$') {
    throw "Version must start with a three-part semantic version: $Version"
}
$major = [int]$Matches.major
$minor = [int]$Matches.minor
$patch = [int]$Matches.patch
$isPrerelease = $Version.Contains("-")
if ($isPrerelease) {
    if ($Version -match '-rc\.(?<ordinal>[0-9]+)') {
        $prereleaseOrdinal = 60000 + [int]$Matches.ordinal
    } elseif ($Version -match '-ci\.(?<ordinal>[0-9]+)') {
        $prereleaseOrdinal = 1 + (([int]$Matches.ordinal - 1) % 59999)
    } else {
        $ordinalBytes = [System.Text.Encoding]::UTF8.GetBytes($Version)
        $ordinalHash = [System.Security.Cryptography.SHA256]::HashData($ordinalBytes)
        $prereleaseOrdinal = 1 + ([BitConverter]::ToUInt32($ordinalHash, 0) % 59999)
    }
    if ($prereleaseOrdinal -gt 65535) {
        throw "Pre-release ordinal exceeds the Windows Installer version range: $Version"
    }

    if ($minor -gt 0) {
        $packageVersion = "$major.$($minor - 1).$prereleaseOrdinal"
    } elseif ($major -gt 0) {
        $packageVersion = "$($major - 1).255.$prereleaseOrdinal"
    } else {
        $packageVersion = "0.0.$prereleaseOrdinal"
    }
} else {
    $packageVersion = "$major.$minor.$patch"
}

$productCodeBytes = [System.Text.Encoding]::UTF8.GetBytes("QuickPods.Setup.Product|$Version|x64|perUser")
$productCodeHex = [Convert]::ToHexString(
    [System.Security.Cryptography.SHA256]::HashData($productCodeBytes)).Substring(0, 32)
$packageProductCode = "$($productCodeHex.Substring(0, 8))-$($productCodeHex.Substring(8, 4))-$($productCodeHex.Substring(12, 4))-$($productCodeHex.Substring(16, 4))-$($productCodeHex.Substring(20, 12))"
$packageCodeBytes = [System.Text.Encoding]::UTF8.GetBytes("QuickPods.Setup.Package|$Version|x64|perUser")
$packageCodeHex = [Convert]::ToHexString(
    [System.Security.Cryptography.SHA256]::HashData($packageCodeBytes)).Substring(0, 32)
$packageCode = "$($packageCodeHex.Substring(0, 8))-$($packageCodeHex.Substring(8, 4))-$($packageCodeHex.Substring(12, 4))-$($packageCodeHex.Substring(16, 4))-$($packageCodeHex.Substring(20, 12))"
$safeArtifactVersion = $Version -replace '[^A-Za-z0-9._-]', '-'
$payloadOutput = Join-Path $outputRoot "payload"
$payloadRoot = if ([string]::IsNullOrWhiteSpace($PayloadDirectory)) {
    Join-Path $payloadOutput "QuickPods"
} elseif ([System.IO.Path]::IsPathRooted($PayloadDirectory)) {
    [System.IO.Path]::GetFullPath($PayloadDirectory)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $PayloadDirectory))
}
$fragmentPath = Join-Path $outputRoot "generated\PayloadComponents.wxs"
$installerName = "QuickPods-$safeArtifactVersion-win-x64"
$installerPath = Join-Path $outputRoot "$installerName.msi"
$wixOutputDirectory = Join-Path $outputRoot ".wix-output"
$wixBuiltPath = Join-Path $wixOutputDirectory "$installerName.msi"
$checksumPath = Join-Path $outputRoot "SHA256SUMS.txt"
$manifestPath = Join-Path $outputRoot "installer-manifest.json"

foreach ($staleArtifact in @($installerPath, $checksumPath, $manifestPath)) {
    if (Test-Path -LiteralPath $staleArtifact -PathType Leaf) {
        Remove-Item -LiteralPath $staleArtifact -Force
    }
}
if (Test-Path -LiteralPath $wixOutputDirectory -PathType Container) {
    Remove-Item -LiteralPath $wixOutputDirectory -Recurse -Force
}

if (-not $SkipPayloadPublish) {
    if (Test-Path -LiteralPath $outputRoot) {
        Remove-Item -LiteralPath $outputRoot -Recurse -Force
    }

    & (Join-Path $PSScriptRoot "Publish-ReleaseCandidate.ps1") `
        -OutputDirectory $payloadOutput `
        -Version $Version `
        -SkipRestore:$SkipRestore
} elseif (-not (Test-Path -LiteralPath $payloadRoot -PathType Container)) {
    throw "SkipPayloadPublish requires an existing payload at $payloadRoot."
}

& (Join-Path $PSScriptRoot "New-InstallerPayloadFragment.ps1") `
    -PayloadDirectory $payloadRoot `
    -OutputPath $fragmentPath

$projectPath = Join-Path $repositoryRoot "installer\QuickPods.Setup\QuickPods.Setup.wixproj"
$buildArguments = @(
    "build",
    $projectPath,
    "--configuration", "Release",
    "-p:PackageVersion=$packageVersion",
    "-p:PackageProductCode=$packageProductCode",
    "-p:PayloadDirectory=$payloadRoot",
    "-p:GeneratedPayloadFragment=$fragmentPath",
    "-p:InstallerOutputDirectory=$wixOutputDirectory",
    "-p:OutputName=$installerName",
    "-p:ContinuousIntegrationBuild=true"
)
if ($SkipRestore) {
    $buildArguments += "--no-restore"
}

& dotnet @buildArguments
if ($LASTEXITCODE -ne 0) {
    throw "WiX installer build failed."
}
if (-not (Test-Path -LiteralPath $wixBuiltPath -PathType Leaf)) {
    throw "WiX build did not produce $wixBuiltPath."
}
[System.IO.File]::Copy($wixBuiltPath, $installerPath, $true)
Remove-Item -LiteralPath $wixOutputDirectory -Recurse -Force

& (Join-Path $PSScriptRoot "Set-InstallerDeterminism.ps1") `
    -InstallerPath $installerPath `
    -PackageCode $packageCode

$signature = Get-AuthenticodeSignature -LiteralPath $installerPath
$installerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$installerHash  $([System.IO.Path]::GetFileName($installerPath))" |
    Set-Content -LiteralPath $checksumPath -Encoding ascii

$manifest = [ordered]@{
    product = "QuickPods"
    artifactVersion = $Version
    windowsInstallerVersion = $packageVersion
    productCode = $packageProductCode
    packageCode = $packageCode
    allowsSameVersionUpgrade = $false
    architecture = "x64"
    scope = "perUser"
    requiresElevation = $false
    signed = $signature.Status -eq [System.Management.Automation.SignatureStatus]::Valid
    signatureStatus = $signature.Status.ToString()
    sha256 = $installerHash
    fileName = [System.IO.Path]::GetFileName($installerPath)
    payloadManifest = "payload-artifact-manifest.json"
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
Copy-Item `
    -LiteralPath (Join-Path $payloadRoot "artifact-manifest.json") `
    -Destination (Join-Path $outputRoot "payload-artifact-manifest.json") `
    -Force

& (Join-Path $PSScriptRoot "Test-InstallerPackage.ps1") `
    -InstallerPath $installerPath `
    -ExpectedVersion $packageVersion `
    -ExpectedPackageCode $packageCode

[pscustomobject]@{
    Version = $Version
    PackageVersion = $packageVersion
    InstallerPath = $installerPath
    InstallerSha256 = $installerHash
    SignatureStatus = $signature.Status
}
