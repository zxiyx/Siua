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
    [ObservableProperty]private string _selectedCourse;
    [ObservableProperty]private string _newCourse;
    [ObservableProperty] private bool _isSaving;
     [ObservableProperty]
     private  GlobalSettings _settings;
    
    public CourseEditViewModel(GlobalSettings globalSettings)
    {
        _settings = globalSettings;
    }
    partial void OnSelectedCourseChanged(string value)
    {
         NewCourse = value;
    }
    [RelayCommand]
    public void AddCourse()
    {
        Settings.Courses.Add(NewCourse);
    }
    [RelayCommand]
    public void SubCourse()
    {
        Settings.Courses.Remove(NewCourse);
    }
}