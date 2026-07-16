using System;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core;

public sealed class DocResolver
{

    private const string IncompleteIconSelector = "div.ans-job-icon[aria-label='任务点未完成']";
    private readonly ILocator _container;

    public DocResolver(ILocator container)
    {
        _container = container;
    }

    public async Task<Doc?> ResolveAsync()
    {
        try
        {
            var outerFrame = await GetContentFrameAsync(_container.Locator("iframe").First);
            if (outerFrame is null || await outerFrame.Locator("#docContainer").CountAsync() == 0)
            {
                return null;
            }

            var documentFrame = await GetContentFrameAsync(outerFrame.Locator("#panView").Last);
            if (documentFrame is null)
            {
                return null;
            }
            var isCompleted = await _container.Locator(IncompleteIconSelector).CountAsync() == 0;
            return new Doc(documentFrame, isCompleted);
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    private static async Task<IFrame?> GetContentFrameAsync(ILocator locator)
    {
        var element = await locator.ElementHandleAsync();
        return element is null ? null : await element.ContentFrameAsync();
    }
}