using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core.Zhs;

public sealed class Video
{
    private readonly ILocator _video;

    public Video(ILocator video)
    {
        _video = video;
    }

    public async Task PlayAsync(
        double playbackRate,
        bool muted,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _video.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 20_000
        });
        await ApplyPlaybackSettingsAsync(playbackRate, muted, resume: true);
    }

    public async Task<bool> TryFinishAsync(CancellationToken cancellationToken = default)
    {
        var duration = await WaitForDurationAsync(cancellationToken);
        if (duration is null)
            return false;

        await _video.EvaluateAsync(
            "(video, target) => { video.currentTime = target; }",
            duration.Value * 0.995);
        await Task.Delay(500, cancellationToken);
        return await HasEndedAsync();
    }

    public async Task<bool> WaitForEndAsync(
        double playbackRate,
        bool muted,
        int timeoutMilliseconds = 3_600_000,
        CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < timeoutMilliseconds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await HasEndedAsync())
                return true;

            await ApplyPlaybackSettingsAsync(playbackRate, muted, resume: true);
            await Task.Delay(1_000, cancellationToken);
        }

        return await HasEndedAsync();
    }

    private Task ApplyPlaybackSettingsAsync(double playbackRate, bool muted, bool resume) =>
        _video.EvaluateAsync(
            """
            (video, options) => {
                video.muted = options.muted;
                video.playbackRate = options.rate;
                if (options.resume && video.paused && !video.ended) {
                    video.play().catch(() => {});
                }
            }
            """,
            new { rate = playbackRate, muted, resume });

    private Task<bool> HasEndedAsync() =>
        _video.EvaluateAsync<bool>(
            """
            video => video.ended ||
                (Number.isFinite(video.duration) && video.duration > 0 &&
                 video.currentTime >= video.duration - 0.5)
            """);

    private async Task<double?> WaitForDurationAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var duration = await _video.EvaluateAsync<double>("video => video.duration");
            if (double.IsFinite(duration) && duration > 0)
                return duration;

            await Task.Delay(250, cancellationToken);
        }

        return null;
    }
}
