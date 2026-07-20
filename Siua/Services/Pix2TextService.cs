using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Siua.Common;
using Siua.Interfaces;

namespace Siua.Services;

public sealed class Pix2TextService : IDisposable
{
    private const string LogSource = "Pix2Text";

    private readonly GlobalSettings _settings;
    private readonly ILogService _logService;
    private readonly SemaphoreSlim _installLock = new(1, 1);
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private readonly HttpClient _httpClient;
    private Process? _process;

    private const string ServerBootstrap =
        "import sys; from pix2text.serve import start_server; " +
        "config={'total_configs':{'layout':{},'text_formula':{'languages':['en','ch_sim'],'mfd':{},'formula':{},'text':{}}},'enable_formula':True,'enable_table':False,'device':'cpu'}; " +
        "start_server(config, 'output-md-root', host=sys.argv[1], port=int(sys.argv[2]), reload=False)";

    public bool IsReady { get; private set; }
    public string? ErrorMessage { get; private set; }

    public Pix2TextService(GlobalSettings settings, ILogService logService)
    {
        _settings = settings;
        _logService = logService;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
        _settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    public async Task<bool> InstallAsync(CancellationToken cancellationToken = default)
    {
        await _installLock.WaitAsync(cancellationToken);
        try
        {
            var existingExecutable = ResolveExecutablePath();
            if (existingExecutable is not null &&
                await HasServeDependenciesAsync(existingExecutable, cancellationToken))
            {
                await SaveExecutablePathAsync(existingExecutable);
                ErrorMessage = null;
                LogInfo($"Pix2Text 服务组件已安装：{existingExecutable}");
                return true;
            }

            if (existingExecutable is not null)
                LogInfo("[Pix2Text 安装] 检测到基础包，正在补充 HTTP 服务依赖...");

            var installRoot = GetInstallRoot();
            var runtimeDirectory = Path.Combine(installRoot, "Pix2TextRuntime");
            var pythonDirectory = Path.Combine(installRoot, ".pix2text-python");
            var cacheDirectory = Path.Combine(installRoot, ".uv-cache");
            var pythonExecutable = Path.Combine(runtimeDirectory, "Scripts", "python.exe");
            var pix2TextExecutable = Path.Combine(runtimeDirectory, "Scripts", "p2t.exe");
            Directory.CreateDirectory(installRoot);

            var environment = new Dictionary<string, string>
            {
                ["UV_CACHE_DIR"] = cacheDirectory,
                ["UV_PYTHON_INSTALL_DIR"] = pythonDirectory
            };

            if (!File.Exists(pythonExecutable))
            {
                var pythonRequest = "python.exe";
                if (await HasSystemPythonAsync(cancellationToken))
                {
                    LogInfo("[Pix2Text 安装] 检测到现有 Python，直接使用");
                }
                else
                {
                    pythonRequest = "3.11";
                    LogInfo("[Pix2Text 安装] 未找到 Python，准备 Python 3.11...");
                    if (await RunUvAsync(
                            installRoot,
                            environment,
                            ["python", "install", "3.11"],
                            cancellationToken) != 0)
                    {
                        return InstallationFailed("Python 3.11 安装失败");
                    }
                }

                LogInfo("[Pix2Text 安装] 创建独立运行环境...");
                if (await RunUvAsync(
                        installRoot,
                        environment,
                        ["venv", runtimeDirectory, "--python", pythonRequest],
                        cancellationToken) != 0)
                {
                    return InstallationFailed("Pix2Text 运行环境创建失败");
                }
            }

            LogInfo("[Pix2Text 安装] 安装或更新 Pix2Text，下载模型依赖可能需要一些时间...");
            if (await RunUvAsync(
                    installRoot,
                    environment,
                    ["pip", "install", "--python", pythonExecutable, "--upgrade", "pix2text[serve]"],
                    cancellationToken) != 0)
            {
                return InstallationFailed("Pix2Text 安装失败");
            }

            if (!File.Exists(pix2TextExecutable))
                return InstallationFailed("安装完成，但没有找到 p2t.exe");

            await SaveExecutablePathAsync(pix2TextExecutable);
            ErrorMessage = null;
            LogInfo("Pix2Text 安装完成，可以点击“启动 Pix2Text”");
            return true;
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Pix2Text 安装已取消";
            LogInfo(ErrorMessage);
            return false;
        }
        catch (Win32Exception exception)
        {
            ErrorMessage = "未找到 uv，请先安装 uv 并确保 uv.exe 已加入 PATH";
            LogError($"{ErrorMessage}：{exception.Message}");
            return false;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            LogError($"Pix2Text 安装失败：{exception.Message}");
            return false;
        }
        finally
        {
            _installLock.Release();
        }
    }

    public async Task<bool> EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetEndpoint(out var listenAddress, out var serviceUri, out var endpointError))
        {
            ErrorMessage = endpointError;
            LogError(endpointError);
            return false;
        }

        if (IsReady || await IsServiceReadyAsync(serviceUri, cancellationToken))
        {
            IsReady = true;
            return true;
        }

        await _startLock.WaitAsync(cancellationToken);
        try
        {
            if (!TryGetEndpoint(out listenAddress, out serviceUri, out endpointError))
            {
                ErrorMessage = endpointError;
                LogError(endpointError);
                return false;
            }

            if (IsReady || await IsServiceReadyAsync(serviceUri, cancellationToken))
            {
                IsReady = true;
                return true;
            }

            var executable = ResolveExecutablePath();
            if (executable is null)
            {
                ErrorMessage = "未找到 Pix2Text Runtime，请先点击“安装 Pix2Text”。";
                LogError(ErrorMessage);
                return false;
            }

            var pythonExecutable = Path.Combine(
                Path.GetDirectoryName(executable)!,
                "python.exe");
            if (!File.Exists(pythonExecutable))
            {
                ErrorMessage = "Pix2Text Runtime 中没有找到 python.exe，请重新安装。";
                LogError(ErrorMessage);
                return false;
            }

            LogInfo($"正在启动 Pix2Text（{serviceUri.Authority}），首次加载模型可能需要较长时间...");
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonExecutable,
                    WorkingDirectory = Path.GetDirectoryName(executable)!,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };
            AddProcessArguments(process.StartInfo, listenAddress, _settings.Pix2TextPort);
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    LogInfo(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    LogProcessOutput(e.Data);
            };
            _process = process;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            for (var i = 0; i < 240; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited)
                    break;

                if (await IsServiceReadyAsync(serviceUri, cancellationToken))
                {
                    IsReady = true;
                    ErrorMessage = null;
                    LogInfo("Pix2Text 已就绪，支持普通文字和数学公式识别");
                    return true;
                }

                await Task.Delay(500, cancellationToken);
            }

            ErrorMessage = process.HasExited
                ? $"Pix2Text 启动失败，进程退出码：{process.ExitCode}"
                : "Pix2Text 模型加载超时";
            LogError(ErrorMessage);
            return false;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            LogError($"Pix2Text 启动失败：{ex.Message}");
            return false;
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task<string?> RecognizeAsync(
        byte[] imageBytes,
        CancellationToken cancellationToken = default)
    {
        if (imageBytes is not { Length: > 0 })
        {
            ErrorMessage = "截图数据为空";
            LogError($"Pix2Text 识别失败：{ErrorMessage}");
            return null;
        }

        if (!await EnsureReadyAsync(cancellationToken))
            return null;

        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent("text_formula"), "file_type");
            form.Add(new StringContent("768"), "resized_shape");
            form.Add(new StringContent(" $,$ "), "embed_sep");
            form.Add(new StringContent("$$\n,\n$$"), "isolated_sep");

            using var image = new ByteArrayContent(imageBytes);
            image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(image, "image", "question.png");

            if (!TryGetEndpoint(out _, out var serviceUri, out var endpointError))
                throw new InvalidOperationException(endpointError);

            using var response = await _httpClient.PostAsync(
                new Uri(serviceUri, "pix2text"), form, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                ErrorMessage =
                    $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase})：{json}";
                LogError($"Pix2Text 识别失败：{ErrorMessage}");
                return null;
            }

            return ExtractText(json);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            IsReady = false;
            LogError($"Pix2Text 识别失败：{ex.Message}");
            return null;
        }
    }

    public bool Stop() => StopAsync().GetAwaiter().GetResult();

    public async Task<bool> StopAsync(CancellationToken cancellationToken = default)
    {
        var stopped = StopManagedProcess();
        var fullyStopped = true;
        if (TryGetEndpoint(out _, out var serviceUri, out _))
        {
            if (stopped || await IsServiceReadyAsync(serviceUri, cancellationToken))
                stopped |= StopListeningProcesses(_settings.Pix2TextPort);

            fullyStopped = await WaitUntilStoppedAsync(
                serviceUri,
                _settings.Pix2TextPort,
                cancellationToken);
        }

        IsReady = false;
        ErrorMessage = null;
        var message = stopped && fullyStopped
            ? "Pix2Text 服务已完全停止，监听端口已释放"
            : fullyStopped
                ? "Pix2Text 服务当前未运行"
                : "Pix2Text 服务停止失败，监听端口仍被占用";
        if (fullyStopped)
            LogInfo(message);
        else
            LogError(message);
        return fullyStopped;
    }

    private async Task<bool> WaitUntilStoppedAsync(
        Uri serviceUri,
        int port,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (!await IsServiceReadyAsync(serviceUri, cancellationToken) &&
                GetListeningProcessIds(port).Count == 0)
                return true;

            await Task.Delay(100, cancellationToken);
        }

        return false;
    }

    private bool StopListeningProcesses(int port)
    {
        var stopped = false;
        foreach (var processId in GetListeningProcessIds(port))
        {
            if (processId == Environment.ProcessId)
                continue;

            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                    continue;

                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
                stopped |= process.HasExited;
            }
            catch (ArgumentException)
            {
                stopped = true;
            }
            catch (Exception exception)
            {
                LogError($"Pix2Text 监听进程 {processId} 停止失败：{exception.Message}");
            }
        }

        return stopped;
    }

    private static IReadOnlySet<int> GetListeningProcessIds(int port)
    {
        if (!OperatingSystem.IsWindows())
            return new HashSet<int>();

        var processIds = new HashSet<int>();
        ReadIpv4Listeners(port, processIds);
        ReadIpv6Listeners(port, processIds);
        return processIds;
    }

    private static void ReadIpv4Listeners(int port, ISet<int> processIds)
    {
        var size = 0;
        _ = GetExtendedTcpTable(
            IntPtr.Zero,
            ref size,
            true,
            (int)AddressFamily.InterNetwork,
            TcpTableClass.OwnerPidListener,
            0);
        if (size <= 0)
            return;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(
                    buffer,
                    ref size,
                    true,
                    (int)AddressFamily.InterNetwork,
                    TcpTableClass.OwnerPidListener,
                    0) != 0)
                return;

            var count = Marshal.ReadInt32(buffer);
            var rowPointer = IntPtr.Add(buffer, sizeof(uint));
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            for (var index = 0; index < count; index++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPointer);
                if (GetPort(row.LocalPort) == port)
                    processIds.Add((int)row.OwningPid);
                rowPointer = IntPtr.Add(rowPointer, rowSize);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void ReadIpv6Listeners(int port, ISet<int> processIds)
    {
        var size = 0;
        _ = GetExtendedTcpTable(
            IntPtr.Zero,
            ref size,
            true,
            (int)AddressFamily.InterNetworkV6,
            TcpTableClass.OwnerPidListener,
            0);
        if (size <= 0)
            return;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(
                    buffer,
                    ref size,
                    true,
                    (int)AddressFamily.InterNetworkV6,
                    TcpTableClass.OwnerPidListener,
                    0) != 0)
                return;

            var count = Marshal.ReadInt32(buffer);
            var rowPointer = IntPtr.Add(buffer, sizeof(uint));
            var rowSize = Marshal.SizeOf<MibTcp6RowOwnerPid>();
            for (var index = 0; index < count; index++)
            {
                var row = Marshal.PtrToStructure<MibTcp6RowOwnerPid>(rowPointer);
                if (GetPort(row.LocalPort) == port)
                    processIds.Add((int)row.OwningPid);
                rowPointer = IntPtr.Add(rowPointer, rowSize);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int GetPort(uint networkPort) =>
        unchecked((ushort)IPAddress.NetworkToHostOrder((short)networkPort));

    private enum TcpTableClass
    {
        OwnerPidListener = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddress;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddress;
        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int size,
        bool order,
        int addressFamily,
        TcpTableClass tableClass,
        uint reserved);

    private string? ResolveExecutablePath()
    {
        var configured = _settings.Pix2TextExecutablePath;
        var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var candidates = new[]
        {
            Path.IsPathRooted(configured) ? configured : Path.Combine(AppContext.BaseDirectory, configured),
            Path.Combine(Directory.GetCurrentDirectory(), configured),
            Path.Combine(GetInstallRoot(), "Pix2TextRuntime", "Scripts", "p2t.exe"),
            Path.Combine(desktopDirectory, "Pix2TextRuntime", "Scripts", "p2t.exe"),
            Path.Combine(Directory.GetCurrentDirectory(), "tools", "pix2text", ".venv", "Scripts", "p2t.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string GetInstallRoot() => AppContext.BaseDirectory;

    private async Task SaveExecutablePathAsync(string executablePath)
    {
        _settings.Pix2TextExecutablePath = Path.GetFullPath(executablePath);
        await _settings.SaveToJson();
    }

    private bool InstallationFailed(string message)
    {
        ErrorMessage = message;
        LogError(message);
        return false;
    }

    private async Task<int> RunUvAsync(
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "uv.exe",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        foreach (var variable in environment)
            process.StartInfo.Environment[variable.Key] = variable.Value;

        process.Start();
        var outputTask = RelayInstallerOutputAsync(process.StandardOutput);
        var errorTask = RelayInstallerOutputAsync(process.StandardError);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }

        await Task.WhenAll(outputTask, errorTask);
        return process.ExitCode;
    }

    private static async Task<bool> HasSystemPythonAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "python.exe",
                    Arguments = "--version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.Start();
            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var versionText = $"{await standardOutput} {await standardError}";
            foreach (var token in versionText.Split(
                         [' ', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries))
            {
                if (Version.TryParse(token, out var version))
                    return version.Major == 3;
            }
        }
        catch
        {
        }

        return false;
    }

    private static async Task<bool> HasServeDependenciesAsync(
        string pix2TextExecutable,
        CancellationToken cancellationToken)
    {
        var pythonExecutable = Path.Combine(
            Path.GetDirectoryName(pix2TextExecutable)!,
            "python.exe");
        if (!File.Exists(pythonExecutable))
            return false;

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonExecutable,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add("import fastapi, uvicorn, multipart, pix2text.serve");
            process.Start();
            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(standardOutput, standardError);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task RelayInstallerOutputAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
                LogInfo($"[安装] {line}");
        }
    }

    private async Task<bool> IsServiceReadyAsync(Uri serviceUri, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(1));
            using var response = await _httpClient.GetAsync(
                new Uri(serviceUri, "openapi.json"), timeout.Token);
            if (!response.IsSuccessStatusCode)
                return false;

            var content = await response.Content.ReadAsStringAsync(timeout.Token);
            return content.Contains("/pix2text", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static string? ExtractText(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("results", out var results))
            return null;

        if (results.ValueKind == JsonValueKind.String)
            return NormalizeRecognitionText(results.GetString());

        if (results.ValueKind != JsonValueKind.Array)
            return null;

        return NormalizeRecognitionText(string.Join("\n", results.EnumerateArray()
            .Select(item => item.TryGetProperty("text", out var text) ? text.GetString() : null)
            .Where(text => !string.IsNullOrWhiteSpace(text))));
    }

    private static string? NormalizeRecognitionText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var normalized = Regex.Replace(
            text,
            @"\$\$(?<math>.*?)\$\$",
            match => NormalizeMath(match.Groups["math"].Value),
            RegexOptions.Singleline);
        normalized = Regex.Replace(
            normalized,
            @"(?<!\$)\$(?!\$)(?<math>.*?)(?<!\$)\$(?!\$)",
            match => NormalizeMath(match.Groups["math"].Value));
        normalized = Regex.Replace(
            normalized,
            @"(?<![A-Za-z0-9])(?<option>[a-hA-H])\s*[)）](?=\s|$)",
            match => match.Groups["option"].Value.ToUpperInvariant());
        return normalized.Replace("$", string.Empty).Trim();
    }

    private static string NormalizeMath(string math)
    {
        var normalized = Regex.Replace(math, @"\\!", string.Empty);
        normalized = Regex.Replace(normalized, @"(?<=\d)\s+(?=\d)", string.Empty);
        normalized = Regex.Replace(normalized, @"(?<=\d)\s+(?=[A-Za-z])", string.Empty);
        normalized = Regex.Replace(normalized, @"\s*([+\-=(),])\s*", "$1");
        return Regex.Replace(normalized, @"\s+", " ").Trim();
    }

    private bool TryGetEndpoint(
        out IPAddress listenAddress,
        out Uri serviceUri,
        out string errorMessage) =>
        Pix2TextEndpoint.TryCreate(
            _settings.Pix2TextHost,
            _settings.Pix2TextPort,
            out listenAddress,
            out serviceUri,
            out errorMessage);

    private static void AddProcessArguments(
        ProcessStartInfo startInfo,
        IPAddress listenAddress,
        int port)
    {
        string[] arguments =
        [
            "-c", ServerBootstrap, listenAddress.ToString(), port.ToString()
        ];
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is not (nameof(GlobalSettings.Pix2TextHost) or
            nameof(GlobalSettings.Pix2TextPort)))
            return;

        IsReady = false;
        ErrorMessage = null;
        StopManagedProcess();
    }

    private bool StopManagedProcess()
    {
        var process = Interlocked.Exchange(ref _process, null);
        if (process is null)
            return false;

        try
        {
            var wasRunning = !process.HasExited;
            if (wasRunning)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
            return wasRunning;
        }
        catch
        {
            return false;
        }
        finally
        {
            process.Dispose();
        }
    }

    private void LogInfo(string message) =>
        _logService.AddLog(LogLevel.Info, LogSource, message);

    private void LogError(string message) =>
        _logService.AddLog(LogLevel.Error, LogSource, message);

    private void LogProcessOutput(string message)
    {
        if (message.Contains("Traceback", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Exception", StringComparison.OrdinalIgnoreCase))
        {
            LogError(message);
        }
        else
        {
            LogInfo(message);
        }
    }

    public void Dispose()
    {
        _settings.PropertyChanged -= OnSettingsPropertyChanged;
        StopManagedProcess();
        _httpClient.Dispose();
        _installLock.Dispose();
        _startLock.Dispose();
    }
}
