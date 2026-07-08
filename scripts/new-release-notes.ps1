param(
    [Parameter(Mandatory = $true)]
    [string] $Version,
    [Parameter(Mandatory = $true)]
    [string] $Tag,
    [Parameter(Mandatory = $true)]
    [string] $Repository,
    [Parameter(Mandatory = $true)]
    [string] $OutputPath
)

$ErrorActionPreference = "Stop"

function Assert-ReleaseVersion {
    param([string] $Value)

    $pattern = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?$'
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -notmatch $pattern) {
        throw "Release version '$Value' must be SemVer like 0.1.0 or 0.1.0-preview.1."
    }
}

function Get-PreviousReleaseTag {
    param([string] $CurrentTag)

    $tags = @(git tag --list "v*.*.*" --sort=-v:refname) |
        Where-Object { $_ -ne $CurrentTag -and $_ -match '^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?$' }

    if ($tags.Count -eq 0) {
        return $null
    }

    return $tags[0]
}

function Get-ReleaseChanges {
    param([string] $PreviousTag)

    $range = "HEAD"
    if (-not [string]::IsNullOrWhiteSpace($PreviousTag)) {
        $range = "$PreviousTag..HEAD"
    }

    $subjects = @(git log --no-merges --pretty=format:"%s (%h)" $range) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique

    if ($subjects.Count -eq 0) {
        $subjects = @(git log --pretty=format:"%s (%h)" $range) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Select-Object -Unique
    }

    return $subjects
}

function Format-RepositoryUrl {
    param([string] $RepositoryValue)

    if ($RepositoryValue -match '^https?://') {
        return $RepositoryValue.TrimEnd('/')
    }

    return "https://github.com/$RepositoryValue"
}

Assert-ReleaseVersion $Version

if ($Tag -ne "v$Version") {
    throw "Release tag '$Tag' must match version '$Version'."
}

$previousTag = Get-PreviousReleaseTag -CurrentTag $Tag
$changes = @(Get-ReleaseChanges -PreviousTag $previousTag)
$repositoryUrl = Format-RepositoryUrl $Repository
$compareUrl = if ([string]::IsNullOrWhiteSpace($previousTag)) {
    "$repositoryUrl/commits/$Tag"
}
else {
    "$repositoryUrl/compare/$previousTag...$Tag"
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("Unsigned Windows release of Mangosteen Image Viewer.")
$lines.Add("")
$lines.Add("## What's Changed")

if ($changes.Count -eq 0) {
    $lines.Add("- Rebuilt release artifacts from the current source tree.")
}
else {
    foreach ($change in $changes) {
        $lines.Add("- $change")
    }
}

$lines.Add("")
$lines.Add("## Downloads")
$lines.Add("- Installer: ``Mangosteen-Setup-$Version-x64.exe``")
$lines.Add("- Portable build: ``Mangosteen-Portable-$Version-x64.zip``")
$lines.Add("- Checksums: ``SHA256SUMS.txt``")
$lines.Add("")
$lines.Add("Verify the SHA256 checksum before running downloaded files. These early builds are unsigned, so Windows SmartScreen or Smart App Control may warn until the project has code signing and reputation in place.")
$lines.Add("")
$lines.Add("Full changelog: $compareUrl")

New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
Set-Content -LiteralPath $OutputPath -Value $lines -Encoding UTF8
Write-Host "Release notes: $OutputPath"
