using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core.Zhs;

public sealed class Chapter
{
    private const string SectionSelector =
        "div.chapter-content > div.chapter-item > div.item-box, " +
        "div.chapter-content > div.chapter-item > div.chapter-content-second";

    private readonly ILocator _chapterLocator;
    private readonly List<Section> _sections = [];

    public Chapter(ILocator chapterLocator)
    {
        _chapterLocator = chapterLocator;
    }

    public IReadOnlyList<Section> Sections => _sections;
    public ChapterTest? ChapterTest { get; private set; }
    public bool HasTest => ChapterTest is not null;

    public async Task ResolveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _sections.Clear();
        ChapterTest = null;

        var content = _chapterLocator
            .Locator("div.el-collapse-item__wrap div.el-collapse-item__content")
            .First;
        var sectionLocators = await content.Locator(SectionSelector).AllAsync();
        foreach (var sectionLocator in sectionLocators)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _sections.Add(new Section(sectionLocator));
        }

        var testBox = content.Locator("div.test-box").First;
        if (await testBox.CountAsync() > 0)
            ChapterTest = new ChapterTest(testBox);
    }

    public async Task ExpandAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var header = _chapterLocator.Locator("div.el-collapse-item__header").First;
        if (!string.Equals(
                await header.GetAttributeAsync("aria-expanded"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            await header.ScrollIntoViewIfNeededAsync();
            await header.ClickAsync(new LocatorClickOptions { Timeout = 15_000 });
        }

        await _chapterLocator.Locator("div.el-collapse-item__wrap").First.WaitForAsync(
            new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 15_000
            });
    }
}

public sealed class Section
{
    private readonly ILocator _locator;

    public Section(ILocator locator)
    {
        _locator = locator;
    }

    public async Task<double> GetProgressPercentAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var progressPath = _locator
            .Locator(".progress-div .el-progress-circle__path")
            .First;
        if (await progressPath.CountAsync() == 0)
            return 0;

        return await progressPath.EvaluateAsync<double>(
            """
            path => {
                const dashArray = getComputedStyle(path).strokeDasharray;
                const values = dashArray.match(/[\d.]+/g)?.map(Number) ?? [];
                if (values.length < 2 || !Number.isFinite(values[1]) || values[1] <= 0) {
                    return 0;
                }

                return Math.min(100, Math.max(0, values[0] / values[1] * 100));
            }
            """);
    }

    public async Task<bool> IsCompletedAsync(CancellationToken cancellationToken = default) =>
        await GetProgressPercentAsync(cancellationToken) >= 99.9;

    public async Task ClickAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _locator.ScrollIntoViewIfNeededAsync();
        await _locator.ClickAsync(new LocatorClickOptions { Timeout = 15_000 });
    }
}
