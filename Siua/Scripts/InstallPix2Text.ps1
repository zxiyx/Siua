#requires -Version 5.1
<#
Pix2Text 安装/修复器。独立运行时先退出 Siua；在桌面右键运行会提示输入 Siua.exe 所在目录。
放在 Siua.exe 旁边运行时自动使用该目录。也可指定：
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\InstallPix2Text.ps1 -InstallDirectory "D:\Siua"
默认复用可用环境；验证失败自动修复核心依赖。-Upgrade 更新其他依赖，但仍遵守核心兼容版本。
-NoPause 适用于软件内调用，必须传入 -InstallDirectory 或将脚本放在 Siua.exe 旁边。
默认自动配置当前用户 PATH；-NoUserPath 可跳过 PATH 写入。
依赖镜像：https://mirrors.tuna.tsinghua.edu.cn/help/pypi/
uv/Python 镜像：https://mirrors.ustc.edu.cn/help/github-release.html
仅安装 Python 包；首次 OCR 使用时仍可能需要下载模型权重。
#>
[CmdletBinding()]
param(
    [string]$InstallDirectory = '',
    [ValidateRange(30, 1800)][int]$HttpTimeout = 180,
    [ValidateRange(1, 5)][int]$RetryCount = 2,
    [switch]$Upgrade,
    [switch]$NoUserPath,
    [switch]$NoPause
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$PSNativeCommandUseErrorActionPreference = $false
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$script:LogPath = $null
$savedEnvironment = @{}
$originalTls = [Net.ServicePointManager]::SecurityProtocol
$exitCode = 0
# CnSTD 1.2.8 依赖 resolve_model_key；RapidOCR 3.10 的接口已变化。
# 必须作为直接安装要求传给 uv，才能修复已安装但不兼容的 RapidOCR。
# 这四项同时固定，避免只锁定 RapidOCR 却安装到新的 CnSTD/CnOCR。
$coreRequirements = @('pix2text[serve]==1.1.7', 'cnstd==1.2.8', 'cnocr[ort-cpu]==2.3.3', 'rapidocr==3.9.2')
$corePackages = @('pix2text', 'cnstd', 'cnocr', 'rapidocr')
# 与本组 Pix2Text 实测使用的模型推理接口一致，避免首次安装/Upgrade 拉入新的大版本。
$inferenceRequirements = @('transformers==4.57.6', 'optimum[onnxruntime]==2.1.0', 'optimum-onnx==0.1.0')

function Resolve-InstallDirectory {
    param([string]$RequestedDirectory, [string]$ScriptDirectory, [bool]$Unattended)
    if (-not [string]::IsNullOrWhiteSpace($RequestedDirectory)) {
        return [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($RequestedDirectory.Trim().Trim('"')))
    }
    if (Test-Path -LiteralPath (Join-Path $ScriptDirectory 'Siua.exe') -PathType Leaf) {
        return [IO.Path]::GetFullPath($ScriptDirectory)
    }
    if ($Unattended) { throw '自动运行时请通过 -InstallDirectory 指定软件目录。' }
    Write-Host '请先退出 Siua，然后输入 Siua.exe 所在的文件夹（例如 D:\Program Files\Siua）。' -ForegroundColor Cyan
    $selectedDirectory = Read-Host '安装/修复目录（留空取消）'
    if ([string]::IsNullOrWhiteSpace($selectedDirectory)) { throw '已取消安装。' }
    $resolvedDirectory = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($selectedDirectory.Trim().Trim('"')))
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedDirectory 'Siua.exe') -PathType Leaf)) {
        throw "该目录中没有 Siua.exe，请确认软件安装位置后重试：$resolvedDirectory"
    }
    return $resolvedDirectory
}

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

function Merge-PathEntries {
    param([AllowNull()][string]$Existing, [string[]]$Directories)
    $parts = @($Existing -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    foreach ($directory in $Directories) {
        $normalized = $directory.TrimEnd('\', '/')
        $found = @($parts | Where-Object {
            [Environment]::ExpandEnvironmentVariables($_).Trim().Trim('"').TrimEnd('\', '/') -ieq $normalized
        }).Count -gt 0
        if (-not $found) { $parts += $directory }
    }
    return ($parts -join ';')
}

function Add-UserPath {
    param([string[]]$Directories)
    if ($NoUserPath) { return }
    $Directories = @($Directories | Where-Object { Test-Path -LiteralPath $_ -PathType Container })
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $updated = Merge-PathEntries $userPath $Directories
    if ($updated -ne $userPath) { [Environment]::SetEnvironmentVariable('Path', $updated, 'User') }
    # 当前脚本立即可用；不覆盖已有 Python 的搜索顺序。运行中的其他程序需重新打开。
    $env:Path = Merge-PathEntries $env:Path $Directories
    Write-Step '已配置当前用户 PATH（uv、Python），未修改系统 PATH；已打开的软件需重新启动。'
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
    if (-not (Test-Path -LiteralPath $PythonExecutable -PathType Leaf)) { return $false }
    try {
        # 元数据查询不导入包，即使导入崩溃，也能先在日志留下实际版本。
        Invoke-NativeCommand $PythonExecutable @('-I', '-u', '-c', @'
import sys
from importlib.metadata import version, PackageNotFoundError
print('Python: ' + sys.version.split()[0] + ' | ' + sys.executable)
for name in ('pix2text', 'cnstd', 'cnocr', 'rapidocr', 'transformers', 'optimum', 'optimum-onnx'):
    try:
        print(name + ': ' + version(name))
    except PackageNotFoundError:
        print(name + ': not installed')
'@)
        if (-not (Test-Path -LiteralPath $Pix2TextExecutable -PathType Leaf)) {
            Write-Step '未找到 p2t.exe，将安装或修复命令入口。'
            return $false
        }
        Invoke-NativeCommand $PythonExecutable @(
            '-I', '-u', '-c',
            "from rapidocr.utils.model_resolver import resolve_model_key; import fastapi, uvicorn, multipart, pix2text.serve; print('Pix2Text service imports OK')"
        )
        return $true
    }
    catch { Write-Step "服务依赖验证未通过，详情已记录到安装日志。"; return $false }
}

function Get-Pix2TextInstallArguments {
    param([string]$PythonExecutable, [string]$Index, [bool]$Repair, [bool]$Update)
    $arguments = @('pip', 'install', '--python', $PythonExecutable, '--index-url', $Index) + $coreRequirements + $inferenceRequirements
    if ($Update) { $arguments += '--upgrade' }
    if ($Repair) {
        # 相同版本也可能存在残缺文件。仅重装四个核心包，保留大体积依赖和模型缓存。
        foreach ($package in $corePackages) { $arguments += @('--reinstall-package', $package) }
    }
    return $arguments
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
    $InstallDirectory = Resolve-InstallDirectory $InstallDirectory $PSScriptRoot ([bool]$NoPause)
    New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
    $logDirectory = Join-Path $InstallDirectory 'Log'
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $script:LogPath = Join-Path $logDirectory ('Pix2Text-install-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.log')
    Write-Step "安装目录：$InstallDirectory"
    Write-Step '重复运行会复用下载缓存；无需管理员权限（安装目录须可写）。'

    $toolsDirectory = Join-Path $env:LOCALAPPDATA 'Siua\Tools'
    $localUvDirectory = Join-Path $toolsDirectory 'uv'
    $pythonDirectory = Join-Path $toolsDirectory 'python'
    $uvExecutable = Find-UvExecutable $localUvDirectory
    if ([string]::IsNullOrWhiteSpace($uvExecutable)) { $uvExecutable = Install-Uv $localUvDirectory }
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
        Set-InstallerEnvironment 'UV_PYTHON_INSTALL_DIR' $pythonDirectory
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

        Write-Step "使用 uv：$uvExecutable；网络超时 $HttpTimeout 秒；下载并发 3。"
        Write-Step "核心兼容版本：$($coreRequirements -join '；')。"
        Write-Step "模型推理兼容版本：$($inferenceRequirements -join '；')。"
        $repairExisting = $currentVersion -eq '3.11'
        if ($repairExisting) { Write-Step '检测到已有环境需要安装/修复，将按兼容版本重装核心包，保留 Python 和缓存。' }

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
                    $installArguments = Get-Pix2TextInstallArguments $pythonExecutable $index $repairExisting ([bool]$Upgrade)
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
            throw '兼容依赖已安装，但服务导入验证仍失败。请查看本日志中的版本和完整异常；不要盲目升级 RapidOCR。'
        }
        Write-Step 'Pix2Text 安装完成，服务依赖验证通过。'
    }
    if (-not $NoUserPath) {
        $baseDirectory = & $pythonExecutable -I -X utf8 -c "import sys; print(sys.base_prefix)"
        if ($LASTEXITCODE -ne 0) { throw '无法读取 Python 安装路径。' }
        Add-UserPath @((Split-Path -Parent $uvExecutable), ([string]($baseDirectory | Select-Object -Last 1)).Trim())
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
