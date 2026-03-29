using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core;

public class Chapter
{
    private IElementHandle? data;
    private IElementHandle catalog;
    private List<SubChapter> subChapters = new();
    public List<SubChapter> SubChapters => subChapters;
    private string title;
    
    public string Title 
    {
        get => title;
        set => title = value;
    }
    public Chapter(IElementHandle? _data, IElementHandle _catalog)
    {
        data = _data;
        catalog = _catalog;
    }
    
    public async Task<string> GetChapterTitle()
    {
        title = await data.InnerTextAsync();
        return title;
    }

    public async Task GetSubChapters()
    {
        var scs = await catalog.QuerySelectorAllAsync(".posCatalog_level ul li");
        foreach (var sc in scs)
        {
            subChapters.Add(new SubChapter(await sc.QuerySelectorAsync(".posCatalog_name")));
        }
    }
    public async Task Click()
    {
        await data.ClickAsync();
    }
}
public class SubChapter
{
    private IElementHandle? data;
    private string title;
    
    public string Title 
    {
        get => title;
        set => title = value;
    }
    public SubChapter(IElementHandle? _data)
    {
        data = _data;
    }
    public async Task<string> GetSubChapterTitle()
    {
        title = await data.InnerTextAsync();
        return title;
    }

    public async Task Click()
    {
        await data.ClickAsync();
    }
    
}