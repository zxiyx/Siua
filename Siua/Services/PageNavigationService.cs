using System;
using Siua.Common;

namespace Siua.Services;

/// <summary>负责主界面的页面导航状态。</summary>
public class PageNavigationService
{
    public event Action<Type>? NavigationRequested;

    public void RequestNavigation<T>() where T : PageBase
    {
        NavigationRequested?.Invoke(typeof(T));
    }
}
