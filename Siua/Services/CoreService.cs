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

/// <summary>协调浏览器生命周期与各学习平台任务。</summary>
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
    private bool _loginInProgress;
    // 登录恢复不能与刷课任务同时导航同一个页面。
    private readonly SemaphoreSlim _zhsPageGate = new(1, 1);

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
        var autoLogin = _settings.Login.Enabled;
        var loginAccount = autoLogin ? _settings.GetSelectedLoginAccount() : null;
        if (autoLogin && loginAccount is null)
        {
            _logService.AddLog("自动登录尚未选择有效账号，请在开始页选择账号或关闭自动登录");
            DisposeBrowserResources();
            return false;
        }
        _logService.AddLog($"正在启动「{platform}」任务");

        try
        {
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(CreateBrowserOptions());
            _page = await _browser.NewPageAsync();
            RegisterSessionEvents(_browser, _page, _sessionCts);
            _loginInProgress = true;
            _page.Console += (_, message) =>
            {
                // 登录页的第三方脚本可能打印凭据，登录期间不转发浏览器控制台。
                if (!_loginInProgress && _page is { } page && !IsLoginPage(page.Url))
                    _logService.AddLog($"[Browser] {message.Text}");
            };

            if (platform == LearningPlatformCatalog.ZhiHuiShu)
            {
                await _zhsPageGate.WaitAsync(GetSessionToken());
                try
                {
                    await ZhsLoginNavigator.OpenCourseAsync(_page, validatedCourseUrl,
                        message => _logService.AddLog(message), GetSessionToken(),
                        submitLogin: loginAccount is null ? null :
                            (page, token) => SubmitSavedLoginAsync(page, platform, loginAccount, token));
                }
                finally { _zhsPageGate.Release(); }
            }
            else
            {
                await _page.GotoAsync(validatedCourseUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });

                if (IsLoginPage(_page.Url))
                {
                    if (loginAccount is not null)
                    {
                        await SubmitSavedLoginAsync(_page, platform, loginAccount, GetSessionToken());
                    }
                    else _logService.AddLog("检测到登录界面，请先登录");
                    _logService.AddLog("等待登录中...");
                    await _page.WaitForURLAsync(GetAuthenticatedUrlPattern(), new PageWaitForURLOptions
                    {
                        Timeout = 300_000,
                        WaitUntil = WaitUntilState.DOMContentLoaded
                    }).WaitAsync(GetSessionToken());
                }
            }

            if (!IsSessionActive || IsLoginPage(_page.Url))
            {
                _logService.AddLog("登录状态异常，请重新启动");
                return false;
            }

            _logService.AddLog(platform == LearningPlatformCatalog.ZhiHuiShu
                ? "登录成功，目标课程已加载" : "登录成功");
            _loginInProgress = false;
            StartLoginHeartbeat();
            return true;
        }
        catch (OperationCanceledException) when (!IsSessionActive)
        {
            return false;
        }
        catch (Exception exception) when (!IsSessionActive || exception is PlaywrightException)
        {
            _logService.AddLog(LogLevel.Error, "Browser", $"浏览器启动失败：{exception}");
            HandleSessionEnded("浏览器已关闭或连接中断，任务已终止");
            return false;
        }
        catch (Exception exception)
        {
            _logService.AddLog(LogLevel.Error, "Browser", $"浏览器启动失败：{exception}");
            DisposeBrowserResources();
            return false;
        }
    }

    public bool IsSessionActive =>
        _sessionCts is { IsCancellationRequested: false } &&
        _browser is { IsConnected: true } &&
        _page is { IsClosed: false };

    private async Task SubmitSavedLoginAsync(IPage page, string platform, LoginAccount account,
        CancellationToken cancellationToken)
    {
        string? password = null;
        try { password = AccountPasswordProtector.Unprotect(account.EncryptedPassword); }
        catch { _logService.AddLog("已保存的密码无法读取，请重新编辑账号；本次可在浏览器中手动登录"); }
        if (string.IsNullOrEmpty(password)) return;
        if (platform == LearningPlatformCatalog.ZhiHuiShu)
            await ZhsLoginAutomation.SubmitOnceAsync(page, account.Username, password, _logService, cancellationToken);
        else if (platform == LearningPlatformCatalog.XueXiTong)
            await XxtLoginAutomation.SubmitOnceAsync(page, account.Username, password, _logService, cancellationToken);
    }

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

        var entered = false;
        try
        {
            var cancellationToken = GetSessionToken();
            await _zhsPageGate.WaitAsync(cancellationToken);
            entered = true;
            if (!ReferenceEquals(page, _page) || !IsSessionActive) return false;
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
            _logService.AddLog(LogLevel.Error, "Browser", $"浏览器连接已中断：{exception}");
            HandleSessionEnded("浏览器连接已中断，任务已终止");
        }
        catch (Exception exception)
        {
            _logService.AddLog(
                LogLevel.Error,
                "Zhs",
                $"处理智慧树课程失败：{exception}");
        }
        finally
        {
            if (entered) _zhsPageGate.Release();
        }

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
            _logService.AddLog(LogLevel.Error, "Browser", $"浏览器连接已中断：{exception}");
            HandleSessionEnded("浏览器连接已中断，任务已终止");
            return false;
        }
        catch (Exception exception)
        {
            _logService.AddLog(
                LogLevel.Error,
                "Xxt",
                $"处理学习通课程页面失败：{exception}");
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

                if (_activePlatform == LearningPlatformCatalog.ZhiHuiShu)
                {
                    if (!await _zhsPageGate.WaitAsync(0, cancellationToken))
                    {
                        _logService.AddLog("智慧树登录态失效，请完成登录后重新启动任务；当前任务执行中不自动跳转页面");
                        continue;
                    }
                    try
                    {
                        if (!ReferenceEquals(page, _page)) continue;
                        _loginInProgress = true;
                        await ZhsLoginNavigator.OpenCourseAsync(page, GetCourseUrl(),
                            message => _logService.AddLog(message), cancellationToken);
                        _logService.AddLog("智慧树登录验证通过，目标课程已重新加载，请重新启动任务");
                    }
                    finally
                    {
                        if (ReferenceEquals(page, _page)) _loginInProgress = false;
                        _zhsPageGate.Release();
                    }
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
                _logService.AddLog(LogLevel.Error, "Browser", $"[心跳] 检测异常：{exception}");
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
                ZhsLoginNavigator.IsAuthenticationPage(url),
            _ => false
        };
    }

    private string GetAuthenticatedUrlPattern()
    {
        return _activePlatform switch
        {
            LearningPlatformCatalog.XueXiTong => "**/mooc1.chaoxing.com/**",
            //LearningPlatformCatalog.XueXiTong => "**/{mooc1,mooc2-ans}.chaoxing.com/**",
            _ => throw new InvalidOperationException("当前任务没有有效的平台适配器。")
        };
    }

    private bool RequiresLoginRecovery(string url)
    {
        if (_activePlatform == LearningPlatformCatalog.ZhiHuiShu)
            return ZhsLoginNavigator.IsAuthenticationPage(url) || ZhsLoginNavigator.IsPersonalCoursePage(url);
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
            _logService.AddLog(LogLevel.Error, "Browser", $"[注册表] 权限不足：{exception}");
        }
        catch (SecurityException exception)
        {
            _logService.AddLog(LogLevel.Error, "Browser", $"[注册表] 安全异常：{exception}");
        }
        catch (Exception exception)
        {
            _logService.AddLog(LogLevel.Error, "Browser", $"[注册表] 读取失败：{exception}");
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
