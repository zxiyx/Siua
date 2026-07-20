using System;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Microsoft.Win32;
using Siua.Common;
using Siua.Interfaces;
using Siua.Core.Xxt;
using Siua.Core.Zhs;

namespace Siua.Services;

public sealed class CoreService : ICoreService
{
    private const string PassportUrlFragment = "passport";
    private static readonly string[] BrowserArguments =
    [
        "--disable-blink-features=AutomationControlled",
        "--disable-site-isolation-trials",
        "--no-default-browser-check"
    ];

    private readonly ILogService _logService;
    private readonly GlobalSettings _settings;
    private readonly Pix2TextService _ocrService;
    private readonly AiControlService _aiControlService;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;
    private CancellationTokenSource? _loginHeartbeatCts;
    private CancellationTokenSource? _sessionCts;
    private XxtRunner? _xxtRunner;
    private ZhsRunner? _zhsRunner;
    private string? _activePlatform;
    private string? _activeCourseUrl;
    private int _sessionEnded;
    private int _isDisposing;

    public CoreService(
        ILogService logService,
        GlobalSettings settings,
        Pix2TextService pix2TextService,
        AiControlService aiControlService)
    {
        _logService = logService;
        _settings = settings;
        _ocrService = pix2TextService;
        _aiControlService = aiControlService;
    }

    public async Task<bool> LoadPlaywright(string courseUrl)
    {
        DisposeBrowserResources();
        _sessionCts = new CancellationTokenSource();
        Interlocked.Exchange(ref _sessionEnded, 0);
        _logService.Clear();
        var platform = _settings.CurrentPlatform;
        if (!LearningPlatformCatalog.IsSupported(platform))
        {
            _logService.AddLog(LogLevel.Error, "Platform", $"平台「{platform}」尚未适配，无法启动任务");
            DisposeBrowserResources();
            return false;
        }

        if (!LearningPlatformCatalog.TryValidateCourseUrl(
                platform, courseUrl, out var validatedCourseUrl, out var courseError))
        {
            _logService.AddLog(LogLevel.Error, "Platform", $"「{platform}」课程地址无效：{courseError}");
            DisposeBrowserResources();
            return false;
        }

        _activePlatform = platform;
        _activeCourseUrl = validatedCourseUrl;
        _logService.AddLog($"正在启动「{platform}」任务");

        try
        {
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(CreateBrowserOptions());
            _page = await _browser.NewPageAsync();
            RegisterSessionEvents(_browser, _page, _sessionCts);
            _page.Console += (_, message) => _logService.AddLog($"[Browser] {message.Text}");

            await _page.GotoAsync(validatedCourseUrl);
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            if (IsLoginPage(_page.Url))
            {
                _logService.AddLog("检测到登录界面，请先登录");
                _logService.AddLog("等待登录中...");
                await _page.WaitForURLAsync(GetAuthenticatedUrlPattern(), new PageWaitForURLOptions
                {
                    Timeout = 300_000
                });
            }

            if (!IsSessionActive || IsLoginPage(_page.Url))
            {
                _logService.AddLog("登录状态异常，请重新启动");
                return false;
            }

            _logService.AddLog("登录成功");
            StartLoginHeartbeat();
            return true;
        }
        catch (Exception exception) when (!IsSessionActive || exception is PlaywrightException)
        {
            HandleSessionEnded("浏览器已关闭或连接中断，任务已终止");
            return false;
        }
        catch (Exception exception)
        {
            _logService.AddLog($"浏览器启动失败：{exception.Message}");
            DisposeBrowserResources();
            return false;
        }
    }

    public bool IsSessionActive =>
        _sessionCts is { IsCancellationRequested: false } &&
        _browser is { IsConnected: true } &&
        _page is { IsClosed: false };

    public async Task<bool> ParsePage()
    {
        return _activePlatform switch
        {
            LearningPlatformCatalog.XueXiTong => await RunXxtAsync(),
            LearningPlatformCatalog.ZhiHuiShu => await RunZhsAsync(),
            _ => LogUnsupportedPlatform()
        };
    }

    private bool LogUnsupportedPlatform()
    {
        _logService.AddLog(LogLevel.Error, "Platform", "当前任务没有可用的平台适配器");
        return false;
    }

    private async Task<bool> RunZhsAsync()
    {
        var page = _page;
        if (page is null || !IsSessionActive)
        {
            return false;
        }

        try
        {
            var cancellationToken = GetSessionToken();
            _zhsRunner ??= new ZhsRunner(
                page,
                _settings,
                _logService,
                _ocrService,
                _aiControlService);
            await _zhsRunner.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (!IsSessionActive)
        {
            return false;
        }
        catch (PlaywrightException exception) when (!IsSessionActive)
        {
            HandleSessionEnded($"浏览器连接已中断：{exception.Message}");
        }
        catch (Exception exception)
        {
            _logService.AddLog(
                LogLevel.Error,
                "Zhs",
                $"处理智慧树课程失败：{exception.Message}");
        }

        // 智慧树适配器一次处理整门课程，结束外层逐页循环。
        return false;
    }

    private async Task<bool> RunXxtAsync()
    {
        var page = _page;
        if (page is null || !IsSessionActive)
        {
            return false;
        }
        try
        {
            var cancellationToken = GetSessionToken();
            _xxtRunner ??= new XxtRunner(
                page,
                _settings,
                _logService,
                _ocrService,
                _aiControlService);
            return await _xxtRunner.RunAsync(cancellationToken) && IsSessionActive;
        }
        catch (OperationCanceledException) when (!IsSessionActive)
        {
            return false;
        }
        catch (PlaywrightException exception) when (!IsSessionActive)
        {
            HandleSessionEnded($"浏览器连接已中断：{exception.Message}");
            return false;
        }
        catch (Exception exception)
        {
            _logService.AddLog(
                LogLevel.Error,
                "Xxt",
                $"处理学习通课程页面失败：{exception.Message}");
            // Runner 已经自行跳过可恢复的任务点异常；到达这里表示当前页无法安全继续，
            // 直接结束可避免外层循环反复处理同一页面。
            return false;
        }
    }

    public void StopLoginHeartbeat()
    {
        var heartbeat = Interlocked.Exchange(ref _loginHeartbeatCts, null);
        if (heartbeat is null)
        {
            return;
        }

        heartbeat.Cancel();
        heartbeat.Dispose();
    }

    public void Dispose()
    {
        StopLoginHeartbeat();
        DisposeBrowserResources();
    }

    private BrowserTypeLaunchOptions CreateBrowserOptions()
    {
        var options = new BrowserTypeLaunchOptions
        {
            Headless = false,
            Args = BrowserArguments
        };

        if (_settings.BrowserCannel == "系统默认")
        {
            var browserPath = GetDefaultBrowserPath();
            if (browserPath is not null)
            {
                options.ExecutablePath = browserPath;
            }
        }
        else
        {
            options.Channel = _settings.BrowserCannel.ToLowerInvariant() switch
            {
                "edge" => "msedge",
                "chrome" => "chrome",
                _ => null
            };
        }

        return options;
    }

    private void RegisterSessionEvents(
        IBrowser browser,
        IPage page,
        CancellationTokenSource session)
    {
        browser.Disconnected += (_, _) =>
            HandleSessionEnded(session, "检测到浏览器已关闭，当前任务已终止");
        page.Close += (_, _) =>
            HandleSessionEnded(session, "检测到课程页面已关闭，当前任务已终止");
        page.Crash += (_, _) =>
            HandleSessionEnded(session, "检测到课程页面崩溃，当前任务已终止");
    }

    private void HandleSessionEnded(string reason)
    {
        var session = _sessionCts;
        if (session is not null)
        {
            HandleSessionEnded(session, reason);
        }
    }

    private void HandleSessionEnded(CancellationTokenSource session, string reason)
    {
        if (!ReferenceEquals(_sessionCts, session))
        {
            return;
        }

        try
        {
            session.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        StopLoginHeartbeat();
        if (Volatile.Read(ref _isDisposing) == 0 &&
            Interlocked.Exchange(ref _sessionEnded, 1) == 0)
        {
            _logService.AddLog(reason);
        }
    }

    private CancellationToken GetSessionToken()
    {
        return _sessionCts?.Token ?? new CancellationToken(canceled: true);
    }

    private void StartLoginHeartbeat()
    {
        StopLoginHeartbeat();
        var heartbeat = new CancellationTokenSource();
        _loginHeartbeatCts = heartbeat;
        _ = RunLoginHeartbeatAsync(heartbeat.Token);
    }

    private async Task RunLoginHeartbeatAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(300_000, cancellationToken);
                var page = _page;
                if (page is null || !RequiresLoginRecovery(page.Url))
                {
                    continue;
                }

                _logService.AddLog("检测到登录态失效，尝试恢复...");
                await page.GotoAsync(GetCourseUrl());
                await page.WaitForLoadStateAsync(LoadState.Load, new PageWaitForLoadStateOptions
                {
                    Timeout = 30_000
                });
                _logService.AddLog(IsLoginPage(page.Url)
                    ? "需要手动重新登录"
                    : "登录态已恢复，继续刷课");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logService.AddLog($"[心跳] 检测异常：{exception.Message}");
            }
        }
    }

    private string GetCourseUrl()
    {
        return _activeCourseUrl
            ?? throw new InvalidOperationException("当前任务没有有效的课程地址。");
    }

    private bool IsLoginPage(string url)
    {
        return _activePlatform switch
        {
            LearningPlatformCatalog.XueXiTong =>
                url.Contains(PassportUrlFragment, StringComparison.OrdinalIgnoreCase),
            LearningPlatformCatalog.ZhiHuiShu =>
                url.Contains("login", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private string GetAuthenticatedUrlPattern()
    {
        return _activePlatform switch
        {
            LearningPlatformCatalog.XueXiTong => "**/mooc1.chaoxing.com/**",
            LearningPlatformCatalog.ZhiHuiShu => "**/wisdom-mooc.zhihuishu.com/**",
            _ => throw new InvalidOperationException("当前任务没有有效的平台适配器。")
        };
    }

    private bool RequiresLoginRecovery(string url)
    {
        return IsLoginPage(url) ||
               (string.Equals(_activePlatform, LearningPlatformCatalog.XueXiTong, StringComparison.Ordinal) &&
                string.Equals(url, "https://i.chaoxing.com/base", StringComparison.OrdinalIgnoreCase));
    }

    private string? GetDefaultBrowserPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(@"http\shell\open\command");
            return ParseExecutablePath(key?.GetValue("")?.ToString());
        }
        catch (UnauthorizedAccessException exception)
        {
            _logService.AddLog($"[注册表] 权限不足：{exception.Message}");
        }
        catch (SecurityException exception)
        {
            _logService.AddLog($"[注册表] 安全异常：{exception.Message}");
        }
        catch (Exception exception)
        {
            _logService.AddLog($"[注册表] 读取失败 [{exception.GetType().Name}]：{exception.Message}");
        }

        return null;
    }

    private static string? ParseExecutablePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var trimmedCommand = command.Trim();
        string path;
        if (trimmedCommand.StartsWith('"'))
        {
            var closingQuote = trimmedCommand.IndexOf('"', 1);
            if (closingQuote <= 1)
            {
                return null;
            }

            path = trimmedCommand[1..closingQuote];
        }
        else
        {
            path = trimmedCommand.Split([' ', '\t'], 2)[0];
            if (!path.Contains(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return File.Exists(path) ? path : null;
    }

    private void DisposeBrowserResources()
    {
        Interlocked.Exchange(ref _isDisposing, 1);
        try
        {
            var session = Interlocked.Exchange(ref _sessionCts, null);
            session?.Cancel();
            StopLoginHeartbeat();
            _page = null;
            _browser = null;
            _xxtRunner = null;
            _zhsRunner = null;
            _activePlatform = null;
            _activeCourseUrl = null;
            _playwright?.Dispose();
            _playwright = null;
            session?.Dispose();
        }
        finally
        {
            Interlocked.Exchange(ref _isDisposing, 0);
        }
    }
}
