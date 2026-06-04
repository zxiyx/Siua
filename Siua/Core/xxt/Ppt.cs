using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core;

public class Ppt
{
    private IFrame handle;
    public Ppt(IFrame data)
    {
        handle = data;
    }
    public async Task<bool> IsCompleted()
    {
        try
        {
            var icon = handle.Locator("div.ans-job-icon.ans-job-icon-clear[aria-label='任务点未完成']");
            return await icon.CountAsync() == 0;
        }
        catch
        {
            return true;
        }
    }
    public async Task ScrollToEnd()
    {
        await handle.Locator("li[id^='anchor']").Last.ScrollIntoViewIfNeededAsync();
        await handle.WaitForTimeoutAsync(2000);
    }
    
    
    
}