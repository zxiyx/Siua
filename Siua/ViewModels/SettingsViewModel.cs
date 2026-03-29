using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Siua.Common;
using Siua.Interfaces;
using Siua.Views.Pages;
using Siua.Views.Windows;
using SukiUI.Dialogs;

namespace Siua.ViewModels;

public partial class SettingsViewModel : PageBase
{
    [ObservableProperty] private ObservableCollection<string> _browsers = new()
    {
        "系统默认","Edge"
    };
    [ObservableProperty]private string _currentBrowser;
    [ObservableProperty]
    private  GlobalSettings _settings;
    private readonly IShowWindowManager _showWindowManager;
    
    public SettingsViewModel(GlobalSettings globalSettings,IShowWindowManager showWindowManager) : base("设置", MaterialIconKind.Settings, 1000)
    {
        _settings = globalSettings;
        _showWindowManager = showWindowManager;
        CurrentBrowser = Settings.BrowserCannel;
    }
    [RelayCommand]
    partial void OnCurrentBrowserChanged(string value)
    {
        Settings.BrowserCannel = value;
    }

    public  async Task ShowAiSetting()
    {
        await _showWindowManager.ShowDialogAsync<AiSettingsView,AiSettingsViewModel>();
    }
    [RelayCommand]
    public async Task ShowCourseList()
    {
        await _showWindowManager.ShowDialogAsync<CourseEditView, CourseEditViewModel>();
    }
}