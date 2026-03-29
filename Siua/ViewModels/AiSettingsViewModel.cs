using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Siua.ViewModels;

public partial class AiSettingsViewModel:ObservableObject
{
    public Action? RequestClose;
    [ObservableProperty] private bool _isAdding;
    [ObservableProperty]
    private  GlobalSettings _settings;
    public AiSettingsViewModel(GlobalSettings globalSettings)
    {
        _settings = globalSettings;
    }
}