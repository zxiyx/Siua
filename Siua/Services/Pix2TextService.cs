using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Siua.Common;
using Siua.Interfaces;

namespace Siua.Services;

public sealed class Pix2TextService : IDisposable
{
    private readonly GlobalSettings _settings;
    private readonly ILogService _logService;
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private readonly HttpClient _httpClient;
    private Process? _process;

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

    public async Task<bool> EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetEndpoint(out var listenAddress, out var serviceUri, out var endpointError))
        {
            ErrorMessage = endpointError;
            _logService.AddLog(LogLevel.Error, "OCR", endpointError);
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
                _logService.AddLog(LogLevel.Error, "OCR", endpointError);
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
                ErrorMessage = "未找到 Pix2Text Runtime。请将运行环境放到 Pix2TextRuntime\\Scripts\\p2t.exe。";
                _logService.AddLog(ErrorMessage);
                return false;
            }

            _logService.AddLog($"正在启动 Pix2Text（{serviceUri.Authority}），首次加载模型可能需要较长时间...");
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
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
                    _logService.AddLog($"[Pix2Text] {e.Data}");
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    _logService.AddLog($"[Pix2Text] {e.Data}");
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
                    _logService.AddLog("Pix2Text 已就绪，支持普通文字和数学公式识别");
                    return true;
                }

                await Task.Delay(500, cancellationToken);
            }

            ErrorMessage = process.HasExited
                ? $"Pix2Text 启动失败，进程退出码：{process.ExitCode}"
                : "Pix2Text 模型加载超时";
            _logService.AddLog(ErrorMessage);
            return false;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _logService.AddLog(LogLevel.Error,"OCR",$"Pix2Text 启动失败：{ex.Message}");
            return false;
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task<string?> RecognizeAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureReadyAsync(cancellationToken))
            return null;

        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent("text_formula"), "file_type");
            form.Add(new StringContent("768"), "resized_shape");
            form.Add(new StringContent(" $,$ "), "embed_sep");
            form.Add(new StringContent("$$\n,\n$$"), "isolated_sep");

            var imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
            using var image = new ByteArrayContent(imageBytes);
            image.Headers.ContentType = new MediaTypeHeaderValue(GetMediaType(imagePath));
            form.Add(image, "image", Path.GetFileName(imagePath));

            if (!TryGetEndpoint(out _, out var serviceUri, out var endpointError))
                throw new InvalidOperationException(endpointError);

            using var response = await _httpClient.PostAsync(
                new Uri(serviceUri, "pix2text"), form, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ExtractText(json);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            IsReady = false;
            _logService.AddLog($"Pix2Text 识别失败：{ex.Message}");
            return null;
        }
    }

    private string? ResolveExecutablePath()
    {
        var configured = _settings.Pix2TextExecutablePath;
        var candidates = new[]
        {
            Path.IsPathRooted(configured) ? configured : Path.Combine(AppContext.BaseDirectory, configured),
            Path.Combine(Directory.GetCurrentDirectory(), configured),
            Path.Combine(Directory.GetCurrentDirectory(), "tools", "pix2text", ".venv", "Scripts", "p2t.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
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
            return results.GetString()?.Trim();

        if (results.ValueKind != JsonValueKind.Array)
            return null;

        return string.Join("\n", results.EnumerateArray()
            .Select(item => item.TryGetProperty("text", out var text) ? text.GetString() : null)
            .Where(text => !string.IsNullOrWhiteSpace(text))).Trim();
    }

    private static string GetMediaType(string path) =>
        Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            ? "image/jpeg"
            : "image/png";

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
            "serve", "-l", "en,ch_sim", "-H", listenAddress.ToString(),
            "-p", port.ToString(), "-d", "cpu", "--disable-table"
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

    private void StopManagedProcess()
    {
        var process = Interlocked.Exchange(ref _process, null);
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    public void Dispose()
    {
        _settings.PropertyChanged -= OnSettingsPropertyChanged;
        StopManagedProcess();
        _httpClient.Dispose();
        _startLock.Dispose();
    }
}
