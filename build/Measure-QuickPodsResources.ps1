[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [int]$ProcessId,
    [ValidateRange(1, 604800)]
    [int]$DurationSeconds = 600,
    [ValidateRange(1, 3600)]
    [int]$SampleIntervalSeconds = 5,
    [string]$OutputPath = "artifacts/metrics/quickpods-resources.csv"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$resolvedOutput = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    [System.IO.Path]::GetFullPath($OutputPath)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
}
$repositoryPrefix = $repositoryRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
if (-not $resolvedOutput.StartsWith($repositoryPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputPath must be inside the QuickPods repository."
}

if (-not (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)) {
    throw "Process $ProcessId is not running."
}

if (-not ("QuickPodsResourceNative" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class QuickPodsResourceNative
{
    [DllImport("user32.dll")]
    public static extern uint GetGuiResources(IntPtr process, uint flags);
}
"@
}

$directory = Split-Path -Parent $resolvedOutput
New-Item -ItemType Directory -Path $directory -Force | Out-Null
if (Test-Path -LiteralPath $resolvedOutput) {
    Remove-Item -LiteralPath $resolvedOutput -Force
}

$deadline = [System.DateTimeOffset]::UtcNow.AddSeconds($DurationSeconds)
$sample = 0
while ([System.DateTimeOffset]::UtcNow -lt $deadline) {
    $allProcesses = Get-CimInstance Win32_Process
    $ownedIds = [System.Collections.Generic.HashSet[int]]::new()
    [void]$ownedIds.Add($ProcessId)
    do {
        $added = $false
        foreach ($candidate in $allProcesses) {
            if ($ownedIds.Contains([int]$candidate.ParentProcessId) -and
                $ownedIds.Add([int]$candidate.ProcessId)) {
                $added = $true
            }
        }
    } while ($added)

    $processes = foreach ($ownedId in $ownedIds) {
        Get-Process -Id $ownedId -ErrorAction SilentlyContinue
    }
    if (-not $processes) {
        break
    }

    $gdiObjects = 0L
    $userObjects = 0L
    foreach ($process in $processes) {
        $gdiObjects += [QuickPodsResourceNative]::GetGuiResources($process.Handle, 0)
        $userObjects += [QuickPodsResourceNative]::GetGuiResources($process.Handle, 1)
    }

    $row = [pscustomobject][ordered]@{
        TimestampUtc = [System.DateTimeOffset]::UtcNow.ToString("O")
        Sample = $sample
        ProcessCount = @($processes).Count
        CpuSeconds = [Math]::Round((($processes | Measure-Object CPU -Sum).Sum), 3)
        WorkingSetBytes = ($processes | Measure-Object WorkingSet64 -Sum).Sum
        PrivateMemoryBytes = ($processes | Measure-Object PrivateMemorySize64 -Sum).Sum
        HandleCount = ($processes | Measure-Object HandleCount -Sum).Sum
        GdiObjectCount = $gdiObjects
        UserObjectCount = $userObjects
    }
    $row | Export-Csv -LiteralPath $resolvedOutput -NoTypeInformation -Encoding utf8 -Append
    $sample++
    Start-Sleep -Seconds $SampleIntervalSeconds
}

if ($sample -eq 0) {
    throw "No QuickPods resource samples were captured."
}

Get-Item -LiteralPath $resolvedOutput
