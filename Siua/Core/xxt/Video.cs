using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core;

public sealed class Video
{
    private const string IncompleteIconSelector =
        "div.ans-job-icon.ans-job-icon-clear[aria-label='任务点未完成']";
    private const string EndedClass = "vjs-ended";
    private const string PausedClass = "vjs-paused";
    private const string StartedClass = "vjs-has-started";

    private readonly ILocator _container;
    private readonly GlobalSettings _settings;
    private ILocator? _player;
    private ILocator? _videoElement;
    private ILocator? _playButton;
    private ILocator? _bigPlayButton;

    public Video(ILocator container, GlobalSettings settings)
    {
        _container = container;
        _settings = settings;
    }

    public async Task<bool> IsCompletedAsync()
    {
        try
        {
            return await _container.Locator(IncompleteIconSelector).CountAsync() == 0;
        }
        catch (PlaywrightException)
        {
            return true;
        }
    }

    public async Task InitializeAsync()
    {
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

        if (!HasClass(playerClass, StartedClass) &&
            await IsUsableAsync(GetBigPlayButton()))
        {
            await GetBigPlayButton().ClickAsync();
        }

        await ApplyPlaybackSettingsAsync();
        if (HasClass(await GetPlayerClassAsync(), PausedClass))
        {
            await ResumeAsync();
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
        await ResumeAsync();
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
                await ResumeAsync();
            }

            await Task.Delay(1000, cancellationToken);
        }

        return HasClass(await GetPlayerClassAsync(), EndedClass);
    }

    private async Task ResumeAsync()
    {
        if (!HasClass(await GetPlayerClassAsync(), PausedClass))
        {
            return;
        }

        var playButton = GetPlayButton();
        if (await IsUsableAsync(playButton))
        {
            await playButton.ClickAsync(new LocatorClickOptions { Timeout = 3000 });
        }
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