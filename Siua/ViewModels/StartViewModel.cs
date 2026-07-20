using System;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
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
    private readonly Pix2TextService _pix2TextService;
    private readonly Dictionary<string, string> _selectedCourseByPlatform =
        new(StringComparer.Ordinal);
    private ObservableCollection<string> _observedCourses;
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
        ? _mainLoopRunning ? "停止任务" : "正在停止"
        : "启动学习";
    public StartViewModel(GlobalSettings globalSettings,ICoreService coreService,Pix2TextService pix2TextService) : base("开始", MaterialIconKind.Application, 0)
    {
        _settings = globalSettings;
        _selectedPlatform = string.IsNullOrWhiteSpace(globalSettings.CurrentPlatform)
            ? "学习通"
            : globalSettings.CurrentPlatform;
        _coreService = coreService;
        _pix2TextService = pix2TextService;
        _observedCourses = Settings.Courses;
        _observedCourses.CollectionChanged += OnCoursesChanged;
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        RefreshSelectedCourse();
    }
    partial void OnSelectedPlatformChanged(string value)
    {
        Settings.CurrentPlatform = value;
        OnPropertyChanged(nameof(IsSelectedPlatformSupported));
        OnPropertyChanged(nameof(PlatformAvailabilityText));
        RefreshSelectedCourse();
        StartRunningCommand.NotifyCanExecuteChanged();
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
        OnPropertyChanged(nameof(RunButtonText));
        StartRunningCommand.NotifyCanExecuteChanged();
    }

    private bool CanStartRunning() => IsRunning
        ? _mainLoopRunning
        : IsSelectedPlatformSupported &&
          SelectedCourse is not null &&
          Settings.Courses.Contains(SelectedCourse);

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanStartRunning))]
    public async Task StartRunning()
    {
        if (!IsSelectedPlatformSupported)
            return;

        var courseUrl = SelectedCourse;
        if (string.IsNullOrWhiteSpace(courseUrl) || !Settings.Courses.Contains(courseUrl))
            return;

        if (IsRunning)
        {
            _mainLoopRunning = false;
            OnPropertyChanged(nameof(RunButtonText));
            StartRunningCommand.NotifyCanExecuteChanged();
            _coreService.Dispose();
            return;
        }

        _mainLoopRunning = true;
        IsRunning = true;
        try
        {
            if (!await _coreService.LoadPlaywright(courseUrl))
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
        finally
        {
            IsStartingPix2Text = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanManagePix2Text))]
    public async Task StopPix2Text() => await _pix2TextService.StopAsync();

    private void OnCoursesChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        RefreshSelectedCourse();
        OnPropertyChanged(nameof(HasConfiguredCourse));
        OnPropertyChanged(nameof(PlatformAvailabilityText));
        StartRunningCommand.NotifyCanExecuteChanged();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(GlobalSettings.Courses))
            return;

        _observedCourses.CollectionChanged -= OnCoursesChanged;
        _observedCourses = Settings.Courses;
        _observedCourses.CollectionChanged += OnCoursesChanged;
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

/// <summary>将字符串相等关系转换为绑定布尔值。</summary>
public class StringEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string selected && parameter is string current && selected == current;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter is string current ? current : BindingOperations.DoNothing;
}
