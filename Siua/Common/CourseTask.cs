using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Siua.Common;

public partial class CourseChoice(string url) : ObservableObject
{
    public string Url { get; } = url;
    [ObservableProperty] private bool _isSelected;
}

public partial class CourseTask : ObservableObject
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string CourseUrl { get; }
    public string Platform => Settings.CurrentPlatform;
    public string AccountName => Settings.GetSelectedLoginAccount()?.DisplayName ?? "手动登录";
    public string Label { get; }
    public GlobalSettings Settings { get; }
    internal CancellationTokenSource Cancellation { get; } = new();
    [ObservableProperty] private string _status = "排队中";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isFinished;
    [ObservableProperty] private bool _canRetry;
    public CourseTask(GlobalSettings settings, string url, int number)
    {
        Settings = settings;
        CourseUrl = url;
        Label = $"任务 {number} · {Platform}";
    }
    private bool CanStop() => !IsFinished && !Cancellation.IsCancellationRequested;
    [RelayCommand(CanExecute = nameof(CanStop))]
    public void Stop()
    {
        if (!CanStop()) return;
        Status = "正在停止";
        Cancellation.Cancel();
        StopCommand.NotifyCanExecuteChanged();
    }
}
