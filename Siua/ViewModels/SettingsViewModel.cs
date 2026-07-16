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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverview))]
    [NotifyPropertyChangedFor(nameof(IsSubPageOpen))]
    private ObservableObject? _activeSubPage;

    public bool IsOverview => ActiveSubPage is null;
    public bool IsSubPageOpen => ActiveSubPage is not null;
    public AiSettingsViewModel AiSettings { get; }
    public CourseEditViewModel CourseEditor { get; }
    
    public SettingsViewModel(GlobalSettings globalSettings, AiSettingsViewModel aiSettings,
        CourseEditViewModel courseEditor) : base("设置", MaterialIconKind.Settings, 1000)
    {
        _settings = globalSettings;
        AiSettings = aiSettings;
        CourseEditor = courseEditor;
        AiSettings.RequestClose += CloseSubPage;
        CourseEditor.RequestClose += CloseSubPage;
        CurrentBrowser = Settings.BrowserCannel;
    }
    partial void OnCurrentBrowserChanged(string value)
    {
        Settings.BrowserCannel = value;
    }

    [RelayCommand]
    private void ShowAiSetting() => ActiveSubPage = AiSettings;

    [RelayCommand]
    private void ShowCourseList() => ActiveSubPage = CourseEditor;

    private void CloseSubPage() => ActiveSubPage = null;
}