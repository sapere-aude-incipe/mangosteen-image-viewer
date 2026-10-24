param(
    [Parameter(Mandatory = $true)]
    [string] $InstallerPath,
    [string] $InstallDirectory,
    [string] $AllowedRoot = [System.IO.Path]::GetTempPath(),
    [ValidateSet("core", "full")]
    [string] $SetupType = "core"
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
$registeredApplicationsPath = "HKCU:\Software\RegisteredApplications"
$capabilitiesPath = "HKCU:\Software\Classes\Applications\Mangosteen.exe\Capabilities\FileAssociations"
$modelExtensions = @(".obj", ".stl", ".abc", ".usdz", ".step")
$uninstallPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{5505BFA7-AFF8-4C6E-8B60-52EDF84880D3}_is1"
$installed = $false

try {
    $installArguments = @(
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/DIR=`"$resolvedInstallDirectory`"",
        "/TASKS=associatefiles"
    )
    # Core is checked without /TYPE so the installer's default selection is what gets tested.
    if ($SetupType -eq "full") {
        $installArguments += "/TYPE=full"
    }
    Invoke-HiddenProcess -FilePath $resolvedInstaller -Arguments $installArguments
    $installed = $true

    if (-not (Test-Path -LiteralPath $installedExe)) {
        throw "Installer did not create the application executable: $installedExe"
    }
    if ($SetupType -eq "core") {
        if ((Test-Path -LiteralPath $gpuManifest) -or (Test-Path -LiteralPath $modelManifest)) {
            throw "The default core installation unexpectedly selected an optional component."
        }
        foreach ($extension in $modelExtensions) {
            if ($null -ne (Get-RegistryValueOrNull -Path $capabilitiesPath -Name $extension)) {
                throw "The core installation registered the 3D file type '$extension'."
            }
        }
    }
    else {
        if (-not (Test-Path -LiteralPath $gpuManifest)) {
            throw "The full installation did not install GPU acceleration for large images."
        }
        if (-not (Test-Path -LiteralPath $modelManifest) -or
            -not (Test-Path -LiteralPath (Join-Path $resolvedInstallDirectory "components\model-viewer\bin\f3d_c_api.dll"))) {
            throw "The full installation did not install the 3D model viewer."
        }
        foreach ($extension in $modelExtensions) {
            if ((Get-RegistryValueOrNull -Path $capabilitiesPath -Name $extension) -ne "Mangosteen.Model") {
                throw "The full installation did not register the 3D file type '$extension'."
            }
        }
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

    if (Test-Path -LiteralPath "HKCU:\Software\Classes\Mangosteen.Model") {
        throw "Uninstaller left the 3D model file type behind."
    }

    if (Test-Path -LiteralPath (Join-Path $resolvedInstallDirectory "components")) {
        throw "Uninstaller left optional components behind."
    }

    Write-Host "Installer smoke test passed ($SetupType)."
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

