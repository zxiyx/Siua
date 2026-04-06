using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core;

public class Chapter
{
    private ILocator? data;
    private ILocator catalog;
    private List<SubChapter> subChapters = new();
    public List<SubChapter> SubChapters => subChapters;
    private string title;

    public string Title
    {
        get => title;
        set => title = value;
    }

    public Chapter(ILocator? _data, ILocator _catalog)
    {
        data = _data;
        catalog = _catalog;
    }

    public async Task<string> GetChapterTitle()
    {
        title = data == null ? string.Empty : await data.InnerTextAsync();
        return title;
    }

    public async Task GetSubChapters()
    {
        var scs = catalog.Locator(".posCatalog_level ul li");
        var count = await scs.CountAsync();
        for (int i = 0; i < count; i++)
        {
            var sc = scs.Nth(i);
            subChapters.Add(new SubChapter(sc.Locator(".posCatalog_name")));
        }
    }

    public async Task Click()
    {
        if (data != null) await data.ClickAsync();
    }
}

public class SubChapter
{
    private ILocator? data;
    private string title;

    public string Title
    {
        get => title;
        set => title = value;
    }

    public SubChapter(ILocator? _data)
    {
        data = _data;
    }

    public async Task<string> GetSubChapterTitle()
    {
        title = data == null ? string.Empty : await data.InnerTextAsync();
        return title;
    }

    public async Task Click()
    {
        if (data != null) await data.ClickAsync();
    }
}