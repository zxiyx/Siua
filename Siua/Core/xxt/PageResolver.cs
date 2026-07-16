using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core;

public sealed class PageResolver
{
    private const string MainFrameSelector = "div.course_main > iframe";
    private const string VideoContainerSelector = "p > div.videoContainer";
    private const string AttachmentContainerSelector = "p > div.ans-attach-ct:not(.videoContainer)";

    private readonly IPage _page;
    private readonly GlobalSettings _settings;
    private readonly List<Video> _videos = [];
    private readonly List<ChapterTest> _tests = [];
    private readonly List<Doc> _docs = [];
    private IFrame? _mainFrame;

    public PageResolver(IPage page, GlobalSettings settings)
    {
        _page = page;
        _settings = settings;
    }

    public IReadOnlyList<Video> Videos => _videos;
    public IReadOnlyList<Doc> Docs => _docs;
    public IReadOnlyList<ChapterTest> Tests => _tests;
    public bool HasVideo => _videos.Count > 0;
    public bool HasTest => _tests.Count > 0;
    public bool HasDoc => _docs.Count > 0;

    public async Task<bool> WaitLoadingAsync()
    {
        try
        {
            var frameLocator = _page.Locator(MainFrameSelector).First;
            await frameLocator.WaitForAsync();
            var frameElement = await frameLocator.ElementHandleAsync();
            _mainFrame = frameElement is null ? null : await frameElement.ContentFrameAsync();
            return _mainFrame is not null;
        }
        catch (PlaywrightException)
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

        if (_mainFrame is null)
        {
            return;
        }

        var videoContainers = _mainFrame.Locator(VideoContainerSelector);
        var videoCount = await videoContainers.CountAsync();
        for (var index = 0; index < videoCount; index++)
        {
            _videos.Add(new Video(videoContainers.Nth(index), _settings));
        }

        var attachmentContainers = _mainFrame.Locator(AttachmentContainerSelector);
        var attachmentCount = await attachmentContainers.CountAsync();
        for (var index = 0; index < attachmentCount; index++)
        {
            var container = attachmentContainers.Nth(index);
            var document = await new DocResolver(container).ResolveAsync();
            if (document is not null)
            {
                _docs.Add(document);
            }
            else
            {
                _tests.Add(new ChapterTest(container));
            }
        }
    }

    public async Task ConfirmTestSubmissionAsync()
    {
        var popup = _page.Locator("div.maskDiv > div.popDiv.wid440.Marking").First;
        await popup.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = _settings.PopupTimeout
        });

        var submitButton = popup.Locator("#popok").First;
        if (await submitButton.CountAsync() > 0)
        {
            await submitButton.ClickAsync();
        }
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
            // 章节提示并非每次都会出现。
        }
        catch (PlaywrightException)
        {
            // 页面跳转可能会使提示元素失效，不影响后续章节处理。
        }
    }
}