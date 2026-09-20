using System;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core.Xxt;

/// <summary>解析学习通页面中的文档任务。</summary>
public sealed class XxtDocumentResolver
{

    private const string IncompleteIconSelector = "div.ans-job-icon[aria-label='任务点未完成']";
    private readonly ILocator _container;

    public XxtDocumentResolver(ILocator container)
    {
        _container = container;
    }

    public async Task<XxtDocument?> ResolveAsync(bool requireDocument = false)
    {
        var outerFrame = await GetRequiredContentFrameAsync(_container.Locator("iframe").First);
        var documentContainer = outerFrame.Locator("#docContainer");
        if (requireDocument)
        {
            await documentContainer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached });
        }
        else if (await documentContainer.CountAsync() == 0)
        {
            return null;
        }

        var documentFrame = await GetRequiredContentFrameAsync(outerFrame.Locator("#panView").Last);
        var isCompleted = await _container.Locator(IncompleteIconSelector).CountAsync() == 0;
        return new XxtDocument(documentFrame, isCompleted);
    }

    private static async Task<IFrame> GetRequiredContentFrameAsync(ILocator locator)
    {
        await locator.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached });
        var element = await locator.ElementHandleAsync()
            ?? throw new PlaywrightException("无法获取附件 iframe 元素。");
        var frame = await element.ContentFrameAsync()
            ?? throw new PlaywrightException("无法进入附件 iframe。");
        await XxtPageResolver.WaitForFrameContentAsync(frame, locator);
        return frame;
    }
}
