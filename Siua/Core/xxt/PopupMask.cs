using System;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core;

public class PopupMask 
{
    private IPage page;
    private IElementHandle? popup;

    public PopupMask(IPage opage)
    {
        page = opage;
    }
    public async Task WaitForClosePopupMask()
    {
        try
        {
            popup = await page.WaitForSelectorAsync("div.maskDiv.jobFinishTip",new()
            {
                State = WaitForSelectorState.Hidden
            });
            if (popup != null)
            {
                var closebutton = await popup.QuerySelectorAsync("a.popClose.fr");
                if (closebutton != null) await closebutton.ClickAsync();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
        
            
    }
    public async Task WaitForSubmitAgain()
    {
        popup = await page.WaitForSelectorAsync("div.maskDiv");
        if (popup != null)
        {
            var pop = await popup.QuerySelectorAsync("div.popBottom");
            var submit = await pop.QuerySelectorAsync("a.jb_btn");
            await submit.ClickAsync();
        }
        
    }
}