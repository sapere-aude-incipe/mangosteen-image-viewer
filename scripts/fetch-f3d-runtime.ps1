param(
    [string] $Version = "3.5.0",
    [string] $DestinationRoot
)

$ErrorActionPreference = "Stop"

$knownReleases = @{
    "3.5.0" = @{
        FileName = "F3D-3.5.0-Windows-x86_64.zip"
        Sha256 = "db57f9fb7e1bbe2c022ec19dab3fd1eb38545f8c7b3d29d3906a951936a2e897"
    }
}

if (-not $knownReleases.ContainsKey($Version)) {
    throw "F3D version '$Version' is not pinned by this repository."
}

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($DestinationRoot)) {
    $DestinationRoot = Join-Path $projectRoot "artifacts\f3d-sdk"
}

$release = $knownReleases[$Version]
$archivePath = Join-Path $DestinationRoot $release.FileName
$extractRoot = Join-Path $DestinationRoot "extracted"
$runtimeRoot = Join-Path $extractRoot "F3D-$Version-Windows-x86_64"
$runtimeDll = Join-Path $runtimeRoot "bin\f3d_c_api.dll"

New-Item -ItemType Directory -Path $DestinationRoot -Force | Out-Null
if (-not (Test-Path -LiteralPath $archivePath)) {
    $url = "https://github.com/f3d-app/f3d/releases/download/v$Version/$($release.FileName)"
    Write-Host "Downloading pinned F3D $Version runtime..."
    Invoke-WebRequest -Uri $url -OutFile $archivePath
}

$actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $release.Sha256) {
    throw "F3D archive checksum mismatch. Expected $($release.Sha256), got $actualHash."
}

if (Test-Path -LiteralPath $extractRoot) {
    $resolvedDestination = [System.IO.Path]::GetFullPath($DestinationRoot)
    $resolvedExtractRoot = [System.IO.Path]::GetFullPath($extractRoot)
    if (-not $resolvedExtractRoot.StartsWith(
        $resolvedDestination + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace an F3D extraction directory outside the destination root."
    }

    Remove-Item -LiteralPath $resolvedExtractRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null
Expand-Archive -LiteralPath $archivePath -DestinationPath $extractRoot -Force
if (-not (Test-Path -LiteralPath $runtimeDll)) {
    throw "The verified F3D archive did not contain the expected runtime."
}

Write-Output $runtimeRoot
