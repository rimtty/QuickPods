[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar

Push-Location $repositoryRoot
try {
    $trackedFiles = @(
        & git -c core.quotepath=false ls-files --cached --others --exclude-standard |
            Where-Object { Test-Path -LiteralPath (Join-Path $repositoryRoot $_) -PathType Leaf }
    )
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to enumerate tracked files."
    }

    $problems = [System.Collections.Generic.List[string]]::new()
    $forbiddenPathPatterns = [ordered]@{
        "generated build output" = '(^|/)(bin|obj|artifacts|TestResults|coverage|publish)(/|$)'
        "signing material" = '(?i)\.(pfx|p12|pem|key|snk)$'
        "environment file" = '(^|/)\.env(?:\.|$)'
        "internal handoff documentation" = '^docs/handoff/'
        "raw validation evidence" = '^docs/validation/'
    }

    foreach ($trackedFile in $trackedFiles) {
        foreach ($entry in $forbiddenPathPatterns.GetEnumerator()) {
            if ($trackedFile -match $entry.Value) {
                $problems.Add("${trackedFile}: tracked $($entry.Key)")
            }
        }
    }

    $textExtensions = @(
        ".cs", ".csproj", ".json", ".md", ".props", ".ps1", ".sln",
        ".txt", ".vbs", ".wixproj", ".wxs", ".xml", ".yaml", ".yml"
    )
    $contentPatterns = [ordered]@{
        "private key material" = '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'
        "GitHub token" = '(?i)(?:ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})'
        "AWS access key" = 'AKIA[0-9A-Z]{16}'
        "user profile path" = '(?i)[A-Z]:\\Users\\[^\\\s"'']+'
        "private development workspace" = '(?i)(?:D:\\QuickPods|E:\\tool\\QuickPods)'
        "temporary Codex clipboard file" = '(?i)codex-clipboard-[0-9a-f-]+'
        "Windows SID" = 'S-1-5-21-(?:[0-9]+-){3}[0-9]+'
        "MAC address" = '(?i)(?<![0-9A-F])(?:[0-9A-F]{2}[:-]){5}[0-9A-F]{2}(?![0-9A-F])'
    }

    foreach ($trackedFile in $trackedFiles) {
        $extension = [System.IO.Path]::GetExtension($trackedFile)
        if ($extension -notin $textExtensions) {
            continue
        }

        $fullPath = Join-Path $repositoryRoot $trackedFile
        $content = [System.IO.File]::ReadAllText($fullPath)
        foreach ($entry in $contentPatterns.GetEnumerator()) {
            if ($content -match $entry.Value) {
                $problems.Add("${trackedFile}: contains $($entry.Key)")
            }
        }
    }

    $markdownFiles = @($trackedFiles | Where-Object {
        [System.IO.Path]::GetExtension($_) -eq ".md"
    })
    $markdownLinkPattern = '!?(?:\[[^\]]*\])\((?<target><[^>]+>|[^)\s]+)(?:\s+"[^"]*")?\)'
    $htmlSourcePattern = '(?i)<(?:img|a)\b[^>]*(?:src|href)="(?<target>[^"]+)"'

    foreach ($markdownFile in $markdownFiles) {
        $fullPath = Join-Path $repositoryRoot $markdownFile
        $content = [System.IO.File]::ReadAllText($fullPath)
        $targets = @(
            [regex]::Matches($content, $markdownLinkPattern) |
                ForEach-Object { $_.Groups["target"].Value.Trim('<', '>') }
            [regex]::Matches($content, $htmlSourcePattern) |
                ForEach-Object { $_.Groups["target"].Value }
        )

        foreach ($target in $targets) {
            if ([string]::IsNullOrWhiteSpace($target) -or
                $target.StartsWith("#") -or
                $target -match '^(?i)(?:https?|mailto):') {
                continue
            }

            $pathPart = ($target -split '[?#]', 2)[0]
            $pathPart = [Uri]::UnescapeDataString($pathPart).Replace('/', [IO.Path]::DirectorySeparatorChar)
            $baseDirectory = Split-Path -Parent $fullPath
            $candidate = [IO.Path]::GetFullPath((Join-Path $baseDirectory $pathPart))
            if ($candidate -ne $repositoryRoot -and
                -not $candidate.StartsWith(
                    $repositoryPrefix,
                    [StringComparison]::OrdinalIgnoreCase)) {
                $problems.Add("${markdownFile}: link escapes the repository: $target")
                continue
            }

            if (-not (Test-Path -LiteralPath $candidate)) {
                $problems.Add("${markdownFile}: broken relative link: $target")
            }
        }
    }

    if ($problems.Count -gt 0) {
        $message = "Repository public-readiness validation failed:`n - " +
            (($problems | Sort-Object -Unique) -join "`n - ")
        throw $message
    }

    [pscustomobject]@{
        TrackedFiles = $trackedFiles.Count
        MarkdownFiles = $markdownFiles.Count
        Result = "Pass"
    }
} finally {
    Pop-Location
}
