using System;
using System.Collections.ObjectModel;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SukiUI.Controls;
using SukiUI.Toasts;

namespace Siua.ViewModels;

public partial class CourseEditViewModel:ViewModelBase
{
    public Action? RequestClose;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCourseCommand))]
    private string? _selectedCourse;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCourseCommand))]
    private string? _newCourse;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty]
    private  GlobalSettings _settings;
    
    public CourseEditViewModel(GlobalSettings globalSettings)
    {
        _settings = globalSettings;
    }
    partial void OnSelectedCourseChanged(string? value)
    {
        if (value is not null)
            NewCourse = value;
    }

    private bool CanAddCourse()
    {
        var course = NewCourse?.Trim();
        return !string.IsNullOrWhiteSpace(course) && !Settings.Courses.Contains(course);
    }

    [RelayCommand(CanExecute = nameof(CanAddCourse))]
    public void AddCourse()
    {
        var course = NewCourse!.Trim();
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
}