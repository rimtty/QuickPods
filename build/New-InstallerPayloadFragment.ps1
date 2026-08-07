[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PayloadDirectory,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
$payloadRoot = [System.IO.Path]::GetFullPath($PayloadDirectory)
$fragmentPath = [System.IO.Path]::GetFullPath($OutputPath)

if (-not (Test-Path -LiteralPath $payloadRoot -PathType Container)) {
    throw "Payload directory does not exist: $payloadRoot"
}

$selfContainedProof = & (Join-Path $PSScriptRoot "Test-SelfContainedPayload.ps1") `
    -PayloadDirectory $payloadRoot
$requiredFiles = @($selfContainedProof.Executables) +
    @($selfContainedProof.RuntimeFiles) +
    @($selfContainedProof.RuntimeConfigs) +
    @("artifact-manifest.json")
$componentIds = [System.Collections.Generic.List[string]]::new()
foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $payloadRoot $requiredFile) -PathType Leaf)) {
        throw "Installer payload is missing $requiredFile."
    }
}

function Get-StableToken {
    param([Parameter(Mandatory = $true)][string]$Value)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value.Replace("\", "/").ToLowerInvariant())
    $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
    return [Convert]::ToHexString($hash).Substring(0, 24)
}

function Get-StableGuid {
    param([Parameter(Mandatory = $true)][string]$Value)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes("QuickPods.Setup.Component|$Value")
    $hex = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).Substring(0, 32)
    return "$($hex.Substring(0, 8))-$($hex.Substring(8, 4))-$($hex.Substring(12, 4))-$($hex.Substring(16, 4))-$($hex.Substring(20, 12))"
}

function Add-DirectoryContent {
    param(
        [Parameter(Mandatory = $true)][System.Xml.XmlWriter]$Writer,
        [Parameter(Mandatory = $true)][System.IO.DirectoryInfo]$Directory,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$RelativeDirectory
    )

    $files = @(Get-ChildItem -LiteralPath $Directory.FullName -File | Sort-Object Name)
    if ($files.Count -gt 0) {
        $componentTokenSource = $RelativeDirectory + "|" + (($files.Name) -join "|")
        $componentToken = Get-StableToken $componentTokenSource

        $Writer.WriteStartElement("Component")
        $componentId = "Payload_$componentToken"
        $componentIds.Add($componentId)
        $Writer.WriteAttributeString("Id", $componentId)
        $Writer.WriteAttributeString("Guid", (Get-StableGuid $componentTokenSource))

        foreach ($file in $files) {
            $relativePath = [System.IO.Path]::GetRelativePath($payloadRoot, $file.FullName).Replace("\", "/")
            $fileToken = Get-StableToken $relativePath
            $Writer.WriteStartElement("File")
            $Writer.WriteAttributeString("Id", "File_$fileToken")
            $Writer.WriteAttributeString("Source", $file.FullName)
            $Writer.WriteAttributeString("Name", $file.Name)
            $Writer.WriteEndElement()
        }

        $Writer.WriteStartElement("RegistryValue")
        $Writer.WriteAttributeString("Root", "HKCU")
        $Writer.WriteAttributeString("Key", "Software\QuickPods\Installer\Components")
        $Writer.WriteAttributeString("Name", $componentToken)
        $Writer.WriteAttributeString("Type", "integer")
        $Writer.WriteAttributeString("Value", "1")
        $Writer.WriteAttributeString("KeyPath", "yes")
        $Writer.WriteEndElement()
        $Writer.WriteEndElement()
    }

    foreach ($childDirectory in Get-ChildItem -LiteralPath $Directory.FullName -Directory | Sort-Object Name) {
        $childRelative = if ([string]::IsNullOrEmpty($RelativeDirectory)) {
            $childDirectory.Name
        } else {
            "$RelativeDirectory/$($childDirectory.Name)"
        }
        $directoryToken = Get-StableToken $childRelative
        $Writer.WriteStartElement("Directory")
        $Writer.WriteAttributeString("Id", "Directory_$directoryToken")
        $Writer.WriteAttributeString("Name", $childDirectory.Name)
        Add-DirectoryContent -Writer $Writer -Directory $childDirectory -RelativeDirectory $childRelative
        $Writer.WriteEndElement()
    }
}

$parentDirectory = Split-Path -Parent $fragmentPath
New-Item -ItemType Directory -Path $parentDirectory -Force | Out-Null

$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Encoding = [System.Text.UTF8Encoding]::new($false)
$settings.Indent = $true
$settings.NewLineChars = "`n"
$settings.NewLineHandling = [System.Xml.NewLineHandling]::Replace

$writer = [System.Xml.XmlWriter]::Create($fragmentPath, $settings)
try {
    $writer.WriteStartDocument()
    $writer.WriteStartElement("Wix", "http://wixtoolset.org/schemas/v4/wxs")
    $writer.WriteStartElement("Fragment")
    $writer.WriteStartElement("DirectoryRef")
    $writer.WriteAttributeString("Id", "INSTALLFOLDER")
    Add-DirectoryContent `
        -Writer $writer `
        -Directory ([System.IO.DirectoryInfo]::new($payloadRoot)) `
        -RelativeDirectory ""
    $writer.WriteEndElement()

    $writer.WriteStartElement("ComponentGroup")
    $writer.WriteAttributeString("Id", "QuickPodsPayloadComponents")
    foreach ($componentId in $componentIds) {
        $writer.WriteStartElement("ComponentRef")
        $writer.WriteAttributeString("Id", $componentId)
        $writer.WriteEndElement()
    }
    $writer.WriteEndElement()
    $writer.WriteEndElement()
    $writer.WriteEndElement()
    $writer.WriteEndDocument()
} finally {
    $writer.Dispose()
}

[pscustomobject]@{
    PayloadDirectory = $payloadRoot
    OutputPath = $fragmentPath
    FileCount = @(Get-ChildItem -LiteralPath $payloadRoot -File -Recurse).Count
}
