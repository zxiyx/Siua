using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core.Zhs;

public sealed class ZhsChapter
{
    private const string SectionSelector =
        "div.chapter-content > div.chapter-item > div.item-box, " +
        "div.chapter-content > div.chapter-item > div.chapter-content-second";

    private readonly ILocator _chapterLocator;
    private readonly List<ZhsSection> _sections = [];

    public ZhsChapter(ILocator chapterLocator)
    {
        _chapterLocator = chapterLocator;
    }

    public IReadOnlyList<ZhsSection> Sections => _sections;
    public ZhsChapterTest? ChapterTest { get; private set; }
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
            _sections.Add(new ZhsSection(sectionLocator));
        }

        var testBox = content.Locator("div.test-box").First;
        if (await testBox.CountAsync() > 0)
            ChapterTest = new ZhsChapterTest(testBox);
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

public sealed class ZhsSection
{
    private static readonly Regex StrokeDashArrayPattern = new(
        @"stroke-dasharray\s*:\s*(?<value>[\d.]+)(?:px)?\s*,\s*(?<total>[\d.]+)(?:px)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ILocator _locator;

    public ZhsSection(ILocator locator)
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

        var style = await progressPath.GetAttributeAsync("style");
        var match = string.IsNullOrWhiteSpace(style)
            ? Match.Empty
            : StrokeDashArrayPattern.Match(style);
        if (!match.Success ||
            !double.TryParse(
                match.Groups["value"].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value) ||
            !double.TryParse(
                match.Groups["total"].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var total) ||
            total <= 0)
        {
            return 0;
        }

        return Math.Clamp(value / total * 100, 0, 100);
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
