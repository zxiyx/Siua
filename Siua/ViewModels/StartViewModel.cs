using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Siua.Common;
using Siua.Interfaces;
using Siua.Services;

namespace Siua.ViewModels;

public partial class StartViewModel :PageBase
{
    [ObservableProperty] private bool _isRunning = false;
    [ObservableProperty] private bool _isDownloading = false;
    private bool _mainLoopRunning = false;
    [ObservableProperty]
    private GlobalSettings _settings;
    private readonly ICoreService _coreService;
    private readonly PaddleOcrService _paddleOcrService;

    public StartViewModel(GlobalSettings globalSettings,ICoreService coreService,PaddleOcrService paddleOcrService) : base("开始", MaterialIconKind.Application, 0)
    {
        _settings = globalSettings;
        _coreService = coreService;
        _paddleOcrService = paddleOcrService;
    }
    [RelayCommand]
    public async Task StartRunning()
    {
        _mainLoopRunning = !_mainLoopRunning;
        if (IsRunning)
        {
            IsRunning = !IsRunning;
            _coreService.Dispose();
            return;
        }
        IsRunning = true;
        await _coreService.LoadPlaywright();
        if (Settings.SaveCookies)
        {
            
        }
        while (_mainLoopRunning)
        {
            await _coreService.ParsePage();
        }
        _coreService.StopLoginHeartbeat();
        IsRunning = false;
    }
    [RelayCommand]
    public async Task UpdateOCRModel()
    {
        IsDownloading = true;
        await _paddleOcrService.DownlLoadModel();
        IsDownloading = false;
    }

    
}