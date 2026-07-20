using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core.Zhs;

public sealed class ZhsPageResolver
{
    private const string MainContainerSelector = "div.main-container";
    private const string ChapterSelector =
        "div.course-catalog div.chapter-wrapper " +
        "div.el-collapse.el-collapse-icon-position-right > div.el-collapse-item";
    private const string VideoSelector =
        "div.video-main div.video-play #container video.vjs-tech";

    private readonly IPage _page;
    private ILocator? _mainContainer;

    public ZhsPageResolver(IPage page)
    {
        _page = page;
    }

    public async Task<bool> WaitLoadingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var mainContainer = _page.Locator(MainContainerSelector).First;
            await mainContainer.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 30_000
            });
            _mainContainer = mainContainer;
            return true;
        }
        catch (PlaywrightException)
        {
            _mainContainer = null;
            return false;
        }
    }

    public async Task<IReadOnlyList<ZhsChapter>> ResolveChaptersAsync(
        CancellationToken cancellationToken = default)
    {
        if (_mainContainer is null && !await WaitLoadingAsync(cancellationToken))
            return [];

        var chapterLocators = GetMainContainer().Locator(ChapterSelector);
        await chapterLocators.First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 30_000
        });

        var chapters = new List<ZhsChapter>();
        foreach (var chapterLocator in await chapterLocators.AllAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chapter = new ZhsChapter(chapterLocator);
            await chapter.ResolveAsync(cancellationToken);
            chapters.Add(chapter);
        }

        return chapters;
    }

    public async Task<bool> WaitForContentLoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await GetMainContainer().Locator("div.video-main").First.WaitForAsync(
                new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 15_000
                });
            await Task.Delay(1_200, cancellationToken);
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    public async Task<ZhsVideo?> ResolveCurrentVideoAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var video = GetMainContainer().Locator(VideoSelector).First;
        try
        {
            await video.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 8_000
            });
        }
        catch (PlaywrightException)
        {
            return null;
        }

        return new ZhsVideo(video);
    }

    private ILocator GetMainContainer() =>
        _mainContainer ?? throw new PlaywrightException("智慧树课程主区域尚未加载。");
}
