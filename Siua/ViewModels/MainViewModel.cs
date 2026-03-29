using Avalonia.Collections;
using Siua.Common;
using Siua.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Siua.Interfaces;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace Siua.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public AvaloniaList<PageBase> Pages { get; set; }
    public PageNavigationService PageNavigationService { get; }
    public ISukiDialogManager DialogManager { get; }
    public ISukiToastManager ToastManager { get; }
    
    [ObservableProperty] private PageBase? _activePage;
    
    private readonly ILogService _log;

    public MainViewModel(IEnumerable<PageBase> pages, PageNavigationService pageNavigationService,ISukiToastManager toastManager,
        ISukiDialogManager dialogManager, ILogService logService)
    {
        ToastManager = toastManager;
        DialogManager = dialogManager;
        _log = logService;
        Pages = new AvaloniaList<PageBase>(pages.OrderBy(x => x.Index).ThenBy(x => x.DisplayName));
        PageNavigationService = pageNavigationService;
        pageNavigationService.NavigationRequested += pageType =>
        {
            var page = Pages.FirstOrDefault(x => x.GetType() == pageType);
            if (page is null || ActivePage?.GetType() == pageType) return;
            ActivePage = page;
        };
        _log.AddLog("Run Successfully !");
    }
    
    [RelayCommand]
    private static void OpenUrl(string url)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Process.Start(new ProcessStartInfo(url.Replace("&", "^&")) { UseShellExecute = true });
        /*;
else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    Process.Start("xdg-open", url);
else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    Process.Start("open", url);
    */
    }
}