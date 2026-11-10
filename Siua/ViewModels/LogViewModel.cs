using System;
using System.Collections.ObjectModel;
using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Siua.Common;
using Siua.Interfaces;

namespace Siua.ViewModels;

/// <summary>管理运行日志的展示与筛选状态。</summary>
public partial class LogViewModel :PageBase
{
    
    private const int MaxLogs = 3000;
    [ObservableProperty] private bool _showInfo = true;
    [ObservableProperty] private bool _showError = true;
    [ObservableProperty] private bool _showBrowser = false;
    public ObservableCollection<string> TaskFilters { get; } = ["全部任务"];
    [ObservableProperty] private string _selectedTaskFilter = "全部任务";
    partial void OnSelectedTaskFilterChanged(string value) => RefreshFilter();
    public AvaloniaList<LogEntry> AllLogs { get; } = new();
    public ObservableCollection<LogEntry> Logs { get; } = new();
    
    private readonly ILogService _logService;
    public LogViewModel(ILogService logService) : base("日志", MaterialIconKind.TextBox, 1)
    {
        _logService = logService;
        _logService.OnLogAdded += entry =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                AllLogs.Add(entry);
                if (entry.TaskLabel is not null && !TaskFilters.Contains(entry.TaskLabel)) TaskFilters.Add(entry.TaskLabel);
                if (AllLogs.Count > MaxLogs) AllLogs.RemoveAt(0);
                if (MatchesFilter(entry))
                {
                    Logs.Add(entry);
                    if (Logs.Count > MaxLogs) Logs.RemoveAt(0);
                }
            });
        };
        _logService.OnLogCleared += () =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                AllLogs.Clear();
                Logs.Clear();
                TaskFilters.Clear();
                TaskFilters.Add("全部任务");
                SelectedTaskFilter = "全部任务";
            });
        };
    }
    partial void OnShowInfoChanged(bool value) => RefreshFilter();
    partial void OnShowErrorChanged(bool value) => RefreshFilter();
    partial void OnShowBrowserChanged(bool value) => RefreshFilter();
    private void RefreshFilter()
    {
        Logs.Clear();
        foreach (var e in AllLogs)
            if (MatchesFilter(e)) Logs.Add(e);
    }

    private bool MatchesFilter(LogEntry e)
    {
        bool levelOk = e.Level switch
        {
            LogLevel.Info => ShowInfo,
            LogLevel.Error => ShowError,
            LogLevel.Browser => ShowBrowser,
            _ => true
        };
        if (!levelOk) return false;
        return SelectedTaskFilter == "全部任务" || e.TaskLabel == SelectedTaskFilter;
    }

    [RelayCommand]
    private void ClearLogs() => _logService.Clear();
}




