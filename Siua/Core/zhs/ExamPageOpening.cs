using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core.Zhs;

/// <summary>只跟踪本次课程入口打开的窗口；中转窗口关闭不等于测试结束。</summary>
internal static class ZhsExamPageOpening
{
    private const string UnavailableMessage =
        "章节测试页未在限定时间内稳定显示题目或成绩（可能仍在跳转、要求登录或窗口已关闭）。请检查浏览器后重试；未判定测试完成。";

    internal static bool IsTargetClosed(PlaywrightException exception) =>
        exception.Message.Contains("Target page, context or browser has been closed", StringComparison.OrdinalIgnoreCase);

    internal static bool IsNavigationInterrupted(PlaywrightException exception) =>
        exception.Message.Contains("Execution context was destroyed", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("Cannot find context with specified id", StringComparison.OrdinalIgnoreCase);

    internal static async Task<IPage> OpenAsync(IPage coursePage, Func<Task> click,
        Func<IPage, Task<bool>> isReady, CancellationToken token, int timeoutMilliseconds = 30_000)
    {
        var gate = new object();
        var pages = new List<IPage>();
        var generations = new Dictionary<IPage, int>();
        var contextClosed = false;
        var disposed = false;

        void Navigated(object? sender, IFrame frame)
        {
            lock (gate)
                if (sender is IPage page && frame == page.MainFrame && generations.ContainsKey(page))
                    generations[page]++;
        }
        void Track(object? sender, IPage page)
        {
            lock (gate)
            {
                if (disposed || generations.ContainsKey(page)) return;
                pages.Add(page);
                generations.Add(page, 0);
                page.Popup += Track;
                page.FrameNavigated += Navigated;
            }
        }
        void ContextClosed(object? sender, IBrowserContext context)
        {
            lock (gate) contextClosed = true;
        }

        Track(null, coursePage);
        coursePage.Context.Close += ContextClosed;
        try
        {
            token.ThrowIfCancellationRequested();
            // 只点击一次；读取期间的跳转/中转页关闭仅重试识别，不能重开或重提试卷。
            await click().WaitAsync(token);
            var timer = Stopwatch.StartNew();
            IPage? stablePage = null;
            var stableGeneration = -1;
            var stableSamples = 0;
            while (timer.ElapsedMilliseconds < timeoutMilliseconds)
            {
                token.ThrowIfCancellationRequested();
                IPage[] candidates;
                lock (gate)
                {
                    if (contextClosed || coursePage.Context.Browser is { IsConnected: false })
                        throw new ZhsExamPageUnavailableException("浏览器或浏览器上下文已关闭，无法打开章节测试。");
                    candidates = pages.Where(p => !p.IsClosed).Reverse().ToArray();
                }
                var found = false;
                foreach (var candidate in candidates)
                {
                    int generation;
                    lock (gate) generation = generations[candidate];
                    try
                    {
                        var remaining = timeoutMilliseconds - timer.ElapsedMilliseconds;
                        if (remaining <= 0) throw new ZhsExamPageUnavailableException(UnavailableMessage);
                        if (!await isReady(candidate).WaitAsync(TimeSpan.FromMilliseconds(remaining), token)) continue;
                        lock (gate)
                        {
                            if (candidate.IsClosed || generation != generations[candidate]) continue;
                            found = true;
                            stableSamples = candidate == stablePage && generation == stableGeneration
                                ? stableSamples + 1 : 1;
                            stablePage = candidate;
                            stableGeneration = generation;
                            if (stableSamples >= 3) return candidate;
                        }
                        break;
                    }
                    catch (PlaywrightException exception) when (
                        candidate.IsClosed || IsTargetClosed(exception) || IsNavigationInterrupted(exception))
                    {
                        // 页面可能关闭后再由入口或中转页打开替代窗口，下一轮重新选择。
                    }
                    catch (TimeoutException)
                    {
                        throw new ZhsExamPageUnavailableException(UnavailableMessage);
                    }
                }
                if (!found) { stablePage = null; stableSamples = 0; }
                await Task.Delay(250, token);
            }
            throw new ZhsExamPageUnavailableException(UnavailableMessage);
        }
        finally
        {
            coursePage.Context.Close -= ContextClosed;
            lock (gate)
            {
                disposed = true;
                foreach (var page in pages)
                {
                    page.Popup -= Track;
                    page.FrameNavigated -= Navigated;
                }
            }
        }
    }
}

internal sealed class ZhsExamPageUnavailableException(string message) : Exception(message);
