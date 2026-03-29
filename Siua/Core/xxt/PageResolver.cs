using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core;

public class PageResolver
{
    private IPage page;
    private IFrame mFrame;
    public bool HasVideo
    {
        get => Videos.Count >= 1;
    }
    public bool HasTest
    {
        get => Tests.Count >= 1;
    }
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
    public async Task WaitLoading()
    {
        try
        {
            await page.WaitForSelectorAsync("div.course_main > iframe");
            await GetMainFrame();

        }
        catch (Exception e)
        {
            Console.WriteLine("[Error]WaitLoading :{0}", e);
        }
    }

    private async Task GetMainFrame()
    {
        try
        {
            var felement = await page.QuerySelectorAsync("div.course_main > iframe");
            mFrame = await felement.ContentFrameAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine("[Error]GetMFrame :{0}", e);
        }
        
    }
    public async Task ResolvePage()
    {
        var fContainers = await mFrame.QuerySelectorAllAsync("p > div.videoContainer");
        if (fContainers.Count >= 1)
        {
            foreach (var container in fContainers)
            {
                videos.Add(new Video(container,_settings));
            }
        }
        fContainers = null;
        fContainers = await mFrame.QuerySelectorAllAsync("p > div.ans-attach-ct:not(.videoContainer)");
        if (fContainers.Count >= 1)
        {
            //Console.WriteLine($"获取test {fContainers.Count} 个");
            foreach (var container in fContainers)
            {
                tests.Add(new ChapterTest(container));
            }
        }
    }
    public async Task WaitForSubmitAgain()
    {
        var popup = await page.QuerySelectorAllAsync("div.maskDiv > div.popDiv.wid440.Marking");
        if (popup.Count > 0 && await popup[0].IsVisibleAsync()) 
        {
            var submit = await popup[0].QuerySelectorAsync("#popok");
            await submit.ClickAsync();
        }
        
    }
    public async Task NextPageAsync()
    {
        await page.WaitForSelectorAsync("#prevNextFocus > #prevNextFocusNext");
        var buttonToNext = await page.QuerySelectorAsync("#prevNextFocusNext");
        await buttonToNext.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.Load);
        await WaitForCloseNotice();
    }
    private async Task WaitForCloseNotice()
    {
        try
        {
            var popup = await page.QuerySelectorAllAsync("div.popHead > #popHeadFocus");
            //var popup = await page.QuerySelectorAllAsync("div.maskDiv.jobFinishTip.maskFadeOut > div.popDiv.wid440.popMove > #jobFinishTipFocus");
            if (popup != null)
            {
                var nextbut = await page.QuerySelectorAllAsync("div.popBottom > a.jb_btn.nextChapter");
                if (nextbut.Count>0 &&  await nextbut[0].IsVisibleAsync())
                {
                    await nextbut[0].ClickAsync();
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("未找到");
        }
        
    }

    
}

