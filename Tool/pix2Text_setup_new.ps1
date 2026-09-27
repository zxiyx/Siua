#requires -Version 5.1
<#
Pix2Text 独立安装器。请先退出 Siua，再将本脚本放在 Siua.exe 所在目录运行。
也可指定：powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\222.ps1 -InstallDirectory "D:\Siua"
默认复用可用环境；-Upgrade 主动更新；-NoPause 适用于自动运行。
依赖镜像：https://mirrors.tuna.tsinghua.edu.cn/help/pypi/
uv/Python 镜像：https://mirrors.ustc.edu.cn/help/github-release.html
仅安装 Python 包；首次 OCR 使用时仍可能需要下载模型权重。
#>
[CmdletBinding()]
param(
    [string]$InstallDirectory = $PSScriptRoot,
    [ValidateRange(30, 1800)][int]$HttpTimeout = 180,
    [ValidateRange(1, 5)][int]$RetryCount = 2,
    [switch]$Upgrade,
    [switch]$NoPause
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$PSNativeCommandUseErrorActionPreference = $false
$script:LogPath = $null
$savedEnvironment = @{}
$originalTls = [Net.ServicePointManager]::SecurityProtocol
$exitCode = 0

function Write-Step {
    param([string]$Message)
    $line = '[{0}] [Pix2Text安装] {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    Write-Host $line -ForegroundColor Cyan
    if ($script:LogPath) { Add-Content -LiteralPath $script:LogPath -Value $line -Encoding UTF8 }
}

function Set-InstallerEnvironment {
    param([string]$Name, [AllowNull()][string]$Value)
    if (-not $savedEnvironment.ContainsKey($Name)) {
        $savedEnvironment[$Name] = [Environment]::GetEnvironmentVariable($Name, 'Process')
    }
    [Environment]::SetEnvironmentVariable($Name, $Value, 'Process')
}

function Invoke-NativeCommand {
    param([string]$FilePath, [string[]]$Arguments)
    $resolvedCommand = Get-Command -Name $FilePath -CommandType Application -ErrorAction Stop
    # Windows PowerShell 将 stderr 包装成 ErrorRecord，正常进度不应被误判为异常。
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $resolvedCommand.Source @Arguments 2>&1 | ForEach-Object {
            $line = $_.ToString()
            Write-Host $line
            if ($script:LogPath) {
                Add-Content -LiteralPath $script:LogPath -Value $line -Encoding UTF8 -ErrorAction Stop
            }
        }
        $commandExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousPreference }
    if ($commandExitCode -ne 0) {
        throw "命令执行失败（退出码 $commandExitCode）：$FilePath $($Arguments -join ' ')"
    }
}

function Invoke-WithRetry {
    param([string]$Description, [scriptblock]$Action)
    for ($attempt = 1; $attempt -le $RetryCount; $attempt++) {
        try {
            Write-Step "$Description（第 $attempt/$RetryCount 次）"
            & $Action
            return
        }
        catch {
            Write-Step "本次失败：$($_.Exception.Message)"
            if ($attempt -eq $RetryCount) { throw }
            Start-Sleep -Seconds ([Math]::Min(2 * $attempt, 10))
        }
    }
}

function Get-PythonVersion {
    param([string]$PythonExecutable)
    if (-not (Test-Path -LiteralPath $PythonExecutable -PathType Leaf)) { return $null }
    try {
        $version = & $PythonExecutable -I -c "import sys; print('%s.%s' % sys.version_info[:2])" 2>$null
        if ($LASTEXITCODE -eq 0) { return ([string]($version | Select-Object -Last 1)).Trim() }
    }
    catch {}
    return $null
}

function Test-Pix2Text {
    param([string]$PythonExecutable, [string]$Pix2TextExecutable)
    if (-not (Test-Path -LiteralPath $Pix2TextExecutable -PathType Leaf)) { return $false }
    try {
        Invoke-NativeCommand $PythonExecutable @(
            '-I', '-c',
            "import fastapi, uvicorn, multipart, pix2text.serve; from importlib.metadata import version; print('Pix2Text ' + version('pix2text'))"
        )
        return $true
    }
    catch { Write-Step "服务依赖验证未通过，详情已记录到安装日志。"; return $false }
}

function Find-UvExecutable {
    param([string]$LocalUvDirectory)
    $candidates = @((Join-Path $LocalUvDirectory 'uv.exe'))
    $command = Get-Command uv.exe -CommandType Application -ErrorAction SilentlyContinue
    if ($null -ne $command) { $candidates += $command.Source }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            try {
                Invoke-NativeCommand $candidate @('--version')
                return $candidate
            }
            catch { Write-Step 'uv 不可用，将尝试其他来源。' }
        }
    }
    return $null
}

function Install-Uv {
    param([string]$LocalUvDirectory)
    New-Item -ItemType Directory -Path $LocalUvDirectory -Force | Out-Null
    Set-InstallerEnvironment 'UV_UNMANAGED_INSTALL' $LocalUvDirectory
    Set-InstallerEnvironment 'UV_INSTALL_DIR' $LocalUvDirectory
    Set-InstallerEnvironment 'UV_NO_MODIFY_PATH' '1'
    Set-InstallerEnvironment 'UV_INSTALLER_GITHUB_BASE_URL' $null
    $installerPath = Join-Path ([IO.Path]::GetTempPath()) ('pix2text-uv-' + [Guid]::NewGuid().ToString('N') + '.ps1')
    $sources = @(
        @{ Name = '中科大镜像'; Script = 'https://mirrors.ustc.edu.cn/github-release/astral-sh/uv/LatestRelease/uv-installer.ps1'; Download = 'https://mirrors.ustc.edu.cn/github-release/astral-sh/uv/LatestRelease/' },
        @{ Name = 'uv 官方'; Script = 'https://astral.sh/uv/install.ps1'; Download = $null }
    )
    # 始终使用系统 Windows PowerShell，避免 PowerShell 7 的 PSHOME 内不存在 powershell.exe。
    $windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    try {
        foreach ($source in $sources) {
            try {
                Set-InstallerEnvironment 'UV_DOWNLOAD_URL' $source.Download
                Invoke-WithRetry "从$($source.Name)安装 uv" {
                    Invoke-WebRequest -Uri $source.Script -OutFile $installerPath -UseBasicParsing -TimeoutSec $HttpTimeout
                    # 独立进程隔离安装器的 exit，不修改系统执行策略。
                    Invoke-NativeCommand $windowsPowerShell @(
                        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $installerPath
                    )
                }
                $localUv = Join-Path $LocalUvDirectory 'uv.exe'
                Invoke-NativeCommand $localUv @('--version')
                return $localUv
            }
            catch { Write-Step "$($source.Name)不可用：$($_.Exception.Message)" }
        }
        throw 'uv 下载失败。请检查网络、代理和安全软件后重新运行；详细信息见安装日志。'
    }
    finally {
        if (Test-Path -LiteralPath $installerPath) { Remove-Item -LiteralPath $installerPath -Force -ErrorAction SilentlyContinue }
    }
}

try {
    if ($env:OS -ne 'Windows_NT') { throw '此脚本仅支持 Windows。' }
    [Net.ServicePointManager]::SecurityProtocol = $originalTls -bor [Net.SecurityProtocolType]::Tls12
    $InstallDirectory = [IO.Path]::GetFullPath($InstallDirectory)
    New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
    $logDirectory = Join-Path $InstallDirectory 'Log'
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $script:LogPath = Join-Path $logDirectory ('Pix2Text-install-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.log')
    Write-Step "安装目录：$InstallDirectory"
    Write-Step '请先退出 Siua。重复运行会复用下载缓存；无需管理员权限（安装目录须可写）。'

    $runtimeDirectory = Join-Path $InstallDirectory 'Pix2TextRuntime'
    $pythonExecutable = Join-Path $runtimeDirectory 'Scripts\python.exe'
    $pix2TextExecutable = Join-Path $runtimeDirectory 'Scripts\p2t.exe'
    $currentVersion = Get-PythonVersion $pythonExecutable
    $alreadyInstalled = $false
    if ($currentVersion -eq '3.11' -and -not $Upgrade) {
        $alreadyInstalled = Test-Pix2Text $pythonExecutable $pix2TextExecutable
    }
    if ($alreadyInstalled) {
        Write-Step '已安装 Pix2Text，且服务依赖验证通过。无需重复下载；如需更新请使用 -Upgrade。'
    }
    else {
        Set-InstallerEnvironment 'UV_CACHE_DIR' (Join-Path $InstallDirectory '.uv-cache')
        Set-InstallerEnvironment 'UV_PYTHON_INSTALL_DIR' (Join-Path $InstallDirectory '.pix2text-python')
        Set-InstallerEnvironment 'UV_MANAGED_PYTHON' '1'
        Set-InstallerEnvironment 'UV_HTTP_TIMEOUT' ([string]$HttpTimeout)
        Set-InstallerEnvironment 'UV_HTTP_RETRIES' '3'
        Set-InstallerEnvironment 'UV_CONCURRENT_DOWNLOADS' '3'
        Set-InstallerEnvironment 'UV_NO_PROGRESS' '1'
        Set-InstallerEnvironment 'UV_NO_CONFIG' '1'
        # 每次只用一个源，防止额外源配置导致仍访问其他站点。
        foreach ($name in @('UV_INDEX','UV_INDEX_URL','UV_EXTRA_INDEX_URL','UV_DEFAULT_INDEX','UV_FIND_LINKS','UV_CONFIG_FILE')) {
            Set-InstallerEnvironment $name $null
        }

        $localUvDirectory = Join-Path $InstallDirectory '.uv-bin'
        $uvExecutable = Find-UvExecutable $localUvDirectory
        if ([string]::IsNullOrWhiteSpace($uvExecutable)) { $uvExecutable = Install-Uv $localUvDirectory }
        Write-Step "使用 uv：$uvExecutable；网络超时 $HttpTimeout 秒；下载并发 3。"

        if ($currentVersion -ne '3.11') {
            # 不再用 --clear 删除已有环境，防止误清理用户文件。
            if (Test-Path -LiteralPath $runtimeDirectory) {
                throw "现有 Pix2TextRuntime 不是可运行的 Python 3.11 环境。请退出 Siua，将该目录改名备份后重试：$runtimeDirectory"
            }
            $pythonReady = $false
            foreach ($mirror in @('https://mirrors.ustc.edu.cn/github-release/astral-sh/python-build-standalone/', 'https://github.com/astral-sh/python-build-standalone/releases/download/')) {
                try {
                    Set-InstallerEnvironment 'UV_PYTHON_INSTALL_MIRROR' $mirror
                    Invoke-WithRetry "下载 Python 3.11：$mirror" {
                        Invoke-NativeCommand $uvExecutable @('python', 'install', '3.11')
                    }
                    $pythonReady = $true
                    break
                }
                catch { Write-Step 'Python 下载未成功，尝试下一来源。' }
            }
            if (-not $pythonReady) { throw 'Python 3.11 下载失败，请检查安装日志后重试。' }
            Invoke-NativeCommand $uvExecutable @('venv', $runtimeDirectory, '--python', '3.11')
        }
        if ((Get-PythonVersion $pythonExecutable) -ne '3.11') { throw 'Python 3.11 环境验证失败。' }

        $installed = $false
        foreach ($index in @('https://pypi.tuna.tsinghua.edu.cn/simple', 'https://mirrors.ustc.edu.cn/pypi/simple', 'https://pypi.org/simple')) {
            try {
                Invoke-WithRetry "安装 Pix2Text，使用源：$index" {
                    $installArguments = @('pip', 'install', '--python', $pythonExecutable, '--index-url', $index, 'pix2text[serve]')
                    if ($Upgrade) { $installArguments += '--upgrade' }
                    Invoke-NativeCommand $uvExecutable $installArguments
                }
                $installed = $true
                break
            }
            catch { Write-Step '该源安装失败，将切换下一来源；已有缓存会保留。' }
        }
        if (-not $installed) { throw '所有源均安装失败。请查看日志中的具体原因；检查网络、磁盘空间或文件占用后重试。' }
        Write-Step '检查依赖一致性和服务模块...'
        Invoke-NativeCommand $uvExecutable @('pip', 'check', '--python', $pythonExecutable)
        if (-not (Test-Pix2Text $pythonExecutable $pix2TextExecutable)) {
            throw '包下载完成，但服务验证失败。请查看日志；可使用 -Upgrade 重试更新依赖。'
        }
        Write-Step 'Pix2Text 安装完成，服务依赖验证通过。'
    }
    Write-Step "Python：$pythonExecutable"
    Write-Step "Pix2Text：$pix2TextExecutable"
    Write-Step '首次识别时可能还需下载模型。安装目录应与 Siua.exe 所在目录一致。'
}
catch {
    $exitCode = 1
    Write-Host '[Pix2Text安装] 安装失败。' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    if ($script:LogPath) {
        try { Add-Content -LiteralPath $script:LogPath -Value ($_ | Out-String) -Encoding UTF8 } catch {}
    }
}
finally {
    foreach ($name in $savedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process')
    }
    [Net.ServicePointManager]::SecurityProtocol = $originalTls
}
if ($script:LogPath) { Write-Host "安装日志：$script:LogPath" }
if (-not $NoPause) { [void](Read-Host '按 Enter 关闭') }
exit $exitCode