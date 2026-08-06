[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
Push-Location $repositoryRoot
try {
    $json = & dotnet list QuickPods.sln package `
        --vulnerable `
        --include-transitive `
        --format json `
        --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet vulnerability audit failed to execute."
    }

    $report = $json | ConvertFrom-Json
    $vulnerable = @(
        foreach ($project in $report.projects) {
            foreach ($framework in @($project.frameworks)) {
                foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                    if ($null -ne $package -and
                        @($package.vulnerabilities | Where-Object { $null -ne $_ }).Count -gt 0) {
                        [pscustomobject]@{
                            Project = $project.path
                            Package = $package.id
                            Version = $package.resolvedVersion
                            Severity = (@($package.vulnerabilities) | ForEach-Object severity) -join ","
                        }
                    }
                }
            }
        }
    )
    if ($vulnerable.Count -gt 0) {
        $vulnerable | Format-Table -AutoSize | Out-String | Write-Error
        throw "$($vulnerable.Count) vulnerable NuGet package entries were found."
    }

    [pscustomobject]@{
        ProjectsAudited = @($report.projects).Count
        VulnerablePackageEntries = 0
    }
} finally {
    Pop-Location
}
