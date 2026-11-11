using Avalonia.Collections;
using Siua.Common;
using Siua.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Siua.Interfaces;
using Material.Icons;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace Siua.ViewModels;

/// <summary>管理主窗口导航、主题与页面状态。</summary>
public partial class MainViewModel : ViewModelBase
{
    public AvaloniaList<PageBase> Pages { get; set; }
    public PageNavigationService PageNavigationService { get; }
    public ISukiDialogManager DialogManager { get; }
    public ISukiToastManager ToastManager { get; }
    
    [ObservableProperty] private string _siuaVersion = "Siua v1.1.7";
    [ObservableProperty] private PageBase? _activePage;
    public NavigationEntry[] Navigation { get; } =
    [
        new("start", "工作台", MaterialIconKind.ViewDashboardOutline),
        new("logs", "运行日志", MaterialIconKind.TextBoxOutline),
        new("courses", "课程管理", MaterialIconKind.BookOpenVariant),
        new("accounts", "账号与登录", MaterialIconKind.AccountOutline),
        new("ai", "AI 模型", MaterialIconKind.RobotOutline),
        new("settings", "偏好设置", MaterialIconKind.CogOutline),
        new("about", "关于 Siua", MaterialIconKind.InformationOutline)
    ];
    [ObservableProperty] private NavigationEntry? _selectedNavigation;
    private bool _navigating;
    
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
            SyncNavigation();
        };
        ActivePage = Pages.FirstOrDefault();
        if (Pages.OfType<SettingsViewModel>().FirstOrDefault() is { } settings)
            settings.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(SettingsViewModel.ActiveSubPage)) SyncNavigation(); };
        SyncNavigation();
        _log.AddLog("Run Successfully !");
    }
    
    partial void OnSelectedNavigationChanged(NavigationEntry? value)
    {
        if (_navigating || value is null) return;
        _navigating = true;
        try
        {
            if (value.Key == "start") ActivePage = Pages.OfType<StartViewModel>().FirstOrDefault();
            else if (value.Key == "logs") ActivePage = Pages.OfType<LogViewModel>().FirstOrDefault();
            else if (value.Key == "about") ActivePage = Pages.OfType<AboutViewModel>().FirstOrDefault();
            else if (Pages.OfType<SettingsViewModel>().FirstOrDefault() is { } settings)
            {
                ActivePage = settings;
                settings.ActiveSubPage = value.Key switch
                {
                    "courses" => settings.CourseEditor, "accounts" => settings.AccountEditor,
                    "ai" => settings.AiSettings, _ => null
                };
            }
        }
        finally { _navigating = false; }
    }

    private void SyncNavigation()
    {
        if (_navigating) return;
        var key = ActivePage switch
        {
            StartViewModel => "start", LogViewModel => "logs", AboutViewModel => "about",
            SettingsViewModel s => s.ActiveSubPage switch
            { CourseEditViewModel => "courses", AccountEditViewModel => "accounts", AiSettingsViewModel => "ai", _ => "settings" },
            _ => "start"
        };
        _navigating = true;
        SelectedNavigation = Navigation.First(item => item.Key == key);
        _navigating = false;
    }

    [RelayCommand]
    private void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _log.AddLog(LogLevel.Error, "App", $"无法打开链接：{exception}");
        }
    }
}
