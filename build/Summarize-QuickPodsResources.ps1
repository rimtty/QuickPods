[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InputPath,
    [ValidateRange(1, 604800)]
    [int]$MinimumDurationSeconds = 86400,
    [ValidateRange(1, 3600)]
    [int]$ExpectedSampleIntervalSeconds = 5,
    [ValidateRange(1, 3600)]
    [int]$MaximumSampleGapSeconds = 30,
    [ValidateRange(1, 50)]
    [int]$ComparisonWindowPercent = 10,
    [string]$OutputDirectory = "artifacts/metrics/report",
    [switch]$Preview
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

function Resolve-RepositoryOutputPath {
    param([Parameter(Mandatory)][string]$Path)

    $resolved = if ([System.IO.Path]::IsPathRooted($Path)) {
        [System.IO.Path]::GetFullPath($Path)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
    }
    $repositoryPrefix = $repositoryRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($repositoryPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "OutputDirectory must be inside the QuickPods repository."
    }

    return $resolved
}

function Get-Percentile {
    param(
        [Parameter(Mandatory)][double[]]$Values,
        [Parameter(Mandatory)][ValidateRange(0, 1)][double]$Percentile
    )

    if ($Values.Count -eq 0) {
        throw "A percentile cannot be calculated from an empty series."
    }

    [double[]]$sorted = $Values | Sort-Object
    if ($sorted.Count -eq 1) {
        return $sorted[0]
    }

    $position = ($sorted.Count - 1) * $Percentile
    $lower = [Math]::Floor($position)
    $upper = [Math]::Ceiling($position)
    if ($lower -eq $upper) {
        return $sorted[$lower]
    }

    $weight = $position - $lower
    return $sorted[$lower] + (($sorted[$upper] - $sorted[$lower]) * $weight)
}

function Get-SlopePerHour {
    param(
        [Parameter(Mandatory)][double[]]$ElapsedSeconds,
        [Parameter(Mandatory)][double[]]$Values
    )

    if ($Values.Count -lt 2 -or $ElapsedSeconds.Count -ne $Values.Count) {
        return 0.0
    }

    $meanX = ($ElapsedSeconds | Measure-Object -Average).Average
    $meanY = ($Values | Measure-Object -Average).Average
    $numerator = 0.0
    $denominator = 0.0
    for ($index = 0; $index -lt $Values.Count; $index++) {
        $xDelta = $ElapsedSeconds[$index] - $meanX
        $numerator += $xDelta * ($Values[$index] - $meanY)
        $denominator += $xDelta * $xDelta
    }

    if ($denominator -eq 0) {
        return 0.0
    }

    return ($numerator / $denominator) * 3600.0
}

function Get-MetricSummary {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][double[]]$Values,
        [Parameter(Mandatory)][double[]]$ElapsedSeconds,
        [Parameter(Mandatory)][int]$WindowSize
    )

    [double[]]$early = $Values | Select-Object -First $WindowSize
    [double[]]$late = $Values | Select-Object -Last $WindowSize
    $earlyMedian = Get-Percentile -Values $early -Percentile 0.5
    $lateMedian = Get-Percentile -Values $late -Percentile 0.5

    return [pscustomobject][ordered]@{
        Name = $Name
        First = $Values[0]
        Last = $Values[-1]
        Minimum = ($Values | Measure-Object -Minimum).Minimum
        Maximum = ($Values | Measure-Object -Maximum).Maximum
        Median = [Math]::Round((Get-Percentile -Values $Values -Percentile 0.5), 3)
        P95 = [Math]::Round((Get-Percentile -Values $Values -Percentile 0.95), 3)
        EarlyMedian = [Math]::Round($earlyMedian, 3)
        LateMedian = [Math]::Round($lateMedian, 3)
        MedianDelta = [Math]::Round(($lateMedian - $earlyMedian), 3)
        SlopePerHour = [Math]::Round((Get-SlopePerHour -ElapsedSeconds $ElapsedSeconds -Values $Values), 3)
    }
}

function Test-PotentialGrowth {
    param(
        [Parameter(Mandatory)]$Metric,
        [Parameter(Mandatory)][double]$AbsoluteTolerance,
        [Parameter(Mandatory)][double]$RelativeTolerance
    )

    $relativeThreshold = [Math]::Abs($Metric.EarlyMedian) * $RelativeTolerance
    $threshold = [Math]::Max($AbsoluteTolerance, $relativeThreshold)
    return $Metric.MedianDelta -gt $threshold -and $Metric.SlopePerHour -gt 0
}

$resolvedInput = if ([System.IO.Path]::IsPathRooted($InputPath)) {
    [System.IO.Path]::GetFullPath($InputPath)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $InputPath))
}
if (-not (Test-Path -LiteralPath $resolvedInput -PathType Leaf)) {
    throw "Input CSV was not found: $resolvedInput"
}

$requiredColumns = @(
    "TimestampUtc",
    "Sample",
    "ProcessCount",
    "CpuSeconds",
    "WorkingSetBytes",
    "PrivateMemoryBytes",
    "HandleCount",
    "GdiObjectCount",
    "UserObjectCount"
)
$rows = @(Import-Csv -LiteralPath $resolvedInput)
if ($rows.Count -lt 2) {
    throw "At least two resource samples are required."
}

$actualColumns = @($rows[0].PSObject.Properties.Name)
$missingColumns = @($requiredColumns | Where-Object { $_ -notin $actualColumns })
if ($missingColumns.Count -gt 0) {
    throw "Input CSV is missing required columns: $($missingColumns -join ', ')."
}

$typedRows = [System.Collections.Generic.List[object]]::new()
for ($index = 0; $index -lt $rows.Count; $index++) {
    $row = $rows[$index]
    $timestamp = [System.DateTimeOffset]::MinValue
    if (-not [System.DateTimeOffset]::TryParse(
            $row.TimestampUtc,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::RoundtripKind,
            [ref]$timestamp)) {
        throw "Sample $index has an invalid TimestampUtc value."
    }

    try {
        $sample = [int]::Parse($row.Sample, [System.Globalization.CultureInfo]::InvariantCulture)
        $processCount = [int]::Parse($row.ProcessCount, [System.Globalization.CultureInfo]::InvariantCulture)
        $cpuSeconds = [double]::Parse($row.CpuSeconds, [System.Globalization.CultureInfo]::InvariantCulture)
        $workingSetBytes = [double]::Parse($row.WorkingSetBytes, [System.Globalization.CultureInfo]::InvariantCulture)
        $privateMemoryBytes = [double]::Parse($row.PrivateMemoryBytes, [System.Globalization.CultureInfo]::InvariantCulture)
        $handleCount = [double]::Parse($row.HandleCount, [System.Globalization.CultureInfo]::InvariantCulture)
        $gdiObjectCount = [double]::Parse($row.GdiObjectCount, [System.Globalization.CultureInfo]::InvariantCulture)
        $userObjectCount = [double]::Parse($row.UserObjectCount, [System.Globalization.CultureInfo]::InvariantCulture)
    } catch {
        throw "Sample $index contains a non-numeric metric."
    }

    if ($sample -ne $index) {
        throw "Sample numbering must be contiguous from zero; row $index contains sample $sample."
    }
    if ($processCount -lt 1 -or $cpuSeconds -lt 0 -or $workingSetBytes -lt 0 -or
        $privateMemoryBytes -lt 0 -or $handleCount -lt 0 -or $gdiObjectCount -lt 0 -or
        $userObjectCount -lt 0) {
        throw "Sample $index contains an invalid negative metric or zero process count."
    }

    $typedRows.Add([pscustomobject][ordered]@{
        Timestamp = $timestamp
        Sample = $sample
        ProcessCount = $processCount
        CpuSeconds = $cpuSeconds
        WorkingSetBytes = $workingSetBytes
        PrivateMemoryBytes = $privateMemoryBytes
        HandleCount = $handleCount
        GdiObjectCount = $gdiObjectCount
        UserObjectCount = $userObjectCount
    })
}

$intervals = [System.Collections.Generic.List[double]]::new()
for ($index = 1; $index -lt $typedRows.Count; $index++) {
    $interval = ($typedRows[$index].Timestamp - $typedRows[$index - 1].Timestamp).TotalSeconds
    if ($interval -le 0) {
        throw "Timestamps must be strictly increasing; sample $index is out of order."
    }
    $intervals.Add($interval)
}

$elapsedSeconds = ($typedRows[-1].Timestamp - $typedRows[0].Timestamp).TotalSeconds
$durationToleranceSeconds = [Math]::Max(30, $ExpectedSampleIntervalSeconds * 3)
if (-not $Preview -and $elapsedSeconds -lt ($MinimumDurationSeconds - $durationToleranceSeconds)) {
    throw "Observation window was $([Math]::Round($elapsedSeconds, 3)) seconds; at least $MinimumDurationSeconds seconds (tolerance $durationToleranceSeconds seconds) is required."
}

$maximumObservedGap = ($intervals | Measure-Object -Maximum).Maximum
if ($maximumObservedGap -gt $MaximumSampleGapSeconds) {
    throw "Maximum sample gap was $([Math]::Round($maximumObservedGap, 3)) seconds; limit is $MaximumSampleGapSeconds seconds."
}

[double[]]$elapsedSeries = $typedRows | ForEach-Object {
    ($_.Timestamp - $typedRows[0].Timestamp).TotalSeconds
}
$windowSize = [Math]::Max(1, [Math]::Ceiling($typedRows.Count * ($ComparisonWindowPercent / 100.0)))

$metrics = [ordered]@{}
foreach ($metricName in @(
        "ProcessCount",
        "WorkingSetBytes",
        "PrivateMemoryBytes",
        "HandleCount",
        "GdiObjectCount",
        "UserObjectCount")) {
    [double[]]$metricValues = $typedRows | ForEach-Object { [double]($_.$metricName) }
    $metrics[$metricName] = Get-MetricSummary `
        -Name $metricName `
        -Values $metricValues `
        -ElapsedSeconds $elapsedSeries `
        -WindowSize $windowSize
}

$cpuDeltaSeconds = $typedRows[-1].CpuSeconds - $typedRows[0].CpuSeconds
if ($cpuDeltaSeconds -lt 0) {
    throw "CpuSeconds decreased during the observation window."
}
$singleCoreCpuPercent = if ($elapsedSeconds -gt 0) {
    ($cpuDeltaSeconds / $elapsedSeconds) * 100.0
} else {
    0.0
}
$logicalProcessorCount = [Environment]::ProcessorCount
$machineCpuPercent = $singleCoreCpuPercent / $logicalProcessorCount

$growthSignals = [System.Collections.Generic.List[string]]::new()
if (Test-PotentialGrowth $metrics.PrivateMemoryBytes (8MB) 0.10) {
    $growthSignals.Add("PrivateMemoryBytes")
}
if (Test-PotentialGrowth $metrics.WorkingSetBytes (16MB) 0.10) {
    $growthSignals.Add("WorkingSetBytes")
}
if (Test-PotentialGrowth $metrics.HandleCount 10 0.02) {
    $growthSignals.Add("HandleCount")
}
if (Test-PotentialGrowth $metrics.GdiObjectCount 2 0.05) {
    $growthSignals.Add("GdiObjectCount")
}
if (Test-PotentialGrowth $metrics.UserObjectCount 2 0.05) {
    $growthSignals.Add("UserObjectCount")
}

$result = [pscustomobject][ordered]@{
    SchemaVersion = 1
    GeneratedAtUtc = [System.DateTimeOffset]::UtcNow.ToString("O")
    InputFile = $resolvedInput
    Mode = if ($Preview) { "Preview" } else { "Final" }
    CaptureIntegrity = if ($Preview) { "Preview" } else { "Pass" }
    Ac025Decision = "ReviewRequired"
    SampleCount = $typedRows.Count
    StartedAtUtc = $typedRows[0].Timestamp.ToString("O")
    EndedAtUtc = $typedRows[-1].Timestamp.ToString("O")
    ElapsedSeconds = [Math]::Round($elapsedSeconds, 3)
    MinimumDurationSeconds = $MinimumDurationSeconds
    Cadence = [pscustomobject][ordered]@{
        ExpectedSeconds = $ExpectedSampleIntervalSeconds
        MinimumSeconds = [Math]::Round((($intervals | Measure-Object -Minimum).Minimum), 3)
        AverageSeconds = [Math]::Round((($intervals | Measure-Object -Average).Average), 3)
        P95Seconds = [Math]::Round((Get-Percentile -Values $intervals.ToArray() -Percentile 0.95), 3)
        MaximumSeconds = [Math]::Round($maximumObservedGap, 3)
        MaximumAllowedSeconds = $MaximumSampleGapSeconds
    }
    Cpu = [pscustomobject][ordered]@{
        DeltaSeconds = [Math]::Round($cpuDeltaSeconds, 3)
        SingleCorePercent = [Math]::Round($singleCoreCpuPercent, 3)
        MachineNormalizedPercent = [Math]::Round($machineCpuPercent, 3)
        LogicalProcessorCount = $logicalProcessorCount
        RequirementPercent = 1.0
        MeetsMachineNormalizedRequirement = $machineCpuPercent -lt 1.0
    }
    ResidentMemoryTargetBytes = 150MB
    ResidentMemoryTargetIsAdvisory = $true
    PotentialGrowthSignals = $growthSignals.ToArray()
    Metrics = [pscustomobject]$metrics
    Notes = @(
        "CaptureIntegrity only validates the series shape, observation window, and cadence.",
        "PotentialGrowthSignals use robust early/late medians plus a positive least-squares slope; they are review aids, not an automatic leak verdict.",
        "Summed Working Set may double-count shared self-contained .NET pages. Review its time trend together with Private Memory.",
        "AC-025 remains ReviewRequired until the final series, application logs, Explorer repetition, and Bluetooth repetition are reviewed together."
    )
}

$resolvedOutputDirectory = Resolve-RepositoryOutputPath $OutputDirectory
New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
$jsonPath = Join-Path $resolvedOutputDirectory "quickpods-resource-report.json"
$markdownPath = Join-Path $resolvedOutputDirectory "quickpods-resource-report.md"
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding utf8

function Format-Bytes {
    param([double]$Value)
    return "{0:N2} MB" -f ($Value / 1MB)
}

$markdown = [System.Collections.Generic.List[string]]::new()
$markdown.Add("# QuickPods resource observation report")
$markdown.Add("")
$markdown.Add("- Mode: ``$($result.Mode)``")
$markdown.Add("- Capture integrity: ``$($result.CaptureIntegrity)``")
$markdown.Add("- AC-025 decision: ``$($result.Ac025Decision)``")
$markdown.Add("- Samples: $($result.SampleCount)")
$markdown.Add("- Observation: $($result.StartedAtUtc) to $($result.EndedAtUtc) ($([Math]::Round($elapsedSeconds / 3600, 3)) hours)")
$markdown.Add("- Cadence: avg $($result.Cadence.AverageSeconds)s, p95 $($result.Cadence.P95Seconds)s, max $($result.Cadence.MaximumSeconds)s")
$markdown.Add("- CPU: $($result.Cpu.SingleCorePercent)% of one core; $($result.Cpu.MachineNormalizedPercent)% machine-normalized")
$markdown.Add("")
$markdown.Add("| Metric | First | Last | Min | Max | Early median | Late median | Median delta | Slope/hour |")
$markdown.Add("|---|---:|---:|---:|---:|---:|---:|---:|---:|")
foreach ($metricName in $metrics.Keys) {
    $metric = $metrics[$metricName]
    $isBytes = $metricName.EndsWith("Bytes", [System.StringComparison]::Ordinal)
    $displayValues = if ($isBytes) {
        $first = Format-Bytes -Value ([double]$metric.First)
        $last = Format-Bytes -Value ([double]$metric.Last)
        $minimum = Format-Bytes -Value ([double]$metric.Minimum)
        $maximum = Format-Bytes -Value ([double]$metric.Maximum)
        $earlyMedian = Format-Bytes -Value ([double]$metric.EarlyMedian)
        $lateMedian = Format-Bytes -Value ([double]$metric.LateMedian)
        $medianDelta = Format-Bytes -Value ([double]$metric.MedianDelta)
        $slope = (Format-Bytes -Value ([double]$metric.SlopePerHour)) + "/h"
        @(
            $first,
            $last,
            $minimum,
            $maximum,
            $earlyMedian,
            $lateMedian,
            $medianDelta,
            $slope
        )
    } else {
        @(
            $metric.First,
            $metric.Last,
            $metric.Minimum,
            $metric.Maximum,
            $metric.EarlyMedian,
            $metric.LateMedian,
            $metric.MedianDelta,
            $metric.SlopePerHour
        )
    }
    $markdown.Add("| $metricName | $($displayValues -join ' | ') |")
}
$markdown.Add("")
$signals = if ($growthSignals.Count -eq 0) { "none" } else { $growthSignals -join ", " }
$markdown.Add("Potential growth signals: **$signals**")
$markdown.Add("")
$markdown.Add("The 150 MB summed resident-memory value is an advisory target. Summed Working Set can double-count shared self-contained .NET pages, so review its time trend together with Private Memory.")
$markdown.Add("")
$markdown.Add("This report does not automatically pass AC-025. The final series, application logs, Explorer repetition, and Bluetooth repetition require a joint review.")
$markdown | Set-Content -LiteralPath $markdownPath -Encoding utf8

[pscustomobject][ordered]@{
    JsonReport = $jsonPath
    MarkdownReport = $markdownPath
    CaptureIntegrity = $result.CaptureIntegrity
    Ac025Decision = $result.Ac025Decision
    PotentialGrowthSignals = $signals
}
