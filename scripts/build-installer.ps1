param(
    [string] $Version,
    [string] $Configuration = "Release",
    [ValidateSet("win-x64")]
    [string] $Runtime = "win-x64",
    [string] $InnoCompilerPath,
    [switch] $FrameworkDependent,
    [switch] $NoRestore,
    [switch] $DisableNuGetAudit,
    [switch] $SkipInstaller
)

$ErrorActionPreference = "Stop"

function Assert-ReleaseVersion {
    param([string] $Value)

    $pattern = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?$'
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -notmatch $pattern) {
        throw "Release version '$Value' must be SemVer like 0.1.0 or 0.1.0-preview.1."
    }
}

function Get-ProjectVersion {
    param([string] $ProjectPath)

    [xml] $project = Get-Content -LiteralPath $ProjectPath
    foreach ($group in @($project.Project.PropertyGroup)) {
        if (-not [string]::IsNullOrWhiteSpace($group.Version)) {
            return [string] $group.Version
        }
    }

    return "0.1.0"
}

function Find-InnoCompiler {
    param([string] $ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (Test-Path -LiteralPath $ExplicitPath) {
            return (Resolve-Path -LiteralPath $ExplicitPath).Path
        }

        throw "Inno compiler not found at '$ExplicitPath'."
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $roots = @(${env:ProgramFiles}, ${env:ProgramFiles(x86)}, ${env:LOCALAPPDATA}) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    foreach ($root in $roots) {
        foreach ($relativePath in @("Inno Setup 6\ISCC.exe", "Programs\Inno Setup 6\ISCC.exe")) {
            $candidate = Join-Path $root $relativePath
            if (Test-Path -LiteralPath $candidate) {
                return $candidate
            }
        }
    }

    throw "ISCC.exe was not found. Install Inno Setup 6, then rerun this script."
}

function Assert-NativeCommandSucceeded {
    param([string] $CommandName)

    if ($LASTEXITCODE -ne 0) {
        throw "$CommandName failed with exit code $LASTEXITCODE."
    }
}

function Clear-GeneratedDirectory {
    param(
        [string] $Path,
        [string] $AllowedRoot
    )

    New-Item -ItemType Directory -Path $Path -Force | Out-Null

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    $resolvedRoot = (Resolve-Path -LiteralPath $AllowedRoot).Path
    if (-not $resolvedPath.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear generated directory outside project root: $resolvedPath"
    }

    Get-ChildItem -LiteralPath $resolvedPath -Force | Remove-Item -Recurse -Force
}

function Write-Sha256Checksums {
    param([string] $Directory)

    $checksumPath = Join-Path $Directory "SHA256SUMS.txt"
    $lines = Get-ChildItem -LiteralPath $Directory -File |
        Where-Object { $_.Name -ne "SHA256SUMS.txt" } |
        Sort-Object Name |
        ForEach-Object {
            $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
            "$($hash.Hash.ToLowerInvariant())  $($_.Name)"
        }

    Set-Content -LiteralPath $checksumPath -Value $lines -Encoding ascii
    Write-Host "Checksums: $checksumPath"
}

$projectRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $projectRoot "src\Mangosteen\Mangosteen.csproj"
$issPath = Join-Path $projectRoot "packaging\inno\Mangosteen.iss"
$distDir = Join-Path $projectRoot "dist"
$installerInputDir = Join-Path $projectRoot "publish\installer-input"

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-ProjectVersion $projectPath
}

Assert-ReleaseVersion $Version

Clear-GeneratedDirectory -Path $distDir -AllowedRoot $projectRoot
Clear-GeneratedDirectory -Path $installerInputDir -AllowedRoot $projectRoot

$publishArgs = @(
    "publish",
    $projectPath,
    "-c",
    $Configuration,
    "-r",
    $Runtime,
    "-p:PublishSingleFile=false",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:SatelliteResourceLanguages=en%3Bnb-NO%3Bde%3Bfr%3Bes%3Bpt-BR%3Bpl%3Btr%3Bja%3Bko%3Bzh-Hans%3Bzh-Hant%3Bru",
    "-o",
    $installerInputDir
)

if ($DisableNuGetAudit) {
    $publishArgs += "-p:NuGetAudit=false"
}

if ($NoRestore) {
    $publishArgs += "--no-restore"
}

if ($FrameworkDependent) {
    $publishArgs += "--self-contained"
    $publishArgs += "false"
}
else {
    $publishArgs += "--self-contained"
    $publishArgs += "true"
}

Write-Host "Publishing Mangosteen $Version for $Runtime..."
& dotnet @publishArgs
Assert-NativeCommandSucceeded "dotnet publish"

Get-ChildItem -LiteralPath $installerInputDir -Filter "*.pdb" -Recurse -Force | Remove-Item -Force

$portableZip = Join-Path $distDir "Mangosteen-Portable-$Version-x64.zip"
if (Test-Path -LiteralPath $portableZip) {
    Remove-Item -LiteralPath $portableZip -Force
}

Write-Host "Creating portable zip..."
Compress-Archive -Path (Join-Path $installerInputDir "*") -DestinationPath $portableZip -Force

if ($SkipInstaller) {
    Write-Host "Skipping installer compile."
    Write-Host "Portable zip: $portableZip"
    Write-Sha256Checksums -Directory $distDir
    return
}

$iscc = Find-InnoCompiler $InnoCompilerPath
$installerArgs = @(
    "/DAppVersion=$Version",
    "/DSourceDir=$installerInputDir",
    "/DOutputDir=$distDir",
    $issPath
)

Write-Host "Compiling installer with $iscc..."
& $iscc @installerArgs
Assert-NativeCommandSucceeded "Inno Setup compiler"

$installerPath = Join-Path $distDir "Mangosteen-Setup-$Version-x64.exe"
Write-Host "Installer: $installerPath"
Write-Host "Portable zip: $portableZip"
Write-Sha256Checksums -Directory $distDir
