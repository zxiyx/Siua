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
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Siua.Common;
using Siua.Interfaces;

namespace Siua.Services;

/// <summary>管理 Pix2Text 的安装、运行和图像识别请求。</summary>
public sealed class Pix2TextService : IDisposable
{
    private const string LogSource = "Pix2Text";

    private readonly GlobalSettings _settings;
    private readonly ILogService _logService;
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private readonly HttpClient _httpClient;
    private Pix2TextProcess? _process;
    private readonly object _operationSync = new();
    private CancellationTokenSource? _activeOperation;
    private readonly Func<ProcessStartInfo, Process> _processFactory;
    private readonly TimeSpan _startupTimeout;
    private readonly string _installRoot;
    private bool _disposed;

    private const string ServerBootstrap =
        "import sys; from pix2text.serve import start_server; " +
        "config={'total_configs':{'layout':{},'text_formula':{'languages':['en','ch_sim'],'mfd':{},'formula':{},'text':{}}},'enable_formula':True,'enable_table':False,'device':'cpu'}; " +
        "start_server(config, 'output-md-root', host=sys.argv[1], port=int(sys.argv[2]), reload=False)";

    public bool IsReady { get; private set; }
    public string? ErrorMessage { get; private set; }

    public Pix2TextService(GlobalSettings settings, ILogService logService)
        : this(settings, logService, info => new Process { StartInfo = info }, TimeSpan.FromMinutes(5))
    {
    }

    internal Pix2TextService(GlobalSettings settings, ILogService logService,
        Func<ProcessStartInfo, Process> processFactory, TimeSpan startupTimeout, string? installRoot = null)
    {
        _settings = settings;
        _logService = logService;
        _processFactory = processFactory;
        _startupTimeout = startupTimeout;
        _installRoot = installRoot ?? AppContext.BaseDirectory;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
        _settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    public async Task<bool> InstallAsync(CancellationToken cancellationToken = default)
    {
        await _startLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var operation = BeginOperation(cancellationToken);
        try
        {
            cancellationToken = operation.Token;
            LogInstallation("正在检测 Pix2Text 是否已安装...");
            var existingExecutable = ResolveExecutablePath();
            if (existingExecutable is not null &&
                await HasServeDependenciesAsync(existingExecutable, cancellationToken).ConfigureAwait(false))
            {
                await SaveExecutablePathAsync(existingExecutable).ConfigureAwait(false);
                ErrorMessage = null;
                LogInstallation("已安装Pix2Text");
                return true;
            }

            // 安装与启动共用锁，升级文件前仅停止本软件拥有的服务进程。
            StopManagedProcess();
            var installRoot = GetInstallRoot();
            Directory.CreateDirectory(installRoot);
            var scriptPath = Path.Combine(Path.GetTempPath(), $"Siua-Pix2Text-{Guid.NewGuid():N}.ps1");
            try
            {
                await using (var source = typeof(Pix2TextService).Assembly.GetManifestResourceStream("Siua.InstallPix2Text.ps1")
                    ?? throw new FileNotFoundException("内置 Pix2Text 安装脚本缺失。"))
                await using (var target = File.Create(scriptPath))
                    await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);

                LogInstallation("自动准备 uv、Python 3.11 和 Pix2Text；优先使用国内镜像...");
                var info = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                        "WindowsPowerShell", "v1.0", "powershell.exe"),
                    WorkingDirectory = installRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                    scriptPath, "-InstallDirectory", installRoot, "-NoPause" })
                    info.ArgumentList.Add(argument);
                using var process = _processFactory(info);
                process.Start();
                var output = RelayInstallerOutputAsync(process.StandardOutput);
                var error = RelayInstallerOutputAsync(process.StandardError);
                try { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
                catch
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                    throw;
                }
                finally { await Task.WhenAll(output, error).ConfigureAwait(false); }
                if (process.ExitCode != 0)
                    return InstallationFailed($"安装脚本退出码 {process.ExitCode}；详细输出见 Log 文件夹的 Pix2Text-install 日志。");

                var executable = Path.Combine(installRoot, "Pix2TextRuntime", "Scripts", "p2t.exe");
                if (!File.Exists(executable) || !await HasServeDependenciesAsync(executable, cancellationToken).ConfigureAwait(false))
                    return InstallationFailed("安装后服务依赖验证失败，请检查日志");
                await SaveExecutablePathAsync(executable).ConfigureAwait(false);
                ErrorMessage = null;
                LogInstallation("安装完成，可以直接启动 Pix2Text，无需重启电脑");
                return true;
            }
            finally { if (File.Exists(scriptPath)) File.Delete(scriptPath); }
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Pix2Text 安装已取消";
            LogInstallation("安装已取消");
            return false;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            LogInstallation($"安装失败：{exception}", LogLevel.Error);
            return false;
        }
        finally
        {
            EndOperation(operation);
            _startLock.Release();
        }
    }

    public async Task<bool> EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        await _startLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var operation = BeginOperation(cancellationToken);
        Pix2TextProcess? session = null;
        try
        {
            cancellationToken = operation.Token;
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetEndpoint(out var listenAddress, out var serviceUri, out var endpointError))
            {
                ErrorMessage = endpointError;
                LogError(endpointError);
                return false;
            }

            // 每次核实接口，不让上一次的 IsReady 掩盖已退出的进程或失效端口。
            if (await IsServiceReadyAsync(serviceUri, cancellationToken).ConfigureAwait(false))
            {
                _process?.MarkReady();
                IsReady = true;
                ErrorMessage = null;
                return true;
            }
            IsReady = false;
            session = _process;
            if (session is { HasExited: true })
            {
                StopManagedProcess();
                session = null;
            }
            if (session is null)
            {
                // 未确认是 Pix2Text 的监听者不强行关闭，也不再启动一个竞争同一端口的进程。
                if (GetListeningProcessIds(_settings.Pix2TextPort).Count != 0)
                {
                    ErrorMessage = $"端口 {_settings.Pix2TextPort} 已被占用，且未检测到可用 Pix2Text 服务";
                    LogError(ErrorMessage);
                    return false;
                }
                var executable = ResolveExecutablePath();
                if (executable is null)
                {
                    ErrorMessage = "未找到 Pix2Text Runtime，请先点击“安装 Pix2Text”。";
                    LogError(ErrorMessage);
                    return false;
                }
                var pythonExecutable = Path.Combine(Path.GetDirectoryName(executable)!, "python.exe");
                if (!File.Exists(pythonExecutable))
                {
                    ErrorMessage = "Pix2Text Runtime 中没有找到 python.exe，请重新安装。";
                    LogError(ErrorMessage);
                    return false;
                }
                var info = CreatePythonStartInfo(pythonExecutable);
                AddProcessArguments(info, listenAddress, _settings.Pix2TextPort);
                session = new Pix2TextProcess(_processFactory(info), pythonExecutable);
                _process = session;
                var owned = session;
                session.Start(() =>
                {
                    if (!ReferenceEquals(_process, owned)) return;
                    IsReady = false;
                    if (owned.WasReady)
                    {
                        ErrorMessage = $"Pix2Text 服务进程已退出，退出码 {owned.ExitCode}";
                        LogError(ErrorMessage);
                    }
                });
                cancellationToken.ThrowIfCancellationRequested();
                LogInfo($"正在启动 Pix2Text（{serviceUri.Authority}），首次加载模型可能需要较长时间...");
            }
            else
            {
                LogInfo("Pix2Text 进程仍在运行，继续等待服务就绪，不重复启动...");
            }

            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < _startupTimeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (session.HasExited)
                {
                    ErrorMessage = $"Pix2Text 启动失败，进程退出码 {session.ExitCode}";
                    LogError($"{ErrorMessage}：{Environment.NewLine}{await session.GetStartupDetailsAsync().ConfigureAwait(false)}");
                    StopManagedProcess();
                    return false;
                }
                if (await IsServiceReadyAsync(serviceUri, cancellationToken).ConfigureAwait(false) && !session.HasExited)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    session.MarkReady();
                    IsReady = true;
                    if (session.HasExited) { IsReady = false; continue; }
                    ErrorMessage = null;
                    LogInfo("Pix2Text 已就绪，支持普通文字和数学公式识别");
                    return true;
                }
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }

            if (session.HasExited)
            {
                ErrorMessage = $"Pix2Text 启动失败，进程退出码 {session.ExitCode}";
                LogError($"{ErrorMessage}：{Environment.NewLine}{await session.GetStartupDetailsAsync().ConfigureAwait(false)}");
                StopManagedProcess();
                return false;
            }
            // 保留仍在加载的进程，下次启动继续等待同一进程。
            ErrorMessage = "Pix2Text 模型加载超时，进程仍保留；可再次点击启动继续等待";
            LogError($"{ErrorMessage}：{Environment.NewLine}{await session.GetStartupDetailsAsync().ConfigureAwait(false)}");
            return false;
        }
        catch (OperationCanceledException)
        {
            StopManagedProcess();
            IsReady = false;
            throw;
        }
        catch (Exception exception)
        {
            IsReady = false;
            var details = session is null ? "" : await session.GetStartupDetailsAsync().ConfigureAwait(false);
            ErrorMessage = exception.Message;
            LogError($"Pix2Text 启动失败：{exception}{Environment.NewLine}{details}");
            StopManagedProcess();
            return false;
        }
        finally
        {
            EndOperation(operation);
            _startLock.Release();
        }
    }

    internal static ProcessStartInfo CreatePythonStartInfo(string executable)
    {
        var directory = Path.GetDirectoryName(executable)!;
        var info = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        // 显式使用虚拟环境及其 DLL 搜索路径，不依赖安装前 Siua 继承的旧 PATH。
        info.Environment["PATH"] = directory + Path.PathSeparator +
            Path.GetDirectoryName(directory) + Path.PathSeparator +
            Environment.GetEnvironmentVariable("PATH");
        info.Environment.Remove("PYTHONHOME");
        info.Environment.Remove("PYTHONPATH");
        info.Environment["PYTHONNOUSERSITE"] = "1";
        info.Environment["PYTHONUTF8"] = "1";
        info.Environment["PYTHONUNBUFFERED"] = "1";
        return info;
    }

    private CancellationTokenSource BeginOperation(CancellationToken token)
    {
        lock (_operationSync)
        {
            var operation = CancellationTokenSource.CreateLinkedTokenSource(token);
            if (_disposed) operation.Cancel();
            _activeOperation = operation;
            return operation;
        }
    }

    private void EndOperation(CancellationTokenSource operation)
    {
        lock (_operationSync)
        {
            if (ReferenceEquals(_activeOperation, operation)) _activeOperation = null;
        }
    }

    private void CancelOperation()
    {
        lock (_operationSync) { _activeOperation?.Cancel(); }
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
            if (!response.IsSuccessStatusCode)
            {
                // 错误响应也可能回显题目或识别结果，不将响应正文写入日志。
                ErrorMessage = $"HTTP {(int)response.StatusCode}";
                LogError($"Pix2Text 识别失败：{ErrorMessage}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ExtractText(json);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException)
        {
            ErrorMessage = "识别服务返回的数据格式无效";
            LogError($"Pix2Text 识别失败：{ErrorMessage}");
            return null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            IsReady = false;
            LogError($"Pix2Text 识别失败：{ex}");
            return null;
        }
    }

    public bool Stop() => StopAsync().GetAwaiter().GetResult();

    public async Task<bool> StopAsync(CancellationToken cancellationToken = default)
    {
        CancelOperation();
        await _startLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stopped = StopManagedProcess();
            IsReady = false;
            ErrorMessage = null;
            if (TryGetEndpoint(out _, out var serviceUri, out _) &&
                await IsServiceReadyAsync(serviceUri, cancellationToken).ConfigureAwait(false))
            {
                ErrorMessage = "该端口的 Pix2Text 服务不属于当前软件进程，请在启动它的程序中停止";
                LogError(ErrorMessage);
                return false;
            }
            LogInfo(stopped ? "Pix2Text 服务已停止" : "当前软件没有运行中的 Pix2Text 服务进程");
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            LogError($"Pix2Text 停止失败：{exception}");
            return false;
        }
        finally { _startLock.Release(); }
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

    private string GetInstallRoot() => _installRoot;

    private async Task SaveExecutablePathAsync(string executablePath)
    {
        _settings.Pix2TextExecutablePath = Path.GetFullPath(executablePath);
        await _settings.SaveToJson();
    }

    private bool InstallationFailed(string message)
    {
        ErrorMessage = message;
        LogInstallation(message, LogLevel.Error);
        return false;
    }

    private async Task<bool> HasServeDependenciesAsync(string pix2TextExecutable, CancellationToken cancellationToken)
    {
        var python = Path.Combine(Path.GetDirectoryName(pix2TextExecutable)!, "python.exe");
        if (!File.Exists(python)) return false;
        var info = CreatePythonStartInfo(python);
        info.ArgumentList.Add("-I");
        info.ArgumentList.Add("-X");
        info.ArgumentList.Add("utf8");
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("import fastapi, uvicorn, multipart, pix2text.serve");
        using var session = new Pix2TextProcess(_processFactory(info), python);
        try
        {
            session.Start(() => { });
            var timer = Stopwatch.StartNew();
            while (!session.HasExited && timer.Elapsed < TimeSpan.FromSeconds(60))
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (session.HasExited && session.ExitCode == 0) return true;
            LogInstallation($"服务依赖检查失败（退出码 {session.ExitCode?.ToString() ?? "未退出"}）：{Environment.NewLine}" +
                await session.GetStartupDetailsAsync().ConfigureAwait(false), LogLevel.Error);
            return false;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            LogInstallation($"服务依赖检查失败：{exception}", LogLevel.Error);
            return false;
        }
    }

    private async Task RelayInstallerOutputAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
                LogInstallation(Regex.Replace(line.Trim(), @"^(?:\[[^\]]+\]\s*)?\[Pix2Text安装\]\s*", ""));
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
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
            "-I", "-X", "utf8", "-u", "-c", ServerBootstrap, listenAddress.ToString(), port.ToString()
        ];
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
    }

    private async void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is not (nameof(GlobalSettings.Pix2TextHost) or nameof(GlobalSettings.Pix2TextPort)))
            return;
        IsReady = false;
        CancelOperation();
        await _startLock.WaitAsync().ConfigureAwait(false);
        try { StopManagedProcess(); }
        catch (Exception exception) { LogError($"Pix2Text 配置变更后停止服务失败：{exception}"); }
        finally { _startLock.Release(); }
    }

    private bool StopManagedProcess()
    {
        var session = Interlocked.Exchange(ref _process, null);
        IsReady = false;
        if (session is null) return false;
        try
        {
            var stopped = session.Stop();
            session.Dispose();
            return stopped;
        }
        catch
        {
            // 停止失败时保留归属，下一次停止仍可处理；绝不去杀占用同一端口的其他进程。
            Interlocked.CompareExchange(ref _process, session, null);
            throw;
        }
    }

    private void LogInfo(string message) =>
        _logService.AddLog(LogLevel.Info, LogSource, message);

    private void LogError(string message) =>
        _logService.AddLog(LogLevel.Error, LogSource, message);

    private void LogInstallation(string message, LogLevel level = LogLevel.Info) =>
        _logService.AddLog(level, LogSource, $"[Pix2Text安装] {message}");

    public void Dispose()
    {
        lock (_operationSync)
        {
            if (_disposed) return;
            _disposed = true;
            _activeOperation?.Cancel();
        }
        _settings.PropertyChanged -= OnSettingsPropertyChanged;
        try { StopManagedProcess(); }
        catch (Exception exception) { LogError($"Pix2Text 关闭时清理进程失败：{exception}"); }
        _httpClient.Dispose();

        // 操作取消后仍需释放此锁，因此不在 Dispose 中提前销毁它。
    }
}
