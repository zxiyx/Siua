using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Data;
using Avalonia.Data.Converters;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Siua.Services;

namespace Siua.ViewModels;

/// <summary>管理 AI 服务商与模型连接配置。</summary>
public partial class AiSettingsViewModel:ObservableObject
{
    public Action? RequestClose;
    private readonly ApiConnectionTester _connectionTester;
    private CancellationTokenSource? _connectionTestCancellation;
    [ObservableProperty] private bool _isTestingConnection;
    [ObservableProperty] private bool _isConnectionInfoOpen;
    [ObservableProperty] private string _connectionInfoMessage = string.Empty;
    [ObservableProperty] private NotificationType _connectionInfoSeverity = NotificationType.Information;

    private static readonly Dictionary<string, AiProvider> ConfigMap = 
        AiProviderService.Providers.ToDictionary(p => p.Provider, p => p);
    [ObservableProperty] private bool _isAdding;
    public bool IsCustom => SelectedProvider == "自定义";
    [ObservableProperty]
    private string _selectedProvider = string.Empty;
    
    [ObservableProperty]
    private  GlobalSettings _settings;
    public AiSettingsViewModel(GlobalSettings globalSettings, ApiConnectionTester connectionTester)
    {
        _connectionTester = connectionTester;
        _settings = globalSettings;
        SelectedProvider = string.IsNullOrWhiteSpace(_settings.CurrentAi.AiProvider)
            ? "DeepSeek"
            : _settings.CurrentAi.AiProvider;
    }

    partial void OnSelectedProviderChanged(string value)
    {
        if (value == "自定义")
        {
            Settings.CurrentAi.AiProvider = value;
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

    [RelayCommand]
    private async Task TestApiConnection()
    {
        using var cancellation = new CancellationTokenSource();
        _connectionTestCancellation = cancellation;
        IsTestingConnection = true;
        IsConnectionInfoOpen = false;
        try
        {
            var result = await _connectionTester.TestAsync(Settings.CurrentAi.Domain, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            ConnectionInfoMessage = $"连通{(result.IsSuccess ? "正常" : "错误")}（{result.ElapsedMilliseconds} ms）";
            ConnectionInfoSeverity = result.IsSuccess ? NotificationType.Success : NotificationType.Error;
            IsConnectionInfoOpen = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            _connectionTestCancellation = null;
            IsTestingConnection = false;
        }
    }

    [RelayCommand]
    private void Close()
    {
        _connectionTestCancellation?.Cancel();
        IsConnectionInfoOpen = false;
        RequestClose?.Invoke();
    }
}
/// <summary>将 AI 服务商选择状态转换为绑定布尔值。</summary>
public class ProviderToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string selected && parameter is string current && selected == current;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter is string provider)
            return provider;
        return BindingOperations.DoNothing;
    }
}
