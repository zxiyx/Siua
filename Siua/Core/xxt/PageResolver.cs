using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Siua.Common;
using Siua.Interfaces;

namespace Siua.Core.Xxt;

/// <summary>解析学习通课程页面中的各类任务点。</summary>
public sealed class XxtPageResolver
{
    private const string MainFrameSelector = "div.course_main > iframe";
    private const string VideoContainerSelector = "div.videoContainer";
    private const string AttachmentContainerSelector = "div.ans-attach-ct:not(.videoContainer)";

    private readonly IPage _page;
    private readonly GlobalSettings _settings;
    private readonly ILogService _logService;
    private readonly List<XxtVideo> _videos = [];
    private readonly List<XxtChapterTest> _tests = [];
    private readonly List<XxtDocument> _docs = [];
    private IFrame? _mainFrame;

    public XxtPageResolver(
        IPage page,
        GlobalSettings settings,
        ILogService logService)
    {
        _page = page;
        _settings = settings;
        _logService = logService;
    }

    public IReadOnlyList<XxtVideo> Videos => _videos;
    public IReadOnlyList<XxtDocument> Docs => _docs;
    public IReadOnlyList<XxtChapterTest> Tests => _tests;
    public bool HasVideo => _videos.Count > 0;
    public bool HasTest => _tests.Count > 0;
    public bool HasDoc => _docs.Count > 0;
    public bool HasResolutionErrors { get; private set; }

    public async Task<bool> WaitLoadingAsync()
    {
        try
        {
            var frameLocator = _page.Locator(MainFrameSelector).First;
            await frameLocator.WaitForAsync();
            var frameElement = await frameLocator.ElementHandleAsync();
            _mainFrame = frameElement is null ? null : await frameElement.ContentFrameAsync();
            if (_mainFrame is null)
            {
                return false;
            }

            await WaitForFrameContentAsync(_mainFrame, frameLocator);
            return true;
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            _mainFrame = null;
            return false;
        }
    }

    public async Task ResolvePageAsync()
    {
        _videos.Clear();
        _docs.Clear();
        _tests.Clear();
        HasResolutionErrors = false;

        if (_mainFrame is null)
        {
            HasResolutionErrors = true;
            return;
        }

        var videoContainers = _mainFrame.Locator(VideoContainerSelector);
        var videoCount = await videoContainers.CountAsync();
        for (var index = 0; index < videoCount; index++)
        {
            _videos.Add(new XxtVideo(videoContainers.Nth(index), _settings));
        }

        var attachmentContainers = _mainFrame.Locator(AttachmentContainerSelector);
        var attachmentCount = await attachmentContainers.CountAsync();
        for (var index = 0; index < attachmentCount; index++)
        {
            var container = attachmentContainers.Nth(index);
            try
            {
                var iframe = container.Locator("iframe").First;
                await iframe.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached });
                var module = await GetAttachmentModuleAsync(iframe);
                if (module is "insertbbs" or "downloadfile")
                {
                    _logService.AddLog(LogLevel.Info, "Xxt", "检测到讨论或下载附件，已略过");
                    continue;
                }

                var jobId = await iframe.GetAttributeAsync("jobid");
                if (module == "work" || jobId?.StartsWith("work-", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _tests.Add(new XxtChapterTest(container));
                    continue;
                }

                var isDocumentModule = module is "ppt" or "pdf" or "doc" or "document";
                if (module.Length > 0 && !isDocumentModule)
                {
                    _logService.AddLog(LogLevel.Info, "Xxt", "检测到未支持的附件模块，已略过");
                    continue;
                }

                var document = await new XxtDocumentResolver(container).ResolveAsync(isDocumentModule);
                if (document is not null)
                {
                    _docs.Add(document);
                }
                else if (await ContainsChapterTestAsync(iframe))
                {
                    _tests.Add(new XxtChapterTest(container));
                }
                else
                {
                    _logService.AddLog(LogLevel.Info, "Xxt", "附件中未识别到文档或章节测试，已略过");
                }
            }
            catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
            {
                HasResolutionErrors = true;
                _logService.AddLog(
                    LogLevel.Error,
                    "Xxt",
                    $"任务点解析失败，当前页面仍有未确认任务：{exception}");
            }
        }
    }

    private static async Task<string> GetAttachmentModuleAsync(ILocator iframe)
    {
        var module = await iframe.GetAttributeAsync("module");
        if (!string.IsNullOrWhiteSpace(module))
        {
            return module.Trim().ToLowerInvariant();
        }

        var source = await iframe.GetAttributeAsync("src") ?? string.Empty;
        var path = source.Split('?', '#')[0];
        const string modulePrefix = "/ananas/modules/";
        var start = path.IndexOf(modulePrefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return string.Empty;
        }

        var modulePath = path[(start + modulePrefix.Length)..];
        var end = modulePath.IndexOf('/');
        return (end < 0 ? modulePath : modulePath[..end]).ToLowerInvariant();
    }

    private static async Task<bool> ContainsChapterTestAsync(ILocator iframe, int depth = 0)
    {
        var element = await iframe.ElementHandleAsync();
        var frame = element is null ? null : await element.ContentFrameAsync();
        if (frame is null)
        {
            throw new PlaywrightException("无法进入附件 iframe 以识别章节测试。");
        }

        await WaitForFrameContentAsync(frame, iframe);
        if (await frame.Locator("div.CeYan, div.TiMu.newTiMu, div.testTit_status_complete").CountAsync() > 0)
        {
            return true;
        }

        // 兼容没有模块 URL 的旧页面，但不再把任意附件当作章节测试。
        if (depth < 2)
        {
            var innerFrames = frame.Locator("iframe");
            var count = await innerFrames.CountAsync();
            for (var index = 0; index < count; index++)
            {
                if (await ContainsChapterTestAsync(innerFrames.Nth(index), depth + 1))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static async Task WaitForFrameContentAsync(IFrame frame, ILocator iframe)
    {
        // iframe 元素已出现并不代表它已离开初始的 about:blank 文档。
        var source = await iframe.GetAttributeAsync("src");
        if (frame.Url == "about:blank" &&
            !string.IsNullOrWhiteSpace(source) &&
            !source.StartsWith("about:", StringComparison.OrdinalIgnoreCase) &&
            !source.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            await frame.WaitForURLAsync(url => url != "about:blank", new FrameWaitForURLOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });
        }

        await frame.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    }

    public async Task ConfirmTestSubmissionAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var popup = _page.Locator("div.maskDiv > div.popDiv.wid440.Marking").First;
        await popup.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = _settings.PopupTimeout
        });

        var submitButton = popup.Locator("#popok").First;
        await submitButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = _settings.PopupTimeout
        });
        cancellationToken.ThrowIfCancellationRequested();
        await submitButton.ClickAsync();
    }

    public async Task<bool> NextPageAsync(CancellationToken cancellationToken = default, int timeout = 30_000)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (await IsAtCourseEndAsync())
        {
            _logService.AddLog(LogLevel.Info, "Xxt", "已到课程目录最后一节，结束本次学习");
            return false;
        }

        var nextButton = _page.Locator("#prevNextFocus > #prevNextFocusNext:visible").First;
        try
        {
            await nextButton.WaitForAsync(new LocatorWaitForOptions { Timeout = timeout }).WaitAsync(cancellationToken);
        }
        catch (TimeoutException exception)
        {
            // 目录可能在等待期间加载完成，重新核实；隐藏按钮本身不足以证明已到末尾。
            if (await IsAtCourseEndAsync())
            {
                _logService.AddLog(LogLevel.Info, "Xxt", "已到课程目录最后一节，结束本次学习");
                return false;
            }
            throw new TimeoutException("下一节按钮不可用，尚未确认到达课程末尾；请检查页面提示或章节目录。", exception);
        }

        var previousFrame = _mainFrame;
        var previousUrl = previousFrame?.Url;
        cancellationToken.ThrowIfCancellationRequested();
        _logService.AddLog(LogLevel.Info, "Xxt", "准备进入下一节...");
        await nextButton.ClickAsync(new LocatorClickOptions { Timeout = timeout });
        await CloseChapterNoticeAsync();

        // 学习通通过 AJAX 替换 mainid 或只修改内容 iframe 的 src，外层 DOMContentLoaded 不会再次触发。
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var iframe = _page.Locator(MainFrameSelector).First;
            if (await iframe.CountAsync() > 0)
            {
                try
                {
                    var element = await iframe.ElementHandleAsync(new LocatorElementHandleOptions { Timeout = 500 });
                    var frame = element is null ? null : await element.ContentFrameAsync();
                    if (frame is not null && !frame.IsDetached &&
                        (frame != previousFrame || frame.Url != previousUrl))
                    {
                        var source = await iframe.GetAttributeAsync("src");
                        var sourceDocument = await iframe.GetAttributeAsync("srcdoc");
                        var isInitialBlank = frame.Url == "about:blank" &&
                            (sourceDocument is not null || (!string.IsNullOrWhiteSpace(source) &&
                             !source.StartsWith("about:", StringComparison.OrdinalIgnoreCase) &&
                             !source.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)));
                        if (!isInitialBlank && await frame.EvaluateAsync<bool>(
                                "() => document.readyState !== 'loading' && document.body !== null && document.body.childNodes.length > 0"))
                        {
                            _mainFrame = frame;
                            return true;
                        }
                    }
                }
                catch (Exception exception) when (exception is PlaywrightException or TimeoutException && !_page.IsClosed)
                {
                    // AJAX 替换 iframe 时会短暂失去执行上下文，下一轮读取新控件。
                }
            }
            await Task.Delay(100, cancellationToken);
        }
        throw new TimeoutException("已点击下一节，但章节内容未完成切换；已停止以避免重复处理旧章节。");
    }

    private Task<bool> IsAtCourseEndAsync() => _page.EvaluateAsync<bool>("""
        () => {
            // 对齐页面 hideChangeBtn：目录最后一个章节 + 最后一张内容卡片。
            // 搜索过滤后的目录和未同步的章节标记不能作为课程末尾依据。
            if (document.querySelector('#searchChapterListByName')?.value?.trim()) return false;
            const items = [...document.querySelectorAll('#coursetree li')];
            const last = items.at(-1)?.querySelector(':scope > .posCatalog_select');
            const chapter = document.querySelector('#chapterIdid')?.value;
            if (!last?.classList.contains('posCatalog_active') || !chapter || last.id !== 'cur' + chapter) return false;
            const lists = [...document.querySelectorAll('.prev_list')];
            if (lists.length && !lists.some(list => list.querySelector(':scope > ul > li:last-child')?.classList.contains('active')))
                return false;
            const buttons = [...document.querySelectorAll('#prevNextFocus > #prevNextFocusNext')];
            return buttons.length > 0 && buttons.every(button =>
                button.getClientRects().length === 0 || getComputedStyle(button).visibility === 'hidden');
        }
        """);

    private async Task CloseChapterNoticeAsync()
    {
        try
        {
            var popup = _page.Locator("div.popHead > #popHeadFocus");
            await popup.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = _settings.PopupTimeout
            });

            var nextButton = _page.Locator("div.popBottom > a.jb_btn.nextChapter").First;
            if (await nextButton.CountAsync() > 0 && await nextButton.IsVisibleAsync())
            {
                await nextButton.ClickAsync();
            }
        }
        catch (TimeoutException)
        {
        }
        catch (PlaywrightException)
        {
        }
    }
}
