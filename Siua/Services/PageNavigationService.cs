using System;
using Siua.Common;

namespace Siua.Services;

public class PageNavigationService
{
    public event Action<Type>? NavigationRequested;

    public void RequestNavigation<T>() where T : PageBase
    {
        NavigationRequested?.Invoke(typeof(T));
    }
}