param(
    [Parameter(Mandatory = $true)]
    [string] $InstallerPath,
    [string] $InstallDirectory,
    [string] $AllowedRoot = [System.IO.Path]::GetTempPath()
)

$ErrorActionPreference = "Stop"

function Invoke-HiddenProcess {
    param(
        [string] $FilePath,
        [string[]] $Arguments
    )

    $process = Start-Process `
        -FilePath $FilePath `
        -ArgumentList $Arguments `
        -Wait `
        -PassThru `
        -WindowStyle Hidden
    if ($process.ExitCode -ne 0) {
        throw "'$FilePath' failed with exit code $($process.ExitCode)."
    }
}

function Get-RegistryValueOrNull {
    param(
        [string] $Path,
        [string] $Name
    )

    try {
        return Get-ItemPropertyValue -LiteralPath $Path -Name $Name -ErrorAction Stop
    }
    catch [System.Management.Automation.ItemNotFoundException] {
        return $null
    }
    catch [System.Management.Automation.PSArgumentException] {
        return $null
    }
}

$resolvedInstaller = (Resolve-Path -LiteralPath $InstallerPath).Path
$resolvedAllowedRoot = [System.IO.Path]::GetFullPath($AllowedRoot).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
    $InstallDirectory = Join-Path $resolvedAllowedRoot "MangosteenInstallerSmoke"
}

$resolvedInstallDirectory = [System.IO.Path]::GetFullPath($InstallDirectory)
$allowedPrefix = $resolvedAllowedRoot + [System.IO.Path]::DirectorySeparatorChar
if (-not $resolvedInstallDirectory.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Installer smoke-test directory must remain under the allowed root: $resolvedAllowedRoot"
}

if (Test-Path -LiteralPath $resolvedInstallDirectory) {
    Remove-Item -LiteralPath $resolvedInstallDirectory -Recurse -Force
}

$installedExe = Join-Path $resolvedInstallDirectory "Mangosteen.exe"
$uninstaller = Join-Path $resolvedInstallDirectory "unins000.exe"
$gpuManifest = Join-Path $resolvedInstallDirectory "components\gpu-large-images\component.json"
$modelManifest = Join-Path $resolvedInstallDirectory "components\model-viewer\component.json"
$f3dApi = Join-Path $resolvedInstallDirectory "components\model-viewer\bin\f3d_c_api.dll"
$registeredApplicationsPath = "HKCU:\Software\RegisteredApplications"
$capabilitiesPath = "HKCU:\Software\Classes\Applications\Mangosteen.exe\Capabilities\FileAssociations"
$uninstallPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{5505BFA7-AFF8-4C6E-8B60-52EDF84880D3}_is1"
$installed = $false

try {
    Invoke-HiddenProcess -FilePath $resolvedInstaller -Arguments @(
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/DIR=`"$resolvedInstallDirectory`"",
        "/TASKS=associatefiles"
    )
    $installed = $true

    if (-not (Test-Path -LiteralPath $installedExe)) {
        throw "Installer did not create the application executable: $installedExe"
    }
    if ((Test-Path -LiteralPath $gpuManifest) -or (Test-Path -LiteralPath $modelManifest)) {
        throw "The default core installation unexpectedly selected an optional component."
    }

    $displayName = Get-RegistryValueOrNull -Path $uninstallPath -Name "DisplayName"
    if ($displayName -ne "Mangosteen Image Viewer") {
        throw "Unexpected uninstall display name: '$displayName'."
    }

    $capabilities = Get-RegistryValueOrNull `
        -Path $registeredApplicationsPath `
        -Name "Mangosteen Image Viewer"
    if ($capabilities -ne "Software\Classes\Applications\Mangosteen.exe\Capabilities") {
        throw "RegisteredApplications entry was not installed correctly."
    }

    foreach ($extension in @(".jpg", ".jpeg", ".png", ".avif", ".heic", ".dng", ".raf")) {
        $progId = Get-RegistryValueOrNull -Path $capabilitiesPath -Name $extension
        if ($progId -ne "Mangosteen.Image") {
            throw "File association '$extension' was not installed correctly."
        }
    }

    Invoke-HiddenProcess -FilePath $uninstaller -Arguments @(
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART"
    )
    $installed = $false

    if (Test-Path -LiteralPath $installedExe) {
        throw "Uninstaller left the application executable behind."
    }

    if ($null -ne (Get-RegistryValueOrNull -Path $registeredApplicationsPath -Name "Mangosteen Image Viewer")) {
        throw "Uninstaller left the RegisteredApplications entry behind."
    }

    if (Test-Path -LiteralPath $uninstallPath) {
        throw "Uninstaller left its uninstall registry entry behind."
    }

    Invoke-HiddenProcess -FilePath $resolvedInstaller -Arguments @(
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/DIR=`"$resolvedInstallDirectory`"",
        "/TYPE=complete",
        "/TASKS=associatefiles"
    )
    $installed = $true

    foreach ($componentFile in @($gpuManifest, $modelManifest, $f3dApi)) {
        if (-not (Test-Path -LiteralPath $componentFile)) {
            throw "Complete installation is missing optional component file: $componentFile"
        }
    }

    $modelProgId = Get-RegistryValueOrNull -Path $capabilitiesPath -Name ".stl"
    if ($modelProgId -ne "Mangosteen.Model") {
        throw "The complete installation did not register optional model associations."
    }

    Invoke-HiddenProcess -FilePath $resolvedInstaller -Arguments @(
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/DIR=`"$resolvedInstallDirectory`"",
        "/TYPE=core",
        "/TASKS=associatefiles"
    )

    foreach ($componentFile in @($gpuManifest, $modelManifest, $f3dApi)) {
        if (Test-Path -LiteralPath $componentFile) {
            throw "Core reinstall left a deselected optional component file behind: $componentFile"
        }
    }

    if ($null -ne (Get-RegistryValueOrNull -Path $capabilitiesPath -Name ".stl")) {
        throw "Core reinstall left the optional model association registered."
    }

    Invoke-HiddenProcess -FilePath $uninstaller -Arguments @(
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART"
    )
    $installed = $false

    if (Test-Path -LiteralPath $installedExe) {
        throw "Uninstaller left the complete application installation behind."
    }

    Write-Host "Installer smoke test passed."
}
finally {
    if ($installed -and (Test-Path -LiteralPath $uninstaller)) {
        Invoke-HiddenProcess -FilePath $uninstaller -Arguments @(
            "/VERYSILENT",
            "/SUPPRESSMSGBOXES",
            "/NORESTART"
        )
    }

    if (Test-Path -LiteralPath $resolvedInstallDirectory) {
        Remove-Item -LiteralPath $resolvedInstallDirectory -Recurse -Force
    }
}
