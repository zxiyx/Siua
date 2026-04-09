using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Data;
using Avalonia.Data.Converters;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Siua.Services;

namespace Siua.ViewModels;

public partial class AiSettingsViewModel:ObservableObject
{
    public Action? RequestClose;

    private static readonly Dictionary<string, AiProvider> ConfigMap = 
        AiProviderService.Providers.ToDictionary(p => p.Provider, p => p);
    [ObservableProperty] private bool _isAdding;
    public bool IsCustom => SelectedProvider == "自定义";
    [ObservableProperty]
    private string _selectedProvider = "DeepSeek";
    
    [ObservableProperty]
    private  GlobalSettings _settings;
    public AiSettingsViewModel(GlobalSettings globalSettings)
    {
        _settings = globalSettings;
        if (_settings.CurrentAi.AiProvider != null)
        {
            SelectedProvider = _settings.CurrentAi.AiProvider;
        }
        
    }

    partial void OnSelectedProviderChanged(string value)
    {
        if (value == "自定义")
        {
            OnPropertyChanged(nameof(IsCustom));
            return;
        }
        if (ConfigMap.TryGetValue(value, out var config))
        {
            Settings.CurrentAi.Domain = config.Domain;
            Settings.CurrentAi.AiProvider = config.Provider;
        }
        OnPropertyChanged(nameof(IsCustom));
    }
}
public class ProviderToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is string selected && parameter is string current && selected == current;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter is string provider)
            return provider;
        return BindingOperations.DoNothing;
    }
}