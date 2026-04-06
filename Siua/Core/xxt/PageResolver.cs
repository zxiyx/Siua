using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core;

public class PageResolver
{
    private IPage page;
    private IFrame? mFrame;
    public bool HasVideo=> Videos.Count > 0;
    public bool HasTest => Tests.Count > 0;
    private List<Video> videos = new();
    private List<ChapterTest> tests = new();
    public List<Video> Videos => videos;
    public List<ChapterTest> Tests => tests;
    private readonly GlobalSettings _settings;
    public PageResolver(IPage opage ,GlobalSettings settings)
    {
        page = opage;
        _settings = settings;
    }
    public async Task<bool> WaitLoading()
    {
        try
        {
            await GetMainFrame();
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine("[Error]WaitLoading :{0}", e);
            return false;
        }
    }

    private async Task GetMainFrame()
    {
        var felement = page.Locator("div.course_main > iframe").First;
        await felement.WaitForAsync();

        var frameHandle = await felement.ElementHandleAsync();
        if (frameHandle == null)
        {
            Console.WriteLine("[Error]找不到course_main 中 mframe");
            mFrame = null;
            return;
        }
        mFrame = await frameHandle.ContentFrameAsync();
        if (mFrame == null) Console.WriteLine("[Error]mframe缺失");
    }
    public async Task ResolvePage()
    {
        if (mFrame == null)
        {
            Console.WriteLine("[Error]mframe未加载");
            return;
        }
        var videoContainers = mFrame.Locator("p > div.videoContainer");
        var videoCount = await videoContainers.CountAsync();
        for (int i = 0; i < videoCount; i++)
        {
            videos.Add(new Video(videoContainers.Nth(i), _settings));
        }

        var testContainers = mFrame.Locator("p > div.ans-attach-ct:not(.videoContainer)");
        var testCount = await testContainers.CountAsync();
        for (int i = 0; i < testCount; i++)
        {
            tests.Add(new ChapterTest(testContainers.Nth(i)));
        }
    }
    public async Task WaitForSubmitAgain()
    {
        var popup = page.Locator("div.maskDiv > div.popDiv.wid440.Marking").First;
        await popup.WaitForAsync(new ()
        {
            State = WaitForSelectorState.Visible,
            Timeout = _settings.PopupTimeout
        });
        if (await popup.CountAsync() > 0)
        {
            var submit = popup.First.Locator("#popok");
            if (await submit.CountAsync() > 0)
            {
                await submit.First.ClickAsync();
            }
        }
    }
    public async Task NextPageAsync()
    {
        var next = page.Locator("#prevNextFocus > #prevNextFocusNext").First;
        await next.WaitForAsync();
        await next.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await WaitForCloseNotice();
    }
    private async Task WaitForCloseNotice()
    {
        try
        {
            var popup = page.Locator("div.popHead > #popHeadFocus");
            await popup.WaitForAsync(new()
            {      
                State = WaitForSelectorState.Visible,
                Timeout = _settings.PopupTimeout
            });
            if (await popup.CountAsync() > 0)
            {
                var nextbut = page.Locator("div.popBottom > a.jb_btn.nextChapter");
                if (await nextbut.CountAsync() > 0 && await nextbut.First.IsVisibleAsync())
                {
                    await nextbut.First.ClickAsync();
                }
            }
        }
        catch
        {
            Console.WriteLine("未找到Pop");
        }
    }
}
