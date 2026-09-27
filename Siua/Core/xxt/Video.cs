using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core.Xxt;

/// <summary>封装学习通视频任务的播放与完成检测。</summary>
public sealed class XxtVideo
{
    private const string IncompleteIconSelector =
        "div.ans-job-icon.ans-job-icon-clear[aria-label='任务点未完成']";
    private const string EndedClass = "vjs-ended";
    private const string PausedClass = "vjs-paused";
    private const string StartedClass = "vjs-has-started";
    private const int PlayClickTimeout = 1500;

    private readonly ILocator _container;
    private readonly GlobalSettings _settings;
    private ILocator? _player;
    private ILocator? _videoElement;
    private ILocator? _playButton;
    private ILocator? _bigPlayButton;
    private bool _usePlaybackApi;

    public XxtVideo(ILocator container, GlobalSettings settings)
    {
        _container = container;
        _settings = settings;
    }

    public async Task<bool> IsCompletedAsync()
    {
        return await _container.Locator(IncompleteIconSelector).CountAsync() == 0;
    }

    public async Task InitializeAsync()
    {
        _usePlaybackApi = false;
        var iframeLocator = _container.Locator("iframe").First;
        await iframeLocator.WaitForAsync();
        var iframeElement = await iframeLocator.ElementHandleAsync()
            ?? throw new PlaywrightException("无法获取视频 iframe 元素。");
        var frame = await iframeElement.ContentFrameAsync()
            ?? throw new PlaywrightException("无法进入视频 iframe。");
        _player = frame.Locator("#video").First;
        _videoElement = frame.Locator("#reader video.vjs-tech").First;
        _playButton = frame.Locator(".vjs-play-control").First;
        _bigPlayButton = frame.Locator(".vjs-big-play-button").First;

        await _player.WaitForAsync();
        await _videoElement.WaitForAsync();
    }

    public async Task PlayAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var playerClass = await GetPlayerClassAsync();
        if (HasClass(playerClass, EndedClass))
            return;

        // 在首次播放前应用静音设置，避免 API 后备播放受到自动播放策略限制。
        await ApplyPlaybackSettingsAsync();

        if (!HasClass(playerClass, StartedClass) &&
            await IsUsableAsync(GetBigPlayButton()))
        {
            await ClickPlayOrUseApiAsync(GetBigPlayButton(), cancellationToken);
        }

        if (HasClass(await GetPlayerClassAsync(), PausedClass))
        {
            await ResumeAsync(cancellationToken);
        }
    }

    public async Task<bool> TryFinishAsync(CancellationToken cancellationToken = default)
    {
        var video = GetVideoElement();
        var duration = await WaitForDurationAsync(video, cancellationToken);
        if (duration is null)
        {
            return false;
        }
        await video.EvaluateAsync("(element, time) => element.currentTime = time", duration.Value * 0.999);
        await ApplyPlaybackSettingsAsync();
        await ResumeAsync(cancellationToken);
        await Task.Delay(500, cancellationToken);

        var currentTime = await video.EvaluateAsync<double>("element => element.currentTime");
        return currentTime / duration.Value >= 0.99;
    }

    public async Task<bool> WaitForEndAsync(
        int timeout = 3_600_000,
        CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var playerClass = await GetPlayerClassAsync();
            if (HasClass(playerClass, EndedClass))
            {
                return true;
            }
            await ApplyPlaybackSettingsAsync();
            if (HasClass(playerClass, PausedClass))
            {
                await ResumeAsync(cancellationToken);
            }

            await Task.Delay(1000, cancellationToken);
        }

        return HasClass(await GetPlayerClassAsync(), EndedClass);
    }

    private async Task ResumeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var playerClass = await GetPlayerClassAsync();
        if (HasClass(playerClass, EndedClass) || !HasClass(playerClass, PausedClass))
        {
            return;
        }

        var playButton = GetPlayButton();
        if (!_usePlaybackApi && await IsUsableAsync(playButton))
        {
            await ClickPlayOrUseApiAsync(playButton, cancellationToken);
            return;
        }

        await PlayThroughApiAsync(cancellationToken);
    }

    private async Task ClickPlayOrUseApiAsync(ILocator button, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await button.ClickAsync(new LocatorClickOptions { Timeout = PlayClickTimeout });
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (TimeoutException)
        {
            // 视频弹题浮层可能截获鼠标事件，即使播放按钮可见也无法点击。
            // 使用原播放器的播放接口，不移除浮层或伪造任务完成状态。
            _usePlaybackApi = true;
            await PlayThroughApiAsync(cancellationToken);
        }
    }

    private async Task PlayThroughApiAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resumed = await GetVideoElement().EvaluateAsync<bool>("""
            async element => {
                if (element.ended || !element.paused) return true;
                const win = element.ownerDocument.defaultView;
                const root = element.closest('.video-js, #video');
                const player = root?.id && typeof win.videojs?.getPlayer === 'function'
                    ? win.videojs.getPlayer(root.id) : null;
                let timer;
                try {
                    // 复用已创建的 Video.js 实例，旧播放器则使用原生 media.play()。
                    // play() 可能长期 pending，因此只等待有限时间并核对真实播放状态。
                    const playback = player ? player.play() : element.play();
                    await Promise.race([
                        Promise.resolve(playback),
                        new Promise(resolve => { timer = setTimeout(resolve, 1500); })
                    ]);
                    return element.ended || !element.paused;
                } catch {
                    return false;
                } finally {
                    clearTimeout(timer);
                }
            }
            """).WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!resumed)
            throw new InvalidOperationException("视频未能恢复播放，请检查视频弹题或浏览器播放限制。");
    }

    private async Task ApplyPlaybackSettingsAsync()
    {
        await GetVideoElement().EvaluateAsync(
            "(element, options) => { element.playbackRate = options.rate; element.muted = options.muted; }",
            new { rate = _settings.VideoPlayRate, muted = _settings.IsMuted });
    }

    private static async Task<double?> WaitForDurationAsync(
        ILocator video,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var duration = await video.EvaluateAsync<double>("element => element.duration");
            if (double.IsFinite(duration) && duration > 0)
            {
                return duration;
            }

            await Task.Delay(250, cancellationToken);
        }

        return null;
    }

    private async Task<string> GetPlayerClassAsync()
    {
        return await GetPlayer().GetAttributeAsync("class") ?? string.Empty;
    }

    private static bool HasClass(string classNames, string expected)
    {
        return classNames.Contains(expected, StringComparison.Ordinal);
    }

    private static async Task<bool> IsUsableAsync(ILocator locator)
    {
        return await locator.CountAsync() > 0 && await locator.IsVisibleAsync();
    }

    private ILocator GetPlayer() =>
        _player ?? throw new InvalidOperationException("视频尚未初始化。");

    private ILocator GetVideoElement() =>
        _videoElement ?? throw new InvalidOperationException("视频尚未初始化。");

    private ILocator GetPlayButton() =>
        _playButton ?? throw new InvalidOperationException("视频尚未初始化。");

    private ILocator GetBigPlayButton() =>
        _bigPlayButton ?? throw new InvalidOperationException("视频尚未初始化。");
}
