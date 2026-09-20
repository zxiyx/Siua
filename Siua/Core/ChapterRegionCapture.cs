using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core;

/// <summary>临时展开题目区域和外层 iframe，防止长试卷被滚动容器截断；截图后恢复布局。</summary>
internal static class ChapterRegionCapture
{
    public static async Task<byte[]> CaptureAsync(IFrame frame, ILocator region,
        CancellationToken cancellationToken, string? revealQuestions = null)
    {
        var states = new List<IJSHandle>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            states.Add(await region.EvaluateHandleAsync("""
                (root, selector) => {
                    const nodes = new Set();
                    for (let node = root; node; node = node.parentElement) nodes.add(node);
                    if (selector) {
                        for (const question of root.querySelectorAll(selector)) {
                            for (let node = question; node && node !== root; node = node.parentElement)
                                nodes.add(node);
                        }
                    }
                    const saved = [...nodes].map(node => ({ node, style: node.getAttribute('style') }));
                    for (const node of nodes) {
                        node.style.setProperty('height', 'auto', 'important');
                        node.style.setProperty('max-height', 'none', 'important');
                        node.style.setProperty('overflow', 'visible', 'important');
                        if (selector && root.contains(node)) {
                            node.style.setProperty('display', 'block', 'important');
                            node.style.setProperty('visibility', 'visible', 'important');
                        }
                    }
                    return saved;
                }
                """, revealQuestions));

            for (var current = frame; current.ParentFrame is not null; current = current.ParentFrame)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var height = await current.EvaluateAsync<int>(
                    "Math.ceil(Math.max(document.documentElement.scrollHeight, document.body.scrollHeight))");
                var owner = await current.FrameElementAsync();
                try
                {
                    states.Add(await owner.EvaluateHandleAsync("""
                        (iframe, height) => {
                            const saved = [];
                            for (let node = iframe; node; node = node.parentElement) {
                                saved.push({ node, style: node.getAttribute('style') });
                                node.style.setProperty('height', node === iframe ? height + 'px' : 'auto', 'important');
                                node.style.setProperty('max-height', 'none', 'important');
                                node.style.setProperty('overflow', 'visible', 'important');
                            }
                            return saved;
                        }
                        """, height));
                }
                finally { await owner.DisposeAsync(); }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return await region.ScreenshotAsync(new LocatorScreenshotOptions
            {
                Animations = ScreenshotAnimations.Disabled,
                Type = ScreenshotType.Png,
                Timeout = 30_000
            });
        }
        finally
        {
            for (var index = states.Count - 1; index >= 0; index--)
            {
                try
                {
                    await states[index].EvaluateAsync("""
                        saved => saved.forEach(({node, style}) => {
                            if (style === null) node.removeAttribute('style');
                            else node.setAttribute('style', style);
                        })
                        """);
                }
                catch (PlaywrightException) when (frame.IsDetached || frame.Page.IsClosed)
                { /* 导航或页面关闭后原 DOM 已不存在。 */ }
                finally { await states[index].DisposeAsync(); }
            }
        }
    }
}
