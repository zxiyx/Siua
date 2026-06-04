using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core;

public class PptResolver
{
    public static async Task<Ppt?> ResloveToPpt(ILocator handle)
    {
        var fcontainer = handle.Locator("iframe").First;
        var frame = await (await fcontainer.ElementHandleAsync())!.ContentFrameAsync();
        var iffcontainer = frame!.Locator("iframe").First;
        await iffcontainer.WaitForAsync();
        var fframe = await (await iffcontainer.ElementHandleAsync())!.ContentFrameAsync();
        /*
        if (await fframe!.Locator("div.fileBox").CountAsync() > 0)
        {
            return fframe!.Locator("div.fileBox").First;
        }
        */
        if (fframe != null)
        {
            return new Ppt(fframe!);
        } 
        return null;
    }
}