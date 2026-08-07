[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$CandidateDirectory,

    [string]$OutputPath = "artifacts/gates/explorer-recovery.json",

    [ValidateRange(1, 30)]
    [int]$RecoveryTimeoutSeconds = 10,

    [ValidateRange(1, 60)]
    [int]$SettlementSeconds = 10,

    [switch]$PreflightOnly,

    [switch]$ConfirmExplorerRestart
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$candidateDirectoryResolved = [IO.Path]::GetFullPath($CandidateDirectory).TrimEnd('\')
$candidateExe = Join-Path $candidateDirectoryResolved "QuickPods.exe"
$manifestPath = Join-Path $candidateDirectoryResolved "artifact-manifest.json"

if (-not $PreflightOnly -and -not $ConfirmExplorerRestart) {
    throw "Use -PreflightOnly or explicitly pass -ConfirmExplorerRestart."
}
if ($PreflightOnly -and $ConfirmExplorerRestart) {
    throw "-PreflightOnly and -ConfirmExplorerRestart are mutually exclusive."
}
if (-not (Test-Path -LiteralPath $candidateExe -PathType Leaf)) {
    throw "Candidate executable was not found: $candidateExe"
}
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Candidate manifest was not found: $manifestPath"
}

$resolvedOutput = if ([IO.Path]::IsPathRooted($OutputPath)) {
    [IO.Path]::GetFullPath($OutputPath)
} else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
}
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
if (-not $resolvedOutput.StartsWith(
        $repositoryPrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputPath must be inside the QuickPods repository."
}

if (-not ("QuickPodsExplorerGateNative" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class QuickPodsExplorerGateNative
{
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(
        IntPtr parent,
        EnumWindowsProc callback,
        IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(
        IntPtr hwnd,
        StringBuilder className,
        int maximumCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern uint GetGuiResources(IntPtr process, uint flags);
}
"@
}

$expectedProcessNames = @(
    "QuickPods",
    "QuickPods.TaskbarHost",
    "QuickPods.TaskbarObserver"
)
$currentSessionId = (Get-Process -Id $PID).SessionId
$logPath = Join-Path $env:LOCALAPPDATA (
    "QuickPods\logs\quickpods-{0}.jsonl" -f (Get-Date -Format "yyyyMMdd"))
$gateStartedAt = [DateTimeOffset]::UtcNow
$logBaselineLineCount = if (Test-Path -LiteralPath $logPath -PathType Leaf) {
    @(Get-Content -LiteralPath $logPath).Count
} else {
    0
}

function Get-CandidateProcesses {
    @(
        Get-Process -Name $expectedProcessNames -ErrorAction SilentlyContinue |
            Where-Object {
                try {
                    $_.Path.StartsWith(
                        $candidateDirectoryResolved + '\',
                        [StringComparison]::OrdinalIgnoreCase)
                } catch {
                    $false
                }
            }
    )
}

function Get-ExplorerProcess {
    @(
        Get-Process -Name explorer -ErrorAction SilentlyContinue |
            Where-Object SessionId -eq $currentSessionId
    )
}

function Get-WindowInventory {
    param([Parameter(Mandatory)][Diagnostics.Process[]]$Processes)

    $processIds = [Collections.Generic.HashSet[uint32]]::new()
    foreach ($process in $Processes) {
        [void]$processIds.Add([uint32]$process.Id)
    }

    $windows = [Collections.Generic.List[object]]::new()
    $handles = [Collections.Generic.HashSet[long]]::new()
    $topLevelHandles = [Collections.Generic.List[IntPtr]]::new()
    $collectTopLevelHandles = $true
    $callback = [QuickPodsExplorerGateNative+EnumWindowsProc]{
        param([IntPtr]$hwnd, [IntPtr]$parameter)

        if ($collectTopLevelHandles) {
            $topLevelHandles.Add($hwnd)
        }

        [uint32]$processId = 0
        [void][QuickPodsExplorerGateNative]::GetWindowThreadProcessId(
            $hwnd,
            [ref]$processId)
        $classBuilder = [Text.StringBuilder]::new(256)
        [void][QuickPodsExplorerGateNative]::GetClassName(
            $hwnd,
            $classBuilder,
            $classBuilder.Capacity)
        $className = $classBuilder.ToString()
        if (($processIds.Contains($processId) -or
                $className.StartsWith("QuickPods.", [StringComparison]::Ordinal)) -and
            $handles.Add($hwnd.ToInt64())) {
            $windows.Add([pscustomobject][ordered]@{
                Handle = ('0x{0:X}' -f $hwnd.ToInt64())
                ProcessId = $processId
                ClassName = $className
            })
        }

        return $true
    }

    [void][QuickPodsExplorerGateNative]::EnumWindows($callback, [IntPtr]::Zero)
    $collectTopLevelHandles = $false
    foreach ($topLevelHandle in @($topLevelHandles)) {
        [void][QuickPodsExplorerGateNative]::EnumChildWindows(
            $topLevelHandle,
            $callback,
            [IntPtr]::Zero)
    }

    return @($windows)
}

function Get-ProcessEvidence {
    param([Parameter(Mandatory)][Diagnostics.Process]$Process)

    $Process.Refresh()
    [pscustomobject][ordered]@{
        Name = $Process.ProcessName
        Id = $Process.Id
        StartedAtUtc = $Process.StartTime.ToUniversalTime().ToString("O")
        Path = $Process.Path
        Responding = $Process.Responding
        HandleCount = $Process.HandleCount
        PrivateMemoryBytes = $Process.PrivateMemorySize64
        GdiObjectCount = [QuickPodsExplorerGateNative]::GetGuiResources(
            $Process.Handle,
            0)
        UserObjectCount = [QuickPodsExplorerGateNative]::GetGuiResources(
            $Process.Handle,
            1)
    }
}

function Get-GateState {
    $processes = @(Get-CandidateProcesses)
    $windows = @(Get-WindowInventory -Processes $processes)
    $explorer = @(Get-ExplorerProcess)
    [pscustomobject][ordered]@{
        CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        Explorer = @(
            $explorer | ForEach-Object {
                [pscustomobject][ordered]@{
                    Id = $_.Id
                    StartedAtUtc = $_.StartTime.ToUniversalTime().ToString("O")
                }
            }
        )
        Processes = @($processes | Sort-Object ProcessName | ForEach-Object {
            Get-ProcessEvidence -Process $_
        })
        Counts = [pscustomobject][ordered]@{
            App = @($processes | Where-Object ProcessName -eq "QuickPods").Count
            Host = @($processes | Where-Object ProcessName -eq "QuickPods.TaskbarHost").Count
            Observer = @($processes | Where-Object ProcessName -eq "QuickPods.TaskbarObserver").Count
            Native = @($windows | Where-Object ClassName -eq "QuickPods.Taskbar.View").Count
            Floating = @($windows | Where-Object ClassName -eq "QuickPods.Floating.View").Count
        }
    }
}

function Assert-Baseline {
    param([Parameter(Mandatory)]$State)

    if (@($State.Explorer).Count -ne 1) {
        throw "Exactly one Explorer process is required in the current session."
    }
    if ($State.Counts.App -ne 1 -or
        $State.Counts.Host -ne 1 -or
        $State.Counts.Observer -ne 1 -or
        $State.Counts.Native -ne 1 -or
        $State.Counts.Floating -ne 0) {
        throw "Baseline must contain one App, Host, Observer, and native surface, with zero floating surfaces."
    }
    if (@($State.Processes | Where-Object { -not $_.Responding }).Count -ne 0) {
        throw "Every baseline QuickPods process must be responding."
    }
}

function Get-NamedProcessEvidence {
    param(
        [Parameter(Mandatory)]$State,
        [Parameter(Mandatory)][string]$Name
    )

    @($State.Processes | Where-Object Name -eq $Name) | Select-Object -First 1
}

function Get-NewLogEntries {
    if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
        return @()
    }

    @(
        Get-Content -LiteralPath $logPath |
            Select-Object -Skip $logBaselineLineCount |
            ForEach-Object {
                try {
                    $entry = $_ | ConvertFrom-Json -ErrorAction Stop
                    $entry
                } catch {
                    # A malformed line is represented below as a gate failure.
                    [pscustomobject]@{
                        Timestamp = [DateTimeOffset]::UtcNow.ToString("O")
                        Level = 3
                        EventName = "MalformedLogLine"
                        Message = "A QuickPods JSONL log line could not be parsed."
                        Properties = [pscustomobject]@{}
                    }
                }
            }
    )
}

$manifestDocument = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$manifest = [pscustomobject][ordered]@{
    Product = $manifestDocument.product
    Version = $manifestDocument.version
    RuntimeIdentifier = $manifestDocument.runtimeIdentifier
    SelfContained = $manifestDocument.selfContained
    Signed = $manifestDocument.signed
    Executables = $manifestDocument.executables
}
if ($manifest.SelfContained -ne $true) {
    throw "The Explorer recovery gate requires a self-contained candidate."
}

$sessionText = (& quser 2>&1 | Out-String).Trim()
$isLocalConsole = $sessionText -match '(?m)^>.*\sconsole\s+\d+\s'
if (-not $isLocalConsole) {
    throw "The Explorer recovery gate must run in the active local console session."
}
$baseline = Get-GateState
Assert-Baseline -State $baseline
$baselineApp = Get-NamedProcessEvidence -State $baseline -Name "QuickPods"
$baselineHost = Get-NamedProcessEvidence -State $baseline -Name "QuickPods.TaskbarHost"
$baselineObserver = Get-NamedProcessEvidence -State $baseline -Name "QuickPods.TaskbarObserver"
$baselineExplorer = @($baseline.Explorer)[0]

$result = [ordered]@{
    SchemaVersion = 1
    Mode = if ($PreflightOnly) { "PreflightOnly" } else { "ExplorerRestart" }
    Status = "Running"
    StartedAtUtc = $gateStartedAt.ToString("O")
    Session = $sessionText
    SessionName = $env:SESSIONNAME
    IsLocalConsole = $isLocalConsole
    CandidateDirectory = $candidateDirectoryResolved
    Manifest = $manifest
    RecoveryTimeoutSeconds = $RecoveryTimeoutSeconds
    SettlementSeconds = $SettlementSeconds
    Baseline = $baseline
    Transitions = @()
    RecoveryElapsedMilliseconds = $null
    After = $null
    ResourceDeltas = $null
    LogEvidence = $null
    Assertions = $null
    Failure = $null
}

$failure = $null
try {
    if ($PreflightOnly) {
        $result.Status = "PreflightPass"
    } else {
        $oldExplorerProcess = Get-Process -Id $baselineExplorer.Id -ErrorAction Stop
        Stop-Process -Id $oldExplorerProcess.Id -Force
        if (-not $oldExplorerProcess.WaitForExit(5000)) {
            throw "The old Explorer process did not exit within five seconds."
        }

        $recoveryTimer = [Diagnostics.Stopwatch]::StartNew()
        $restartDeadline = [DateTimeOffset]::UtcNow.AddSeconds(2)
        do {
            $replacementExplorer = @(
                Get-ExplorerProcess | Where-Object Id -ne $baselineExplorer.Id
            )
            if ($replacementExplorer.Count -eq 0) {
                Start-Sleep -Milliseconds 100
            }
        } while ($replacementExplorer.Count -eq 0 -and
            [DateTimeOffset]::UtcNow -lt $restartDeadline)
        if ($replacementExplorer.Count -eq 0) {
            Start-Process -FilePath (Join-Path $env:WINDIR "explorer.exe")
        }

        $deadline = [DateTimeOffset]::UtcNow.AddSeconds($RecoveryTimeoutSeconds)
        $transitions = [Collections.Generic.List[object]]::new()
        $result.Transitions = $transitions
        $lastSignature = $null
        $maximumCounts = [ordered]@{
            App = 0
            Host = 0
            Observer = 0
            Native = 0
            Floating = 0
        }
        $recovered = $false
        do {
            $state = Get-GateState
            foreach ($name in @("App", "Host", "Observer", "Native", "Floating")) {
                $maximumCounts[$name] = [Math]::Max(
                    $maximumCounts[$name],
                    [int]$state.Counts.$name)
            }

            $app = Get-NamedProcessEvidence -State $state -Name "QuickPods"
            $hostEvidence = Get-NamedProcessEvidence -State $state -Name "QuickPods.TaskbarHost"
            $observer = Get-NamedProcessEvidence -State $state -Name "QuickPods.TaskbarObserver"
            $newExplorer = @($state.Explorer | Where-Object Id -ne $baselineExplorer.Id)
            $signature = @(
                $state.Counts.App,
                $state.Counts.Host,
                $state.Counts.Observer,
                $state.Counts.Native,
                $state.Counts.Floating,
                $newExplorer.Id -join ',',
                $observer.Id
            ) -join '|'
            if ($signature -ne $lastSignature) {
                $transitions.Add([pscustomobject][ordered]@{
                    ElapsedMilliseconds = $recoveryTimer.ElapsedMilliseconds
                    Counts = $state.Counts
                    ExplorerIds = @($state.Explorer.Id)
                    ObserverIds = @(
                        $state.Processes |
                            Where-Object Name -eq "QuickPods.TaskbarObserver" |
                            Select-Object -ExpandProperty Id
                    )
                })
                $lastSignature = $signature
            }

            if ($state.Counts.App -gt 1 -or
                $state.Counts.Host -gt 1 -or
                $state.Counts.Observer -gt 1 -or
                $state.Counts.Native -gt 1 -or
                $state.Counts.Floating -gt 0) {
                throw "A duplicate process or unsupported floating surface appeared during recovery."
            }
            if ($null -eq $app -or $app.Id -ne $baselineApp.Id -or
                $null -eq $hostEvidence -or $hostEvidence.Id -ne $baselineHost.Id) {
                throw "The App or TaskbarHost process identity changed during Explorer recovery."
            }

            $recovered = $newExplorer.Count -eq 1 -and
                $state.Counts.Observer -eq 1 -and
                $observer.Id -ne $baselineObserver.Id -and
                $state.Counts.Native -eq 1 -and
                $state.Counts.Floating -eq 0
            if (-not $recovered) {
                Start-Sleep -Milliseconds 100
            }
        } while (-not $recovered -and [DateTimeOffset]::UtcNow -lt $deadline)

        $result.RecoveryElapsedMilliseconds = $recoveryTimer.ElapsedMilliseconds
        if (-not $recovered) {
            throw "QuickPods did not recover within $RecoveryTimeoutSeconds seconds."
        }

        Start-Sleep -Seconds $SettlementSeconds
        $after = Get-GateState
        $afterApp = Get-NamedProcessEvidence -State $after -Name "QuickPods"
        $afterHost = Get-NamedProcessEvidence -State $after -Name "QuickPods.TaskbarHost"
        $afterObserver = Get-NamedProcessEvidence -State $after -Name "QuickPods.TaskbarObserver"
        $logEntries = @(Get-NewLogEntries)
        $warningOrError = @($logEntries | Where-Object { [int]$_.Level -ge 2 })
        $generationChanged = @(
            $logEntries | Where-Object {
                $_.EventName -eq "TaskbarObserverRetired" -and
                $_.Properties.Reason -eq "ExplorerGenerationChanged"
            }
        )
        $resourceDeltas = [pscustomobject][ordered]@{
            App = [pscustomobject][ordered]@{
                HandleCount = $afterApp.HandleCount - $baselineApp.HandleCount
                PrivateMemoryBytes = $afterApp.PrivateMemoryBytes - $baselineApp.PrivateMemoryBytes
                GdiObjectCount = $afterApp.GdiObjectCount - $baselineApp.GdiObjectCount
                UserObjectCount = $afterApp.UserObjectCount - $baselineApp.UserObjectCount
            }
            Host = [pscustomobject][ordered]@{
                HandleCount = $afterHost.HandleCount - $baselineHost.HandleCount
                PrivateMemoryBytes = $afterHost.PrivateMemoryBytes - $baselineHost.PrivateMemoryBytes
                GdiObjectCount = $afterHost.GdiObjectCount - $baselineHost.GdiObjectCount
                UserObjectCount = $afterHost.UserObjectCount - $baselineHost.UserObjectCount
            }
        }
        $assertions = [pscustomobject][ordered]@{
            AppIdentityRetained = $afterApp.Id -eq $baselineApp.Id
            HostIdentityRetained = $afterHost.Id -eq $baselineHost.Id
            OldObserverExited = -not (Get-Process -Id $baselineObserver.Id -ErrorAction SilentlyContinue)
            NewObserverCreated = $afterObserver.Id -ne $baselineObserver.Id
            ExactFinalTopology = $after.Counts.App -eq 1 -and
                $after.Counts.Host -eq 1 -and
                $after.Counts.Observer -eq 1 -and
                $after.Counts.Native -eq 1 -and
                $after.Counts.Floating -eq 0
            NoDuplicateTopologyObserved = $maximumCounts.App -le 1 -and
                $maximumCounts.Host -le 1 -and
                $maximumCounts.Observer -le 1 -and
                $maximumCounts.Native -le 1 -and
                $maximumCounts.Floating -eq 0
            RecoveredWithinTimeout = $result.RecoveryElapsedMilliseconds -le
                ($RecoveryTimeoutSeconds * 1000)
            AppGuiResourcesStable = $resourceDeltas.App.GdiObjectCount -le 0 -and
                $resourceDeltas.App.UserObjectCount -le 0
            HostGuiResourcesStable = $resourceDeltas.Host.GdiObjectCount -le 0 -and
                $resourceDeltas.Host.UserObjectCount -le 0
            ExplorerGenerationEventLogged = $generationChanged.Count -ge 1
            NoWarningOrErrorLogged = $warningOrError.Count -eq 0
        }

        $result.After = $after
        $result.ResourceDeltas = $resourceDeltas
        $result.LogEvidence = [pscustomobject][ordered]@{
            Path = $logPath
            EntryCount = $logEntries.Count
            ObserverGenerationChangedCount = $generationChanged.Count
            WarningOrErrorCount = $warningOrError.Count
            WarningOrErrorEvents = @(
                $warningOrError | Select-Object Timestamp,Level,EventName,Message,Properties
            )
        }
        $result.Assertions = $assertions
        if (@($assertions.PSObject.Properties.Value | Where-Object { $_ -ne $true }).Count -gt 0) {
            throw "One or more Explorer recovery assertions failed."
        }

        $result.Status = "Pass"
    }
} catch {
    $failure = $_
    $result.Status = "Fail"
    $result.Failure = $_.Exception.Message
} finally {
    if (-not $PreflightOnly -and @(Get-ExplorerProcess).Count -eq 0) {
        Start-Process -FilePath (Join-Path $env:WINDIR "explorer.exe")
    }

    $result.CompletedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    $outputDirectory = Split-Path -Parent $resolvedOutput
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    [pscustomobject]$result |
        ConvertTo-Json -Depth 12 |
        Set-Content -LiteralPath $resolvedOutput -Encoding utf8
}

if ($failure) {
    throw $failure
}

Get-Item -LiteralPath $resolvedOutput
