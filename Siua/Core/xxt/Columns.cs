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
       await page.WaitForSelectorAsync(".showcontent.posChapter");
    }
    private async Task<IReadOnlyList<IElementHandle>> GetColumnItems()
    {
        return await page.QuerySelectorAllAsync("ul[style='padding-bottom:30px'] > li");
    }

    public async Task GetChapters()
    {
        var catalog = await GetColumnItems();
        foreach (var c in catalog)
        {
            chapters.Add(new Chapter(await c.QuerySelectorAsync(".posCatalog_select.firstLayer"), c));
        }
    }
}