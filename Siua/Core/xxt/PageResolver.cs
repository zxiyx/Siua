using System;
using System.Collections.Generic;
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
                    $"任务点解析失败，当前页面仍有未确认任务：{exception.Message}");
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

    public async Task NextPageAsync()
    {
        var nextButton = _page.Locator("#prevNextFocus > #prevNextFocusNext").First;
        await nextButton.WaitForAsync();
        await nextButton.ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await CloseChapterNoticeAsync();
    }

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
