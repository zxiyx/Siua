using System;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core;

public class Doc
{
    private IFrame handle;
    public bool IcCompleted =>isCompleted;
    private bool isCompleted;
    public Doc(IFrame data,bool isc)
    {
        handle = data;
        isCompleted = isc;

    }
    
    public async Task ScrollToEnd()
    {
        await handle.Locator("ul > li").Last.ScrollIntoViewIfNeededAsync();
        /*
        var c = await imags.CountAsync();
        Console.WriteLine("共{0}个图片",c);
        for (int i = 0; i < c; i++)
            await imags.Nth(i).ScrollIntoViewIfNeededAsync();
            */
        await handle.WaitForTimeoutAsync(2000);
    }
    
    
    
}