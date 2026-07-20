[CmdletBinding()]
param(
    [string]$InstallDirectory = $PSScriptRoot,
    [switch]$NoPause
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Step {
    param([string]$Message)

    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-NativeCommand {
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed. Exit code $LASTEXITCODE. Command: $FilePath $($Arguments -join ' ')"
    }
}

function Find-UvExecutable {
    param(
        [string]$LocalUvDirectory
    )

    $localUv = Join-Path $LocalUvDirectory "uv.exe"
    if (Test-Path -LiteralPath $localUv) {
        return $localUv
    }

    $command = Get-Command uv.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    return $null
}

function Install-Uv {
    param(
        [string]$LocalUvDirectory
    )

    Write-Step "uv was not found. Installing a local copy..."
    New-Item -ItemType Directory -Path $LocalUvDirectory -Force | Out-Null

    $env:UV_INSTALL_DIR = $LocalUvDirectory
    $env:UV_NO_MODIFY_PATH = "1"
    $installer = Invoke-RestMethod -Uri "https://astral.sh/uv/install.ps1"
    Invoke-Expression $installer

    $uvExecutable = Join-Path $LocalUvDirectory "uv.exe"
    if (-not (Test-Path -LiteralPath $uvExecutable)) {
        throw "uv installation completed, but uv.exe was not found in $LocalUvDirectory"
    }

    return $uvExecutable
}

function Get-PythonVersion {
    param([string]$PythonExecutable)

    if (-not (Test-Path -LiteralPath $PythonExecutable)) {
        return $null
    }

    try {
        $version = & $PythonExecutable -c "import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}')"
        if ($LASTEXITCODE -eq 0) {
            return ($version | Select-Object -Last 1).Trim()
        }
    }
    catch {
    }

    return $null
}

try {
    if ($PSVersionTable.PSEdition -eq "Core" -and -not $IsWindows) {
        throw "This installer currently supports Windows only."
    }

    $InstallDirectory = [System.IO.Path]::GetFullPath($InstallDirectory)
    $runtimeDirectory = Join-Path $InstallDirectory "Pix2TextRuntime"
    $pythonDirectory = Join-Path $InstallDirectory ".pix2text-python"
    $cacheDirectory = Join-Path $InstallDirectory ".uv-cache"
    $localUvDirectory = Join-Path $InstallDirectory ".uv-bin"
    $pythonExecutable = Join-Path $runtimeDirectory "Scripts\python.exe"
    $pix2TextExecutable = Join-Path $runtimeDirectory "Scripts\p2t.exe"

    New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null

    $uvExecutable = Find-UvExecutable -LocalUvDirectory $localUvDirectory
    if ([string]::IsNullOrWhiteSpace($uvExecutable)) {
        $uvExecutable = Install-Uv -LocalUvDirectory $localUvDirectory
    }

    Write-Step "Using uv: $uvExecutable"

    $env:UV_CACHE_DIR = $cacheDirectory
    $env:UV_PYTHON_INSTALL_DIR = $pythonDirectory
    $env:UV_MANAGED_PYTHON = "1"

    Write-Step "Installing the latest Python 3.11 managed by uv..."
    Invoke-NativeCommand -FilePath $uvExecutable -Arguments @(
        "python", "install", "3.11"
    )

    $currentVersion = Get-PythonVersion -PythonExecutable $pythonExecutable
    if ($currentVersion -ne "3.11") {
        Write-Step "Creating an isolated Python 3.11 environment..."
        Invoke-NativeCommand -FilePath $uvExecutable -Arguments @(
            "venv", $runtimeDirectory, "--python", "3.11", "--clear"
        )
    }
    else {
        Write-Step "Existing Python 3.11 runtime detected. Reusing it..."
    }

    if (-not (Test-Path -LiteralPath $pythonExecutable)) {
        throw "Python environment creation failed: $pythonExecutable was not found."
    }

    Write-Step "Installing or updating Pix2Text and HTTP service dependencies..."
    Invoke-NativeCommand -FilePath $uvExecutable -Arguments @(
        "pip", "install",
        "--python", $pythonExecutable,
        "--upgrade",
        "pix2text[serve]"
    )

    if (-not (Test-Path -LiteralPath $pix2TextExecutable)) {
        throw "Pix2Text installation completed, but p2t.exe was not found."
    }

    Write-Step "Validating the Pix2Text runtime..."
    Invoke-NativeCommand -FilePath $pythonExecutable -Arguments @(
        "-c",
        "import fastapi, uvicorn, multipart, pix2text.serve; print('Pix2Text service dependencies are ready.')"
    )

    $installedVersion = & $pythonExecutable -c "import sys; print(sys.version.split()[0])"

    Write-Host ""
    Write-Host "Pix2Text installation completed successfully." -ForegroundColor Green
    Write-Host "Install directory : $InstallDirectory"
    Write-Host "Python version    : $installedVersion"
    Write-Host "Python executable : $pythonExecutable"
    Write-Host "Pix2Text command  : $pix2TextExecutable"
    Write-Host ""
    Write-Host "Keep this script beside Siua.exe if you want Pix2TextRuntime to be installed in the software directory."
}
catch {
    Write-Host ""
    Write-Host "Pix2Text installation failed." -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    if (-not $NoPause) {
        Read-Host "Press Enter to close"
    }
    exit 1
}

if (-not $NoPause) {
    Read-Host "Press Enter to close"
}
