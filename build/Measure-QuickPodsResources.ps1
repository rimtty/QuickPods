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

$rootProcess = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
if (-not $rootProcess) {
    throw "Process $ProcessId is not running."
}

try {
    $rootStartedAtUtc = $rootProcess.StartTime.ToUniversalTime()
} catch {
    throw "Process $ProcessId identity could not be captured."
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
    $currentRoot = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if (-not $currentRoot) {
        throw "Primary process $ProcessId exited before the requested duration completed."
    }

    try {
        $currentRootStartedAtUtc = $currentRoot.StartTime.ToUniversalTime()
    } catch {
        throw "Primary process $ProcessId exited before the requested duration completed."
    }
    if ($currentRootStartedAtUtc -ne $rootStartedAtUtc) {
        throw "Primary process $ProcessId changed identity before the requested duration completed."
    }

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

    $metrics = foreach ($ownedId in $ownedIds) {
        try {
            $process = Get-Process -Id $ownedId -ErrorAction Stop
            $process.Refresh()
            if ($ownedId -eq $ProcessId -and
                $process.StartTime.ToUniversalTime() -ne $rootStartedAtUtc) {
                throw "Primary process identity changed."
            }

            $handle = $process.Handle
            [pscustomobject]@{
                CpuSeconds = $process.TotalProcessorTime.TotalSeconds
                WorkingSetBytes = $process.WorkingSet64
                PrivateMemoryBytes = $process.PrivateMemorySize64
                HandleCount = $process.HandleCount
                GdiObjectCount = [QuickPodsResourceNative]::GetGuiResources($handle, 0)
                UserObjectCount = [QuickPodsResourceNative]::GetGuiResources($handle, 1)
            }
        } catch {
            if ($ownedId -eq $ProcessId) {
                throw "Primary process $ProcessId exited or changed identity during sampling."
            }

            # A supervised child may legitimately exit between the CIM snapshot
            # and metric capture. Its replacement is discovered on the next sample.
        }
    }
    if (-not $metrics) {
        throw "No live QuickPods processes were available during sampling."
    }

    $row = [pscustomobject][ordered]@{
        TimestampUtc = [System.DateTimeOffset]::UtcNow.ToString("O")
        Sample = $sample
        ProcessCount = @($metrics).Count
        CpuSeconds = [Math]::Round((($metrics | Measure-Object CpuSeconds -Sum).Sum), 3)
        WorkingSetBytes = ($metrics | Measure-Object WorkingSetBytes -Sum).Sum
        PrivateMemoryBytes = ($metrics | Measure-Object PrivateMemoryBytes -Sum).Sum
        HandleCount = ($metrics | Measure-Object HandleCount -Sum).Sum
        GdiObjectCount = ($metrics | Measure-Object GdiObjectCount -Sum).Sum
        UserObjectCount = ($metrics | Measure-Object UserObjectCount -Sum).Sum
    }
    $row | Export-Csv -LiteralPath $resolvedOutput -NoTypeInformation -Encoding utf8 -Append
    $sample++
    Start-Sleep -Seconds $SampleIntervalSeconds
}

if ($sample -eq 0) {
    throw "No QuickPods resource samples were captured."
}

Get-Item -LiteralPath $resolvedOutput
