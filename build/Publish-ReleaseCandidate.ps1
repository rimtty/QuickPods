[CmdletBinding()]
param(
    [string]$OutputDirectory = "artifacts/rc",
    [string]$Version = "0.1.0-rc.1",
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

$payloadDirectory = Join-Path $outputRoot "QuickPods"
if (Test-Path -LiteralPath $outputRoot) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $payloadDirectory -Force | Out-Null

$projects = @(
    "src/QuickPods.App/QuickPods.App.csproj",
    "src/QuickPods.TaskbarObserver/QuickPods.TaskbarObserver.csproj",
    "src/QuickPods.TaskbarHost/QuickPods.TaskbarHost.csproj",
    "src/QuickPods.BluetoothWorker/QuickPods.BluetoothWorker.csproj"
)

Push-Location $repositoryRoot
try {
    if (-not $SkipRestore) {
        # The app project graph includes every shipped executable. The normal solution lock
        # files intentionally describe the framework-dependent build, so use RID-specific
        # locks under ignored obj directories and never rewrite the committed locks.
        & dotnet restore $projects[0] `
            --runtime win-x64 `
            -p:NuGetLockFilePath=obj/project.rc.packages.lock.json `
            -p:RestoreLockedMode=false `
            -p:RestoreForceEvaluate=true
        if ($LASTEXITCODE -ne 0) {
            throw "Release-candidate restore failed."
        }
    }

    foreach ($project in $projects) {
        & dotnet publish $project `
            --configuration Release `
            --runtime win-x64 `
            --self-contained true `
            --no-restore `
            --output $payloadDirectory `
            -p:Version=$Version `
            -p:ContinuousIntegrationBuild=true `
            -p:DebugSymbols=false `
            -p:DebugType=None `
            -p:PublishReadyToRun=false `
            -p:PublishSingleFile=false
        if ($LASTEXITCODE -ne 0) {
            throw "Publish failed for $project."
        }
    }
} finally {
    Pop-Location
}

$requiredFiles = @(
    "QuickPods.exe",
    "QuickPods.TaskbarHost.exe",
    "QuickPods.TaskbarObserver.exe",
    "QuickPods.BluetoothWorker.exe"
)
foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $payloadDirectory $requiredFile))) {
        throw "Release candidate is missing $requiredFile."
    }
}

$dotnetRoot = Split-Path -Parent (Get-Command dotnet).Source
$legalFiles = [ordered]@{
    (Join-Path $repositoryRoot "ThirdPartyNotices.txt") = "ThirdPartyNotices.txt"
    (Join-Path $dotnetRoot "LICENSE.txt") = "DOTNET-LICENSE.txt"
    (Join-Path $dotnetRoot "ThirdPartyNotices.txt") = "DOTNET-THIRD-PARTY-NOTICES.txt"
}
foreach ($legalFile in $legalFiles.GetEnumerator()) {
    if (-not (Test-Path -LiteralPath $legalFile.Key)) {
        throw "Required legal file is missing: $($legalFile.Key)"
    }

    Copy-Item -LiteralPath $legalFile.Key -Destination (Join-Path $payloadDirectory $legalFile.Value)
}

$executables = foreach ($requiredFile in $requiredFiles) {
    $versionInfo = (Get-Item -LiteralPath (Join-Path $payloadDirectory $requiredFile)).VersionInfo
    if (-not $versionInfo.ProductVersion.StartsWith($Version, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$requiredFile has product version '$($versionInfo.ProductVersion)', expected '$Version'."
    }

    [ordered]@{
        path = $requiredFile
        productVersion = $versionInfo.ProductVersion
        fileVersion = $versionInfo.FileVersion
    }
}

$manifestPath = Join-Path $payloadDirectory "artifact-manifest.json"
$files = Get-ChildItem -LiteralPath $payloadDirectory -File -Recurse |
    Where-Object { $_.FullName -ne $manifestPath } |
    Sort-Object { [System.IO.Path]::GetRelativePath($payloadDirectory, $_.FullName) } |
    ForEach-Object {
        [ordered]@{
            path = [System.IO.Path]::GetRelativePath($payloadDirectory, $_.FullName).Replace("\", "/")
            length = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }

$manifest = [ordered]@{
    product = "QuickPods"
    version = $Version
    runtimeIdentifier = "win-x64"
    selfContained = $true
    signed = $false
    executables = @($executables)
    files = @($files)
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM

$archivePath = Join-Path $outputRoot "QuickPods-$Version-win-x64.zip"
Add-Type -AssemblyName System.IO.Compression
$archiveStream = [System.IO.File]::Open(
    $archivePath,
    [System.IO.FileMode]::CreateNew,
    [System.IO.FileAccess]::ReadWrite,
    [System.IO.FileShare]::None)
try {
    $archive = [System.IO.Compression.ZipArchive]::new(
        $archiveStream,
        [System.IO.Compression.ZipArchiveMode]::Create,
        $false)
    try {
        $fixedTimestamp = [System.DateTimeOffset]::new(
            2000,
            1,
            1,
            0,
            0,
            0,
            [System.TimeSpan]::Zero)
        Get-ChildItem -LiteralPath $payloadDirectory -File -Recurse |
            Sort-Object { [System.IO.Path]::GetRelativePath($outputRoot, $_.FullName) } |
            ForEach-Object {
                $entryName = [System.IO.Path]::GetRelativePath($outputRoot, $_.FullName).Replace("\", "/")
                $entry = $archive.CreateEntry(
                    $entryName,
                    [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedTimestamp
                $entryStream = $entry.Open()
                $sourceStream = [System.IO.File]::OpenRead($_.FullName)
                try {
                    $sourceStream.CopyTo($entryStream)
                } finally {
                    $sourceStream.Dispose()
                    $entryStream.Dispose()
                }
            }
    } finally {
        $archive.Dispose()
    }
} finally {
    $archiveStream.Dispose()
}

$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = Join-Path $outputRoot "SHA256SUMS.txt"
"$archiveHash  $([System.IO.Path]::GetFileName($archivePath))" |
    Set-Content -LiteralPath $checksumPath -Encoding ascii

[pscustomobject]@{
    Version = $Version
    PayloadDirectory = $payloadDirectory
    ArchivePath = $archivePath
    ArchiveSha256 = $archiveHash
    FileCount = @($files).Count + 1
}
