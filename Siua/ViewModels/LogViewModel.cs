using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;
using Siua.Common;
using Siua.Interfaces;

namespace Siua.ViewModels;

public partial class LogViewModel :PageBase
{
    [ObservableProperty]
    private string? _logContent;
    private readonly ILogService _logService;
    public LogViewModel(ILogService logService) : base("日志", MaterialIconKind.TextBox, 1)
    {
        _logService = logService;
        _logService.OnLogAdded += (message) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                LogContent += message;
            });
        };
        _logService.OnLogCleared += () =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                LogContent= string.Empty;
            });
        };
    }
}




