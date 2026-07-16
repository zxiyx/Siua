using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Microsoft.Win32;
using Siua.Common;
using Siua.Interfaces;
using Siua.Core;

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
                await _page.WaitForURLAsync("**/mooc1.chaoxing.com/**", new PageWaitForURLOptions
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
        if (!string.Equals(_activePlatform, LearningPlatformCatalog.XueXiTong, StringComparison.Ordinal))
        {
            _logService.AddLog(LogLevel.Error, "Platform", "当前任务没有可用的平台适配器");
            return false;
        }

        var page = _page;
        if (page is null || !IsSessionActive)
        {
            return false;
        }
        try
        {
            var cancellationToken = GetSessionToken();
            cancellationToken.ThrowIfCancellationRequested();
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            var resolver = new PageResolver(page, _settings);
            if (!await resolver.WaitLoadingAsync())
            {
                _logService.AddLog("当前课程页面加载失败");
                return IsSessionActive;
            }

            await resolver.ResolvePageAsync();
            await ProcessVideosAsync(resolver.Videos, cancellationToken);
            await ProcessDocumentsAsync(resolver.Docs);

            if (resolver.HasTest && _settings.AutoTest &&
                !await ProcessTestsAsync(resolver, cancellationToken))
            {
                return false;
            }

            _logService.AddLog("进入下一节...");
            await resolver.NextPageAsync();
            await Task.Delay(_settings.ChapterJumpInterval, cancellationToken);
            return IsSessionActive;
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
            _logService.AddLog($"处理课程页面失败：{exception.Message}");
            return IsSessionActive;
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

    private async Task ProcessVideosAsync(
        IReadOnlyList<Video> videos,
        CancellationToken cancellationToken)
    {
        foreach (var video in videos)
        {
            if (_settings.JumpCompleted && await video.IsCompletedAsync())
            {
                continue;
            }

            _logService.AddLog("播放视频中...");
            await video.InitializeAsync();
            await video.PlayAsync(cancellationToken);

            if (_settings.TryFinishVideo)
            {
                var finished = await video.TryFinishAsync(cancellationToken);
                _logService.AddLog(finished
                    ? "操控视频进度成功，视频播放完毕"
                    : "操控视频进度失败，正常播放视频");
            }

            if (!await video.WaitForEndAsync(cancellationToken: cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logService.AddLog("视频结束检测超时");
            }
            _logService.AddLog("播放完毕");
        }
    }

    private async Task ProcessDocumentsAsync(IReadOnlyList<Doc> documents)
    {
        if (documents.Count == 0)
        {
            return;
        }

        _logService.AddLog("检测到文档...");
        foreach (var document in documents)
        {
            if (_settings.JumpCompleted && document.IsCompleted)
            {
                continue;
            }

            _logService.AddLog("完成文档中...");
            await document.ScrollToEndAsync();
            _logService.AddLog("完成文档");
        }
    }

    private async Task<bool> ProcessTestsAsync(
        PageResolver resolver,
        CancellationToken cancellationToken)
    {
        var imagePath = Path.Combine(_settings.UserDataDir, "q.png");
        try
        {
            foreach (var chapterTest in resolver.Tests)
            {
                await chapterTest.LoadQuestionsAsync();
                if (chapterTest.IsCompleted)
                {
                    _logService.AddLog("该章节测试已完成");
                }

                if (!chapterTest.HasQuestion || chapterTest.IsCompleted)
                {
                    continue;
                }

                foreach (var question in chapterTest.Questions)
                {
                    if (!await AnswerQuestionAsync(question, imagePath))
                    {
                        return false;
                    }

                    await Task.Delay(_settings.AiAnsweringInterval, cancellationToken);
                }

                await chapterTest.SubmitAnswerAsync();
                await resolver.ConfirmTestSubmissionAsync();
                _logService.AddLog("该章节测试提交成功");
                await Task.Delay(_settings.ChapterJumpInterval, cancellationToken);
            }

            return true;
        }
        finally
        {
            if (File.Exists(imagePath))
            {
                File.Delete(imagePath);
            }
        }
    }

    private async Task<bool> AnswerQuestionAsync(Question question, string imagePath)
    {
        await question.LoadAnswersAsync();
        var image = await question.CaptureImageAsync();
        if (image is null)
        {
            return DisableAutoTest("题目截图失败，已关闭自动答题");
        }

        await File.WriteAllBytesAsync(imagePath, image);
        var questionText = _settings.UsedAiToOcr
            ? await _aiControlService.GetTextFromImage(imagePath)
            : await _ocrService.RecognizeAsync(imagePath);

        if (questionText is null)
        {
            return DisableAutoTest(_settings.UsedAiToOcr
                ? "AIOCR 识图异常，已自动关闭自动答题"
                : "OCR 识图异常，已关闭自动答题并结束刷课");
        }

        var answer = await _aiControlService.GetAnswer(questionText);
        if (answer is null)
        {
            return DisableAutoTest("AI 配置异常，已关闭自动答题");
        }

        var selectedCount = 0;
        foreach (var option in question.Answers)
        {
            var optionText = await option.Key.InnerTextAsync();
            var optionMarker = await option.Value.InnerTextAsync();
            if (AnswerSelectsOption(answer, optionMarker, optionText))
            {
                await option.Key.ClickAsync();
                selectedCount++;
            }
        }

        return selectedCount > 0 ||
               DisableAutoTest("AI 返回的答案无法匹配任何选项，已关闭自动答题");
    }

    private bool DisableAutoTest(string message)
    {
        _settings.AutoTest = false;
        _logService.AddLog(message);
        return false;
    }

    private static bool AnswerSelectsOption(string answer, string marker, string optionText)
    {
        var markerCharacter = marker
            .Trim()
            .ToUpperInvariant()
            .FirstOrDefault(character => character is >= 'A' and <= 'H');

        if (markerCharacter != default &&
            answer.ToUpperInvariant().Contains(markerCharacter))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(optionText) &&
               answer.Contains(optionText.Trim(), StringComparison.OrdinalIgnoreCase);
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
                if (page is null ||
                    (!IsLoginPage(page.Url) && page.Url != "https://i.chaoxing.com/base"))
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

    private static bool IsLoginPage(string url)
    {
        return url.Contains(PassportUrlFragment, StringComparison.OrdinalIgnoreCase);
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
