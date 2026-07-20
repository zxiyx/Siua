using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Siua.Common;

namespace Siua.ViewModels;

/// <summary>管理全局设置页面及其子页面导航。</summary>
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
    public string Pix2TextEndpointDescription => Pix2TextEndpoint.TryCreate(
        Settings.Pix2TextHost,
        Settings.Pix2TextPort,
        out _,
        out var serviceUri,
        out var errorMessage)
        ? $"当前连接地址：{serviceUri}"
        : errorMessage;
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
        Settings.PropertyChanged += OnSettingsPropertyChanged;
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

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(GlobalSettings.Pix2TextHost) or
            nameof(GlobalSettings.Pix2TextPort))
        {
            OnPropertyChanged(nameof(Pix2TextEndpointDescription));
        }
    }
}
