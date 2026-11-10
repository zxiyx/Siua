using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Siua.Common;
using Siua.Interfaces;

namespace Siua.Services;

/// <summary>一批任务共用并发额度，每门课独立创建 CoreService 和配置快照。</summary>
public sealed class CourseTaskScheduler : IDisposable
{
    private readonly Func<GlobalSettings, ILogService, CancellationToken, ICoreService> _factory;
    private readonly ILogService _log;
    private bool _disposed;
    private int _number;
    public ObservableCollection<CourseTask> Tasks { get; } = [];
    public bool IsRunning { get; private set; }
    public CourseTaskScheduler(GlobalSettings settings, ILogService log, Pix2TextService ocr)
        : this(log, (snapshot, taskLog, token) => new CoreService(taskLog, snapshot, ocr,
            new AiControlService(snapshot, taskLog, token))) { }
    public CourseTaskScheduler(ILogService log,
        Func<GlobalSettings, ILogService, CancellationToken, ICoreService> factory)
    { _log = log; _factory = factory; }

    public async Task RunAsync(GlobalSettings settings, IEnumerable<string> courses, int concurrency)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRunning) return;
        var urls = courses.Distinct(StringComparer.Ordinal).ToArray();
        if (urls.Length == 0) return;
        foreach (var url in urls)
            if (!LearningPlatformCatalog.TryValidateCourseUrl(settings.CurrentPlatform, url, out _, out var error))
                throw new ArgumentException(error);
        foreach (var previous in Tasks)
        { previous.Settings.Dispose(); previous.Cancellation.Dispose(); }
        Tasks.Clear();
        foreach (var url in urls) Tasks.Add(new CourseTask(settings.CreateRunSnapshot(), url, ++_number));
        await ExecuteBatchAsync(Tasks.ToArray(), concurrency);
    }

    public async Task RetryAsync(CourseTask task, GlobalSettings currentSettings)
    {
        if (_disposed || IsRunning || !task.CanRetry) return;
        // 使用原任务的课程策略；账号必须仍存在，避免删除后继续使用已删除凭据。
        var snapshot = task.Settings.CreateRunSnapshot();
        if (snapshot.Login.Enabled)
        {
            var account = currentSettings.Accounts.FirstOrDefault(a => a.Id == snapshot.Login.SelectedAccountId && a.Platform == snapshot.CurrentPlatform);
            if (account is null) { snapshot.Dispose(); throw new InvalidOperationException("账号已删除，请重新选择账号启动课程"); }
            snapshot.Accounts.Clear();
            snapshot.Accounts.Add(account);
            snapshot.Login.SelectedAccountId = account.Id;
            snapshot.Login.Enabled = true;
        }
        var replacement = new CourseTask(snapshot, task.CourseUrl, ++_number);
        Tasks.Add(replacement);
        task.CanRetry = false;
        await ExecuteBatchAsync([replacement], currentSettings.MaxConcurrentCourses);
    }

    private async Task ExecuteBatchAsync(CourseTask[] tasks, int concurrency)
    {
        IsRunning = true;
        using var slots = new SemaphoreSlim(Math.Clamp(concurrency, 1, 8));
        try { await Task.WhenAll(tasks.Select(task => ExecuteAsync(task, slots))); }
        finally { IsRunning = false; }
    }

    private async Task ExecuteAsync(CourseTask task, SemaphoreSlim slots)
    {
        var token = task.Cancellation.Token;
        var entered = false;
        ICoreService? core = null;
        using var scope = LogService.BeginTask(task.Label);
        var taskLog = new TaskLog(_log, task.Label);
        try
        {
            await slots.WaitAsync(token);
            entered = true;
            token.ThrowIfCancellationRequested();
            task.Status = "登录中 / 等待验证";
            core = _factory(task.Settings, taskLog, token);
            // LoadPlaywright 同步建立取消源后才注册停止，覆盖浏览器启动与登录等待。
            var loading = core.LoadPlaywright(task.CourseUrl);
            using var stop = token.Register(core.Dispose);
            if (!await loading) { token.ThrowIfCancellationRequested(); task.Status = "登录或启动失败"; return; }
            token.ThrowIfCancellationRequested();
            task.Status = "运行中";
            while (core.IsSessionActive && await core.ParsePage()) token.ThrowIfCancellationRequested();
            token.ThrowIfCancellationRequested();
            await core.SaveLoginStateAsync();
            task.Status = core.CompletedSuccessfully && !taskLog.HasErrors ? "处理完成" : "已结束，请检查日志";
            task.CanRetry = !core.CompletedSuccessfully || taskLog.HasErrors;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { task.Status = "已停止"; }
        catch (Exception exception)
        {
            task.Status = "执行失败";
            // 不转发可能携带认证信息的异常正文。
            taskLog.AddLog(LogLevel.Error, "Task", $"课程任务异常（{exception.GetType().Name}）");
        }
        finally
        {
            core?.Dispose();
            if (token.IsCancellationRequested) task.Status = "已停止";
            task.IsFinished = true;
            task.CanRetry = task.Status != "处理完成";
            if (entered) slots.Release();
        }
    }

    public void StopAll() { foreach (var task in Tasks) task.Stop(); }
    public void Dispose()
    {
        _disposed = true;
        StopAll();
        // 快照不落盘；运行任务的取消源留给执行流程使用到结束。
        foreach (var task in Tasks) task.Settings.Dispose();
    }

    private sealed class TaskLog(ILogService inner, string label) : ILogService
    {
        private int _hasErrors;
        public bool HasErrors => Volatile.Read(ref _hasErrors) != 0;
        public event Action<LogEntry>? OnLogAdded { add => inner.OnLogAdded += value; remove => inner.OnLogAdded -= value; }
        public event Action? OnLogCleared { add => inner.OnLogCleared += value; remove => inner.OnLogCleared -= value; }
        public void AddLog(string message)
        {
            using var scope = LogService.BeginTask(label);
            inner.AddLog(message);
        }
        public void AddLog(LogLevel level, string source, string message)
        {
            using var scope = LogService.BeginTask(label);
            if (level == LogLevel.Error) Interlocked.Exchange(ref _hasErrors, 1);
            inner.AddLog(level, source, message);
        }
        public void Clear() { /* 一个任务不能清空其它任务的日志。 */ }
    }
}
