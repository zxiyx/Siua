using System;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core;

public class Doc
{
    private readonly IFrame _frame;

    public Doc(IFrame frame, bool isCompleted)
    {
        _frame = frame;
        IsCompleted = isCompleted;
    }

    public bool IsCompleted { get; }

    public async Task ScrollToEndAsync()
    {
        await _frame.Locator("ul > li").Last.ScrollIntoViewIfNeededAsync();
        await _frame.WaitForTimeoutAsync(2000);
    }
    
    
    
}