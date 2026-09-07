[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PayloadDirectory
)

$ErrorActionPreference = "Stop"
$payloadRoot = [System.IO.Path]::GetFullPath($PayloadDirectory)
if (-not (Test-Path -LiteralPath $payloadRoot -PathType Container)) {
    throw "Payload directory does not exist: $payloadRoot"
}

$requiredExecutables = @(
    "QuickPods.exe",
    "QuickPods.TaskbarHost.exe",
    "QuickPods.TaskbarObserver.exe",
    "QuickPods.BluetoothWorker.exe"
)
$requiredManagedAssemblies = @(
    "QuickPods.dll",
    "QuickPods.TaskbarHost.dll",
    "QuickPods.TaskbarObserver.dll",
    "QuickPods.BluetoothWorker.dll"
)
$requiredDependencyManifests = @(
    "QuickPods.deps.json",
    "QuickPods.TaskbarHost.deps.json",
    "QuickPods.TaskbarObserver.deps.json",
    "QuickPods.BluetoothWorker.deps.json"
)
$requiredRuntimeFiles = @(
    "hostfxr.dll",
    "hostpolicy.dll",
    "coreclr.dll",
    "PresentationFramework.dll"
)
$runtimeConfigExpectations = [ordered]@{
    "QuickPods.runtimeconfig.json" = @(
        "Microsoft.NETCore.App",
        "Microsoft.WindowsDesktop.App"
    )
    "QuickPods.TaskbarHost.runtimeconfig.json" = @(
        "Microsoft.NETCore.App",
        "Microsoft.WindowsDesktop.App"
    )
    "QuickPods.TaskbarObserver.runtimeconfig.json" = @(
        "Microsoft.NETCore.App"
    )
    "QuickPods.BluetoothWorker.runtimeconfig.json" = @(
        "Microsoft.NETCore.App"
    )
}

$requiredFiles = @(
    $requiredExecutables
    $requiredManagedAssemblies
    $requiredDependencyManifests
    $requiredRuntimeFiles
    $runtimeConfigExpectations.Keys
)
foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $payloadRoot $requiredFile) -PathType Leaf)) {
        throw "Self-contained payload is missing $requiredFile."
    }
}

$includedFrameworkVersions = [ordered]@{}
foreach ($expectation in $runtimeConfigExpectations.GetEnumerator()) {
    $runtimeConfigPath = Join-Path $payloadRoot $expectation.Key
    try {
        $runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
    } catch {
        throw "Runtime config is invalid JSON: $($expectation.Key)"
    }

    if ($null -eq $runtimeConfig.runtimeOptions) {
        throw "Runtime config has no runtimeOptions: $($expectation.Key)"
    }

    $externalFrameworks = @()
    foreach ($externalPropertyName in @("framework", "frameworks")) {
        $externalFrameworkProperty =
            $runtimeConfig.runtimeOptions.PSObject.Properties[$externalPropertyName]
        if ($null -ne $externalFrameworkProperty) {
            $externalFrameworks += @($externalFrameworkProperty.Value)
        }
    }
    if ($externalFrameworks.Count -ne 0) {
        throw "Runtime config requires an external .NET runtime: $($expectation.Key)"
    }

    $includedFrameworks = @($runtimeConfig.runtimeOptions.includedFrameworks)
    if ($includedFrameworks.Count -eq 0) {
        throw "Runtime config has no included frameworks: $($expectation.Key)"
    }

    foreach ($includedFramework in $includedFrameworks) {
        if ([string]::IsNullOrWhiteSpace($includedFramework.name) -or
            [string]::IsNullOrWhiteSpace($includedFramework.version)) {
            throw "Runtime config has incomplete included-framework metadata: $($expectation.Key)"
        }

        if ($includedFrameworkVersions.Contains($includedFramework.name) -and
            $includedFrameworkVersions[$includedFramework.name] -ne $includedFramework.version) {
            throw "Included framework versions disagree across runtime configs: $($includedFramework.name)"
        }
        $includedFrameworkVersions[$includedFramework.name] = $includedFramework.version
    }

    $includedNames = @($includedFrameworks | ForEach-Object { $_.name })
    foreach ($expectedFramework in $expectation.Value) {
        if ($includedNames -notcontains $expectedFramework) {
            throw "Runtime config does not include $($expectedFramework): $($expectation.Key)"
        }
    }
}

[pscustomobject]@{
    PayloadDirectory = $payloadRoot
    DependencyMode = "selfContained"
    Executables = $requiredExecutables
    ManagedAssemblies = $requiredManagedAssemblies
    DependencyManifests = $requiredDependencyManifests
    RuntimeFiles = $requiredRuntimeFiles
    RuntimeConfigs = @($runtimeConfigExpectations.Keys)
    IncludedFrameworks = [pscustomobject]$includedFrameworkVersions
}
