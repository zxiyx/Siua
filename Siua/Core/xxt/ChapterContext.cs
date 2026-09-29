using System;
using System.Threading;
using Microsoft.Playwright;

namespace Siua.Core.Xxt;

/// <summary>只取消已离开的章节；子播放器自身出错仍由原错误流程处理。</summary>
internal sealed class XxtChapterContext : IDisposable
{
    private readonly IPage _page;
    private readonly IFrame _frame;
    private readonly CancellationTokenSource _cancellation;
    private readonly object _sync = new();
    private int _changed;
    private int _stopped;

    public XxtChapterContext(IPage page, IFrame frame, CancellationToken cancellationToken)
    {
        _page = page;
        _frame = frame;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _page.FrameDetached += OnFrameChanged;
        _page.FrameNavigated += OnFrameChanged;
        if (_frame.IsDetached)
            OnFrameChanged(this, _frame);
    }

    public CancellationToken Token => _cancellation.Token;
    public bool HasChanged => Volatile.Read(ref _changed) != 0;

    private void OnFrameChanged(object? sender, IFrame frame)
    {
        lock (_sync)
        {
            if (Volatile.Read(ref _stopped) != 0 || _page.IsClosed ||
                (frame != _frame && frame != _page.MainFrame))
                return;

            Interlocked.Exchange(ref _changed, 1);
            _cancellation.Cancel();
        }
    }

    // 主动点击下一节前停止观察，避免将自己的导航误判为外部切换。
    public void StopObserving()
    {
        lock (_sync)
        {
            Interlocked.Exchange(ref _stopped, 1);
            _page.FrameDetached -= OnFrameChanged;
            _page.FrameNavigated -= OnFrameChanged;
        }
    }

    public void Dispose()
    {
        StopObserving();
        _cancellation.Dispose();
    }
}
