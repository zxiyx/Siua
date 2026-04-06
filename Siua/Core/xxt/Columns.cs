using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core;

public class Columns
{
    private IPage page;
    private List<Chapter> chapters = new();
    public List<Chapter> Chapters => chapters;
    
    public Columns(IPage _page)
    {
        page = _page;
    }
    public async Task WaitColumnLoading()
    {
        await page.Locator(".showcontent.posChapter").First.WaitForAsync();
    }

    private ILocator GetColumnItems()
    {
        return page.Locator("ul[style='padding-bottom:30px'] > li");
    }

    public async Task GetChapters()
    {
        var catalog = GetColumnItems();
        var count = await catalog.CountAsync();
        for (int i = 0; i < count; i++)
        {
            var c = catalog.Nth(i);
            chapters.Add(new Chapter(c.Locator(".posCatalog_select.firstLayer"), c));
        }
    }
}