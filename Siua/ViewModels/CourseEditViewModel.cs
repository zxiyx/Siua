using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Siua.Common;
using SukiUI.Controls;
using SukiUI.Toasts;

namespace Siua.ViewModels;

/// <summary>管理当前平台的课程地址列表。</summary>
public partial class CourseEditViewModel:ViewModelBase
{
    public Action? RequestClose;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCourseCommand))]
    private string? _selectedCourse;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCourseCommand))]
    [NotifyPropertyChangedFor(nameof(CourseValidationMessage))]
    private string? _newCourse;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty]
    private  GlobalSettings _settings;
    private ObservableCollection<string> _observedCourses;
    
    public CourseEditViewModel(GlobalSettings globalSettings)
    {
        _settings = globalSettings;
        _observedCourses = Settings.Courses;
        _observedCourses.CollectionChanged += OnCoursesChanged;
        Settings.PropertyChanged += OnSettingsPropertyChanged;
    }
    public string CourseValidationMessage
    {
        get
        {
            var course = NewCourse?.Trim();
            if (string.IsNullOrWhiteSpace(course))
                return "请输入进入课程后的完整 HTTP(S) 地址";
            if (!LearningPlatformCatalog.TryValidateCourseUrl(
                    Settings.CurrentPlatform, course, out var normalized, out var errorMessage))
                return errorMessage;
            return Settings.Courses.Any(existing =>
                string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))
                ? "该课程地址已存在"
                : $"地址将保存到「{Settings.CurrentPlatform}」课程列表";
        }
    }
    partial void OnSelectedCourseChanged(string? value)
    {
        if (value is not null)
            NewCourse = value;
    }

    private bool CanAddCourse()
    {
        var course = NewCourse?.Trim();
        return !string.IsNullOrWhiteSpace(course) &&
               LearningPlatformCatalog.TryValidateCourseUrl(
                   Settings.CurrentPlatform, course, out var normalized, out _) &&
               !Settings.Courses.Any(existing =>
                   string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand(CanExecute = nameof(CanAddCourse))]
    public void AddCourse()
    {
        if (!LearningPlatformCatalog.TryValidateCourseUrl(
                Settings.CurrentPlatform, NewCourse, out var course, out _))
            return;

        Settings.Courses.Add(course);
        SelectedCourse = course;
        NewCourse = string.Empty;
    }

    private bool CanRemoveCourse() => SelectedCourse is not null;

    [RelayCommand(CanExecute = nameof(CanRemoveCourse))]
    public void RemoveCourse()
    {
        if (SelectedCourse is null)
            return;

        Settings.Courses.Remove(SelectedCourse);
        SelectedCourse = null;
        NewCourse = string.Empty;
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke();

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(GlobalSettings.Courses))
        {
            _observedCourses.CollectionChanged -= OnCoursesChanged;
            _observedCourses = Settings.Courses;
            _observedCourses.CollectionChanged += OnCoursesChanged;
            SelectedCourse = null;
            NewCourse = string.Empty;
        }

        if (eventArgs.PropertyName is nameof(GlobalSettings.Courses) or nameof(GlobalSettings.CurrentPlatform))
        {
            OnPropertyChanged(nameof(CourseValidationMessage));
            AddCourseCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnCoursesChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        OnPropertyChanged(nameof(CourseValidationMessage));
        AddCourseCommand.NotifyCanExecuteChanged();
    }
}
