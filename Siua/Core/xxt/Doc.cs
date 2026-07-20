using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core.Xxt;

public sealed class XxtDocument
{
    private readonly IFrame _frame;

    public XxtDocument(IFrame frame, bool isCompleted)
    {
        _frame = frame;
        IsCompleted = isCompleted;
    }

    public bool IsCompleted { get; }

    public async Task ScrollToEndAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _frame.Locator("ul > li").Last.ScrollIntoViewIfNeededAsync();
        await Task.Delay(2_000, cancellationToken);
    }
    
    
    
}
