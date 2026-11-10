using System;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Siua.Common;
using Siua.Interfaces;
using Siua.Services;

namespace Siua.ViewModels;

/// <summary>管理课程启动、执行策略与 OCR 服务操作。</summary>
public partial class StartViewModel :PageBase
{
    [ObservableProperty] private string _selectedPlatform;
    [ObservableProperty] private bool _isRunning = false;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallPix2TextCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartPix2TextCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopPix2TextCommand))]
    private bool _isInstallingPix2Text;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallPix2TextCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartPix2TextCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopPix2TextCommand))]
    private bool _isStartingPix2Text;
    private bool _mainLoopRunning = false;
    [ObservableProperty]
    private GlobalSettings _settings;
    private readonly ICoreService _coreService;
    private readonly CourseTaskScheduler _scheduler;
    public ObservableCollection<CourseChoice> CourseChoices { get; } = [];
    public ObservableCollection<CourseTask> Tasks => _scheduler.Tasks;
    public bool HasTasks => Tasks.Count > 0;
    public bool IsMultiCourseMode => Settings.MultiCourseMode;
    public int SelectedCourseCount => CourseChoices.Count(c => c.IsSelected);
    public int RunningTaskCount => Tasks.Count(t => !t.IsFinished && t.Status != "排队中");
    public int FinishedTaskCount => Tasks.Count(t => t.Status == "处理完成");
    public int QueuedTaskCount => Tasks.Count(t => t.Status == "排队中");
    public string SelectionSummary => IsMultiCourseMode ? $"已选择 {SelectedCourseCount} 门课程" : "单课程执行";
    [ObservableProperty] private int _workspaceTab;
    public string[] Platforms { get; } = [LearningPlatformCatalog.XueXiTong, LearningPlatformCatalog.ZhiHuiShu];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTaskMessage))]
    private string _taskMessage = "";
    public bool HasTaskMessage => !string.IsNullOrWhiteSpace(TaskMessage);
    private readonly Pix2TextService _pix2TextService;
    private readonly PageNavigationService _pageNavigationService;
    private readonly Dictionary<string, string> _selectedCourseByPlatform =
        new(StringComparer.Ordinal);
    private ObservableCollection<string> _observedCourses;
    private LoginPreferences _observedLogin;
    private bool _refreshingAccounts;
    public ObservableCollection<LoginAccount> LoginAccounts { get; } = [];
    [ObservableProperty] private LoginAccount? _selectedLoginAccount;
    public bool IsAutoLoginSupported => LearningPlatformCatalog.IsSupported(SelectedPlatform);
    public bool CanConfigureAutoLogin => !IsRunning && IsAutoLoginSupported;
    public bool CanSelectLoginAccount => CanConfigureAutoLogin && Settings.Login.Enabled && LoginAccounts.Count > 0;
    public string AutoLoginDescription => !IsAutoLoginSupported ? "当前平台需手动登录，可在设置中预先保存账号"
        : !Settings.Login.Enabled ? "本次在浏览器中手动登录。"
        : LoginAccounts.Count == 0 ? "请先在「账号与登录」中添加账号"
        : Settings.Login.Enabled && SelectedLoginAccount is null ? "请选择用于本次登录的账号"
        : "优先使用已保存的登录状态；无密码时手动登录，验证码需手动完成";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRunningCommand))]
    private string? _selectedCourse;
    public bool IsSelectedPlatformSupported => LearningPlatformCatalog.IsSupported(SelectedPlatform);
    public bool HasConfiguredCourse => Settings.Courses.Count > 0;
    public string PlatformAvailabilityText => !IsSelectedPlatformSupported
        ? "适配中 · 敬请期待"
        : HasConfiguredCourse
            ? "已适配 · 可以开始"
            : "已适配 · 请先添加课程地址";
    public string RunButtonText => IsRunning
        ? _mainLoopRunning ? IsMultiCourseMode ? "停止全部任务" : "停止任务" : "正在停止"
        : IsMultiCourseMode ? "启动所选课程" : "启动学习";
    public StartViewModel(GlobalSettings globalSettings,ICoreService coreService,Pix2TextService pix2TextService,
        PageNavigationService pageNavigationService, CourseTaskScheduler scheduler) : base("开始", MaterialIconKind.Application, 0)
    {
        _settings = globalSettings;
        _selectedPlatform = LearningPlatformCatalog.IsSupported(globalSettings.CurrentPlatform)
            ? globalSettings.CurrentPlatform
            : LearningPlatformCatalog.XueXiTong;
        if (!string.Equals(globalSettings.CurrentPlatform, _selectedPlatform, StringComparison.Ordinal))
            globalSettings.CurrentPlatform = _selectedPlatform;
        _coreService = coreService;
        _scheduler = scheduler;
        Tasks.CollectionChanged += (_, args) =>
        {
            if (args.NewItems is not null)
                foreach (CourseTask task in args.NewItems) task.PropertyChanged += (_, _) => RefreshTaskCounts();
            RefreshTaskCounts();
        };
        _pix2TextService = pix2TextService;
        _pageNavigationService = pageNavigationService;
        _observedCourses = Settings.Courses;
        _observedCourses.CollectionChanged += OnCoursesChanged;
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        _observedLogin = Settings.Login;
        _observedLogin.PropertyChanged += OnLoginChanged;
        Settings.Accounts.CollectionChanged += (_, _) => RefreshLoginAccounts();
        RefreshLoginAccounts();
        RefreshSelectedCourse();
        RefreshCourseChoices();
    }
    partial void OnSelectedPlatformChanged(string value)
    {
        Settings.CurrentPlatform = value;
        OnPropertyChanged(nameof(IsSelectedPlatformSupported));
        OnPropertyChanged(nameof(PlatformAvailabilityText));
        RefreshSelectedCourse();
        RefreshLoginAccounts();
        StartRunningCommand.NotifyCanExecuteChanged();
        RetryTaskCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedCourseChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            _selectedCourseByPlatform.Remove(SelectedPlatform);
        else
            _selectedCourseByPlatform[SelectedPlatform] = value;
    }

    partial void OnIsRunningChanged(bool value)
    {
        RetryTaskCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(RunButtonText));
        OnPropertyChanged(nameof(CanConfigureAutoLogin));
        OnPropertyChanged(nameof(CanSelectLoginAccount));
        StartRunningCommand.NotifyCanExecuteChanged();
    }

    private bool CanStartRunning() => IsRunning
        ? _mainLoopRunning
        : IsSelectedPlatformSupported &&
          (IsMultiCourseMode ? CourseChoices.Any(c => c.IsSelected) : SelectedCourse is not null && Settings.Courses.Contains(SelectedCourse)) &&
          (!IsAutoLoginSupported || !Settings.Login.Enabled || SelectedLoginAccount is not null);

    partial void OnSelectedLoginAccountChanged(LoginAccount? value)
    {
        if (!_refreshingAccounts) Settings.Login.SelectedAccountId = value?.Id;
        NotifyLoginState();
    }

    private void OnLoginChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(LoginPreferences.SelectedAccountId)) RefreshLoginAccounts();
        else NotifyLoginState();
    }

    private void RefreshLoginAccounts()
    {
        if (_refreshingAccounts) return;
        _refreshingAccounts = true;
        try
        {
            var selectedId = Settings.Login.SelectedAccountId;
            LoginAccounts.Clear();
            foreach (var account in Settings.Accounts.Where(a => a.Platform == Settings.CurrentPlatform)) LoginAccounts.Add(account);
            SelectedLoginAccount = LoginAccounts.FirstOrDefault(a => a.Id == selectedId);
        }
        finally { _refreshingAccounts = false; }
        NotifyLoginState();
    }

    private void NotifyLoginState()
    {
        OnPropertyChanged(nameof(IsAutoLoginSupported));
        OnPropertyChanged(nameof(CanConfigureAutoLogin));
        OnPropertyChanged(nameof(CanSelectLoginAccount));
        OnPropertyChanged(nameof(AutoLoginDescription));
        StartRunningCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanStartRunning))]
    public async Task StartRunning()
    {
        if (!IsSelectedPlatformSupported)
            return;

        if (IsRunning)
        {
            _mainLoopRunning = false;
            OnPropertyChanged(nameof(RunButtonText));
            StartRunningCommand.NotifyCanExecuteChanged();
            _scheduler.StopAll();
            _coreService.Dispose();
            return;
        }

        if (!CanStartRunning()) return;
        var courseUrl = SelectedCourse;

        _mainLoopRunning = true;
        IsRunning = true;
        try
        {
            TaskMessage = "";
            if (IsMultiCourseMode)
            {
                await _scheduler.RunAsync(Settings, CourseChoices.Where(c => c.IsSelected).Select(c => c.Url), Settings.MaxConcurrentCourses);
                return;
            }
            _pageNavigationService.RequestNavigation<LogViewModel>();
            if (!await _coreService.LoadPlaywright(courseUrl!))
                return;

            while (_mainLoopRunning &&
                   _coreService.IsSessionActive &&
                   await _coreService.ParsePage())
            {
            }
            await _coreService.SaveLoginStateAsync();
        }
        catch (Exception exception) { TaskMessage = $"任务未启动或已停止：{exception.GetType().Name}"; }
        finally
        {
            _mainLoopRunning = false;
            _coreService.Dispose();
            IsRunning = false;
        }
    }

    private bool CanRetryTask(CourseTask? task) => !IsRunning && task is { CanRetry: true };
    [RelayCommand(CanExecute = nameof(CanRetryTask))]
    private async Task RetryTask(CourseTask task)
    {
        IsRunning = true;
        _mainLoopRunning = true;
        OnPropertyChanged(nameof(RunButtonText));
        try { await _scheduler.RetryAsync(task, Settings); }
        catch (Exception exception) { TaskMessage = $"重试失败：{exception.Message}"; }
        finally { _mainLoopRunning = false; IsRunning = false; }
    }

    private void RefreshCourseChoices()
    {
        var selected = CourseChoices.Where(c => c.IsSelected).Select(c => c.Url).ToHashSet();
        foreach (var choice in CourseChoices) choice.PropertyChanged -= OnCourseChoiceChanged;
        CourseChoices.Clear();
        foreach (var url in Settings.Courses)
        {
            var choice = new CourseChoice(url) { IsSelected = selected.Contains(url) };
            choice.PropertyChanged += OnCourseChoiceChanged;
            CourseChoices.Add(choice);
        }
        OnPropertyChanged(nameof(SelectedCourseCount));
        OnPropertyChanged(nameof(SelectionSummary));
        StartRunningCommand.NotifyCanExecuteChanged();
    }
    private void OnCourseChoiceChanged(object? sender, PropertyChangedEventArgs args)
    {
        OnPropertyChanged(nameof(SelectedCourseCount));
        OnPropertyChanged(nameof(SelectionSummary));
        StartRunningCommand.NotifyCanExecuteChanged();
    }
    private void RefreshTaskCounts()
    {
        OnPropertyChanged(nameof(HasTasks));
        OnPropertyChanged(nameof(RunningTaskCount));
        OnPropertyChanged(nameof(FinishedTaskCount));
        OnPropertyChanged(nameof(QueuedTaskCount));
    }
    [RelayCommand]
    private void SelectAllCourses() { foreach (var course in CourseChoices) course.IsSelected = true; }
    [RelayCommand]
    private void ClearCourseSelection() { foreach (var course in CourseChoices) course.IsSelected = false; }
    private bool CanManagePix2Text() => !IsInstallingPix2Text && !IsStartingPix2Text;

    [RelayCommand(CanExecute = nameof(CanManagePix2Text))]
    public async Task InstallPix2Text()
    {
        IsInstallingPix2Text = true;
        try
        {
            await _pix2TextService.InstallAsync();
        }
        finally
        {
            IsInstallingPix2Text = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanManagePix2Text))]
    public async Task StartPix2Text()
    {
        IsStartingPix2Text = true;
        try
        {
            await _pix2TextService.EnsureReadyAsync();
        }
        catch (OperationCanceledException)
        {
            // 用户停止或修改服务地址会取消启动，属于正常操作。
        }
        finally
        {
            IsStartingPix2Text = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanManagePix2Text))]
    public async Task StopPix2Text() => await _pix2TextService.StopAsync();

    private void OnCoursesChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        RefreshCourseChoices();
        RefreshSelectedCourse();
        OnPropertyChanged(nameof(HasConfiguredCourse));
        OnPropertyChanged(nameof(PlatformAvailabilityText));
        StartRunningCommand.NotifyCanExecuteChanged();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(GlobalSettings.CurrentPlatform) && SelectedPlatform != Settings.CurrentPlatform)
            SelectedPlatform = Settings.CurrentPlatform;
        if (eventArgs.PropertyName == nameof(GlobalSettings.MultiCourseMode))
        {
            OnPropertyChanged(nameof(IsMultiCourseMode));
            OnPropertyChanged(nameof(SelectionSummary));
            OnPropertyChanged(nameof(RunButtonText));
            StartRunningCommand.NotifyCanExecuteChanged();
        }
        if (eventArgs.PropertyName == nameof(GlobalSettings.Login))
        {
            _observedLogin.PropertyChanged -= OnLoginChanged;
            _observedLogin = Settings.Login;
            _observedLogin.PropertyChanged += OnLoginChanged;
            RefreshLoginAccounts();
        }
        if (eventArgs.PropertyName != nameof(GlobalSettings.Courses))
            return;

        _observedCourses.CollectionChanged -= OnCoursesChanged;
        _observedCourses = Settings.Courses;
        _observedCourses.CollectionChanged += OnCoursesChanged;
        RefreshCourseChoices();
        RefreshSelectedCourse();
        OnPropertyChanged(nameof(HasConfiguredCourse));
        OnPropertyChanged(nameof(PlatformAvailabilityText));
        StartRunningCommand.NotifyCanExecuteChanged();
    }

    private void RefreshSelectedCourse()
    {
        if (_selectedCourseByPlatform.TryGetValue(SelectedPlatform, out var previous) &&
            Settings.Courses.Contains(previous))
        {
            SelectedCourse = previous;
            return;
        }

        SelectedCourse = Settings.Courses.FirstOrDefault();
    }
}
