using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Data;
using Avalonia.Data.Converters;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Siua.Common;
using Siua.Interfaces;
using Siua.Services;

namespace Siua.ViewModels;

public partial class StartViewModel :PageBase
{
    [ObservableProperty] private string _selectedPlatform;
    [ObservableProperty] private bool _isRunning = false;
    [ObservableProperty] private bool _isDownloading = false;
    private bool _mainLoopRunning = false;
    [ObservableProperty]
    private GlobalSettings _settings;
    private readonly ICoreService _coreService;
    private readonly Pix2TextService _pix2TextService;
    public bool IsSelectedPlatformSupported => SelectedPlatform == "学习通";
    public string PlatformAvailabilityText => IsSelectedPlatformSupported ? "已适配 · 可以开始" : "适配中 · 敬请期待";
    public StartViewModel(GlobalSettings globalSettings,ICoreService coreService,Pix2TextService pix2TextService) : base("开始", MaterialIconKind.Application, 0)
    {
        _settings = globalSettings;
        _selectedPlatform = string.IsNullOrWhiteSpace(globalSettings.CurrentPlatform)
            ? "学习通"
            : globalSettings.CurrentPlatform;
        _coreService = coreService;
        _pix2TextService = pix2TextService;
    }
    partial void OnSelectedPlatformChanged(string value)
    {
        Settings.CurrentPlatform = value;
        OnPropertyChanged(nameof(IsSelectedPlatformSupported));
        OnPropertyChanged(nameof(PlatformAvailabilityText));
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task StartRunning()
    {
        if (!IsSelectedPlatformSupported)
            return;

        if (IsRunning)
        {
            _mainLoopRunning = false;
            _coreService.Dispose();
            IsRunning = false;
            return;
        }

        _mainLoopRunning = true;
        IsRunning = true;
        try
        {
            if (!await _coreService.LoadPlaywright())
                return;

            while (_mainLoopRunning &&
                   _coreService.IsSessionActive &&
                   await _coreService.ParsePage())
            {
            }
        }
        finally
        {
            _mainLoopRunning = false;
            _coreService.Dispose();
            IsRunning = false;
        }
    }
    [RelayCommand]
    public async Task InitializePix2Text()
    {
        IsDownloading = true;
        await _pix2TextService.EnsureReadyAsync();
        IsDownloading = false;
    }

    
}

public class StringEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string selected && parameter is string current && selected == current;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter is string current ? current : BindingOperations.DoNothing;
}