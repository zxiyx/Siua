using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core.Xxt;

/// <summary>浏览文档到实际底部，并核对页面的任务点状态。</summary>
public sealed class XxtDocument
{
    private readonly IFrame _frame;
    private readonly ILocator? _container;

    public XxtDocument(IFrame frame, bool isCompleted, ILocator? container = null)
    {
        _frame = frame;
        _container = container;
        IsCompleted = isCompleted;
    }

    public bool IsCompleted { get; private set; }

    public async Task<bool> IsCompletedAsync()
    {
        if (_container is not null)
            IsCompleted = await ReadTaskStateAsync() == "completed";
        return IsCompleted;
    }

    private Task<string> ReadTaskStateAsync() => _container is null
        ? Task.FromResult("none")
        : _container.EvaluateAsync<string>("""
            element => {
                const icon = element.querySelector('.ans-job-icon');
                const label = icon?.getAttribute('aria-label') || '';
                if (label.includes('未完成')) return 'pending';
                if (element.classList.contains('ans-job-finished') || label.includes('已完成')) return 'completed';
                return icon || element.querySelector('iframe[jobid]')?.getAttribute('jobid') ? 'pending' : 'none';
            }
            """);

    public async Task ScrollToEndAsync(CancellationToken cancellationToken = default)
    {
        // 兼容 .fileBox 图片文档及旧版 ul/li 阅读器。只滚动最后一页到可见不能保证到底。
        await _frame.Locator(".fileBox > ul > li, ul > li").Last.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 })
            .WaitAsync(cancellationToken);
        var timer = Stopwatch.StartNew();
        double previousHeight = -1;
        long stableSince = 0;
        var reachedEnd = false;
        while (timer.ElapsedMilliseconds < 15_000)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await _frame.EvaluateAsync<double[]>("""
                () => {
                    const root = document.scrollingElement || document.documentElement;
                    const scope = document.querySelector('.fileBox') || document.querySelector('ul');
                    const images = [...scope.querySelectorAll('img')];
                    if (images.some(img => img.complete && img.naturalHeight === 0))
                        throw new Error('文档图片加载失败');
                    const ready = images.every(img => img.complete && img.naturalHeight > 0);
                    window.scrollTo({ top: root.scrollHeight, behavior: 'instant' });
                    return [root.scrollHeight, root.scrollHeight - root.scrollTop - root.clientHeight, ready ? 1 : 0];
                }
                """).WaitAsync(cancellationToken);
            if (state[0] != previousHeight || state[2] == 0 || state[1] > 40)
                stableSince = timer.ElapsedMilliseconds;
            else if (timer.ElapsedMilliseconds - stableSince >= 1000)
            {
                reachedEnd = true;
                break;
            }
            previousHeight = state[0];
            await Task.Delay(200, cancellationToken);
        }
        if (!reachedEnd)
            throw new TimeoutException("文档图片或页面高度未稳定，尚未确认滚动到底部。");

        // 等平台自己的滚动回调更新任务点，不伪造完成通知。
        timer.Restart();
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await ReadTaskStateAsync();
            IsCompleted = state == "completed";
            if (IsCompleted || state == "none") return;
            await Task.Delay(250, cancellationToken);
        } while (timer.ElapsedMilliseconds < 10_000);
        throw new TimeoutException("文档已滚动到底部，但任务点尚未确认完成。");
    }
}
