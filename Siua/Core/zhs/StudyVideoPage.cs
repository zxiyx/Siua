using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core.Zhs;

/// <summary>新版累计时长课程：通过目录完成标记确认学习结果。</summary>
public sealed class ZhsStudyVideoPage(IPage page)
{
    private ILocator Video => page.Locator("#vjs_container_html5_api").First;

    public async Task CloseStudyPopupsAsync(CancellationToken cancellationToken = default)
    {
        // 只关闭这两种学习提示，不能关闭提交确认或成绩窗口。
        for (var pass = 0; pass < 3; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var closed = false;
            var pending = false;
            foreach (var selector in new[] { ".dialog-read", "#playTopic-dialog" })
            {
                var dialog = page.Locator(selector).First;
                if (await dialog.CountAsync() == 0 || !await dialog.IsVisibleAsync())
                    continue;
                pending = true;
                var close = dialog.Locator(selector == ".dialog-read"
                    ? ".iconguanbi" : ".dialog-footer .btn, button[aria-label='Close']").Last;
                // 两个弹窗可能叠在一起；被挡住时先尝试另一个关闭按钮。
                try { await close.ClickAsync(new LocatorClickOptions { Timeout = 1_000 }); }
                catch (TimeoutException) { continue; }
                await dialog.WaitForAsync(new LocatorWaitForOptions
                { State = WaitForSelectorState.Hidden, Timeout = 5_000 });
                closed = true;
            }
            if (!pending) return;
            if (!closed)
                throw new InvalidOperationException("智慧树学习提示窗口无法关闭，请检查页面提示。");
        }
    }

    public async Task PauseAsync()
    {
        if (await Video.CountAsync() > 0)
            await Video.EvaluateAsync("v => v.pause()");
    }

    public async Task PlaySectionAsync(ZhsSection section, bool muted,
        CancellationToken cancellationToken = default, int timeoutMilliseconds = 7_200_000,
        int reportWaitMilliseconds = 15_000)
    {
        await CloseStudyPopupsAsync(cancellationToken);
        var wasCurrent = await section.IsCurrentAsync();
        var previous = await Video.CountAsync() > 0 ? await Video.ElementHandleAsync() : null;
        try
        {
            var previousSource = previous is null ? "" : await previous.EvaluateAsync<string>(
                "v => v.currentSrc || v.src || ''");
            if (!wasCurrent)
                await section.ClickAsync(cancellationToken);
            var loadTimer = Stopwatch.StartNew();
            var ready = false;
            while (loadTimer.ElapsedMilliseconds < 20_000)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await CloseStudyPopupsAsync(cancellationToken);
                if (await section.IsCurrentAsync() && await Video.CountAsync() > 0 &&
                    await Video.EvaluateAsync<bool>("v => v.readyState >= 1 && Number.isFinite(v.duration) && v.duration > 0"))
                {
                    var changed = wasCurrent || previous is null ||
                        await previous.EvaluateAsync<bool>("v => v !== document.querySelector('#vjs_container_html5_api')") ||
                        await Video.EvaluateAsync<string>("v => v.currentSrc || v.src || ''") != previousSource;
                    if (changed) { ready = true; break; }
                }
                await Task.Delay(250, cancellationToken);
            }
            if (!ready)
                throw new TimeoutException("智慧树小节切换后未加载对应视频，已停止以避免操作上一小节。");
        }
        finally
        {
            if (previous is not null) await previous.DisposeAsync();
        }

        var timer = Stopwatch.StartNew();
        var endedSince = -1L;
        var replayCount = 0;
        var lastProgress = 0L;
        var lastTime = -1d;
        var source = await Video.EvaluateAsync<string>("v => v.currentSrc || v.src || ''");
        while (timer.ElapsedMilliseconds < timeoutMilliseconds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CloseStudyPopupsAsync(cancellationToken);
            if (await section.IsCompletedAsync(cancellationToken))
            {
                // 切入测试前停止当前媒体，避免后台继续播放/自动切节。
                if (await section.IsCurrentAsync() && await Video.CountAsync() > 0)
                    await Video.EvaluateAsync("v => v.pause()");
                return;
            }
            if (!await section.IsCurrentAsync())
                throw new InvalidOperationException("播放过程中当前小节发生变化，原小节尚未完成，请在目标小节重新启动任务。");
            if (await Video.EvaluateAsync<string>("v => v.currentSrc || v.src || ''") != source)
                throw new InvalidOperationException("播放过程中视频来源发生变化，已停止以避免误判完成。");

            var currentTime = await Video.EvaluateAsync<double>("v => v.currentTime");
            if (Math.Abs(currentTime - lastTime) > 0.1)
            {
                lastTime = currentTime;
                lastProgress = timer.ElapsedMilliseconds;
            }
            var ended = await Video.EvaluateAsync<bool>("v => v.ended");
            if (ended)
            {
                if (endedSince < 0) endedSince = timer.ElapsedMilliseconds;
                if (timer.ElapsedMilliseconds - endedSince >= reportWaitMilliseconds)
                {
                    if (replayCount++ >= 1)
                        throw new TimeoutException("视频已播完并重播，但目录仍未显示完成标记，请检查学习时长是否正常累计。");
                    await Video.EvaluateAsync("v => { v.currentTime = 0; }");
                    endedSince = -1;
                    lastProgress = timer.ElapsedMilliseconds;
                }
            }
            else endedSince = -1;

            if (timer.ElapsedMilliseconds - lastProgress > 120_000)
                throw new TimeoutException("智慧树视频两分钟未推进，请检查网络、播放器提示或登录状态。");
            await Video.EvaluateAsync("""
                (v, muted) => {
                    v.muted = muted;
                    v.playbackRate = 1;
                    if (v.paused && !v.ended) v.play().catch(() => {});
                }
                """, muted);
            await Task.Delay(1_000, cancellationToken);
        }
        throw new TimeoutException("等待智慧树目录完成标记超时，已停止当前任务。");
    }
}
