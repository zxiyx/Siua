using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core.Zhs;

/// <summary>
/// 智慧树章节测试入口。当前只负责识别和导航，答题逻辑后续在此扩展。
/// </summary>
public sealed class ChapterTest
{
    private readonly ILocator _locator;

    public ChapterTest(ILocator locator)
    {
        _locator = locator;
    }

    public async Task<bool> IsAvailableAsync() =>
        await _locator.CountAsync() > 0 && await _locator.IsVisibleAsync();

    public async Task ClickAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _locator.ScrollIntoViewIfNeededAsync();
        await _locator.ClickAsync(new LocatorClickOptions { Timeout = 15_000 });
    }
}
