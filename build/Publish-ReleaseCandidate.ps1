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
$applicationDirectory = Join-Path $payloadDirectory "app"
$licenseDirectory = Join-Path $payloadDirectory "licenses"
$buildArtifactsDirectory = Join-Path $outputRoot ".build-artifacts"
$launcherPublishDirectory = Join-Path $buildArtifactsDirectory "launcher-publish"
if (Test-Path -LiteralPath $outputRoot) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $applicationDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $licenseDirectory -Force | Out-Null

$applicationProjects = @(
    "src/QuickPods.App/QuickPods.App.csproj",
    "src/QuickPods.TaskbarObserver/QuickPods.TaskbarObserver.csproj",
    "src/QuickPods.TaskbarHost/QuickPods.TaskbarHost.csproj",
    "src/QuickPods.BluetoothWorker/QuickPods.BluetoothWorker.csproj"
)
$launcherProject = "src/QuickPods.Launcher/QuickPods.Launcher.csproj"

Push-Location $repositoryRoot
try {
    if (-not $SkipRestore) {
        # The app project graph includes every shipped executable. The normal solution lock
        # files intentionally describe the framework-dependent build, so use RID-specific
        # locks under ignored obj directories and never rewrite the committed locks.
        & dotnet restore $applicationProjects[0] `
            --runtime win-x64 `
            --artifacts-path $buildArtifactsDirectory `
            -p:NuGetLockFilePath=obj/project.rc.packages.lock.json `
            -p:RestoreLockedMode=false `
            -p:RestoreForceEvaluate=true
        if ($LASTEXITCODE -ne 0) {
            throw "Release-candidate restore failed."
        }

        & dotnet restore $launcherProject `
            --runtime win-x64 `
            --artifacts-path $buildArtifactsDirectory `
            -p:NuGetLockFilePath=obj/project.rc.packages.lock.json `
            -p:RestoreLockedMode=false `
            -p:RestoreForceEvaluate=true
        if ($LASTEXITCODE -ne 0) {
            throw "Portable launcher restore failed."
        }
    }

    foreach ($project in $applicationProjects) {
        & dotnet publish $project `
            --configuration Release `
            --runtime win-x64 `
            --self-contained true `
            --no-restore `
            --artifacts-path $buildArtifactsDirectory `
            --output $applicationDirectory `
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

    & dotnet publish $launcherProject `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --no-restore `
        --artifacts-path $buildArtifactsDirectory `
        --output $launcherPublishDirectory `
        -p:Version=$Version `
        -p:ContinuousIntegrationBuild=true `
        -p:DebugSymbols=false `
        -p:DebugType=None `
        -p:PublishAot=true
    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed for $launcherProject."
    }

    $launcherExecutable = Join-Path $launcherPublishDirectory "QuickPods.exe"
    if (-not (Test-Path -LiteralPath $launcherExecutable -PathType Leaf)) {
        throw "Native portable launcher was not produced."
    }
    Copy-Item -LiteralPath $launcherExecutable -Destination $payloadDirectory
} finally {
    Pop-Location
    if (Test-Path -LiteralPath $buildArtifactsDirectory -PathType Container) {
        Remove-Item -LiteralPath $buildArtifactsDirectory -Recurse -Force
    }
}

$dotnetRoot = Split-Path -Parent (Get-Command dotnet).Source
$legalFiles = [ordered]@{
    (Join-Path $repositoryRoot "LICENSE") = "LICENSE"
    (Join-Path $repositoryRoot "ThirdPartyNotices.txt") = "ThirdPartyNotices.txt"
    (Join-Path $dotnetRoot "LICENSE.txt") = "DOTNET-LICENSE.txt"
    (Join-Path $dotnetRoot "ThirdPartyNotices.txt") = "DOTNET-THIRD-PARTY-NOTICES.txt"
}
foreach ($legalFile in $legalFiles.GetEnumerator()) {
    if (-not (Test-Path -LiteralPath $legalFile.Key)) {
        throw "Required legal file is missing: $($legalFile.Key)"
    }

    Copy-Item -LiteralPath $legalFile.Key -Destination (Join-Path $licenseDirectory $legalFile.Value)
}

Copy-Item `
    -LiteralPath (Join-Path $repositoryRoot "packaging\PORTABLE-README.txt") `
    -Destination (Join-Path $payloadDirectory "README.txt")

$selfContainedProof = & (Join-Path $PSScriptRoot "Test-SelfContainedPayload.ps1") `
    -PayloadDirectory $applicationDirectory
$requiredExecutables = @("QuickPods.exe") + @(
    $selfContainedProof.Executables |
        ForEach-Object { "app/$($_)" }
)

$executables = foreach ($requiredFile in $requiredExecutables) {
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
    layoutVersion = 2
    entryPoint = "QuickPods.exe"
    applicationDirectory = "app"
    licenseDirectory = "licenses"
    runtimeIdentifier = "win-x64"
    selfContained = $true
    includedFrameworks = $selfContainedProof.IncludedFrameworks
    signed = $false
    executables = @($executables)
    files = @($files)
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM

& (Join-Path $PSScriptRoot "Test-PortablePayload.ps1") `
    -PayloadDirectory $payloadDirectory `
    -ExpectedVersion $Version | Out-Null

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
    EntryPoint = (Join-Path $payloadDirectory "QuickPods.exe")
    ArchivePath = $archivePath
    ArchiveSha256 = $archiveHash
    FileCount = @($files).Count + 1
}
