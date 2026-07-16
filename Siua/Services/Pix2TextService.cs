using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
            BaseAddress = new Uri($"http://127.0.0.1:{settings.Pix2TextPort}/"),
            Timeout = TimeSpan.FromMinutes(2)
        };
    }

    public async Task<bool> EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        if (IsReady || await IsServiceReadyAsync(cancellationToken))
        {
            IsReady = true;
            return true;
        }

        await _startLock.WaitAsync(cancellationToken);
        try
        {
            if (IsReady || await IsServiceReadyAsync(cancellationToken))
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

            _logService.AddLog("正在启动 Pix2Text，本次首次加载模型可能需要较长时间...");
            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = $"serve -l en,ch_sim -H 127.0.0.1 -p {_settings.Pix2TextPort} -d cpu --disable-table",
                    WorkingDirectory = Path.GetDirectoryName(executable)!,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };
            _process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    _logService.AddLog($"[Pix2Text] {e.Data}");
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    _logService.AddLog($"[Pix2Text] {e.Data}");
            };
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            for (var i = 0; i < 240; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_process.HasExited)
                    break;

                if (await IsServiceReadyAsync(cancellationToken))
                {
                    IsReady = true;
                    ErrorMessage = null;
                    _logService.AddLog("Pix2Text 已就绪，支持普通文字和数学公式识别");
                    return true;
                }

                await Task.Delay(500, cancellationToken);
            }

            ErrorMessage = _process.HasExited
                ? $"Pix2Text 启动失败，进程退出码：{_process.ExitCode}"
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

            using var response = await _httpClient.PostAsync("pix2text", form, cancellationToken);
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

    private async Task<bool> IsServiceReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(1));
            using var response = await _httpClient.GetAsync("openapi.json", timeout.Token);
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

    public void Dispose()
    {
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
        }

        _process?.Dispose();
        _httpClient.Dispose();
        _startLock.Dispose();
    }
}
