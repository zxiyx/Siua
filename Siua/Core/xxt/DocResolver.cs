using System;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core;

public class DocResolver
{

    private ILocator handle;
    private IFrame dframe;

    public IFrame Dframe { get => dframe; }
    

    public DocResolver(ILocator data)
    {
        handle = data;

    }
    public async Task<bool> IsCompleted()
    {
        try
        {
            var icon = handle.Locator("div.ans-job-icon[aria-label='任务点未完成']");
            return await icon.CountAsync() == 0;
        }
        catch
        {
            return true;
        }
    }
    public async Task<bool> ResloveToDoc()
    {
        try
        {
            var frameLocator = handle.Locator("iframe").First;
            var frame = await (await frameLocator.ElementHandleAsync())!.ContentFrameAsync();
            if (await frame.Locator("#docContainer").CountAsync() > 0)
            {
                Console.WriteLine(await frame.Locator("#panView").CountAsync());
                var docContainer = frame.Locator("#panView").Last;
                var iframe = await (await docContainer.ElementHandleAsync())!.ContentFrameAsync();
                Console.WriteLine(await iframe.ContentAsync());
                /*
                if (await fframe!.Locator("div.fileBox").CountAsync() > 0)
                {
                    return fframe!.Locator("div.fileBox").First;
                }
                */
                if (iframe != null)
                {
                    dframe = iframe;
                }
                else return false;
                return true;
            }
            return false;
        }
        catch(PlaywrightException exception)
        {
            return false;
        }
        
    }
}