using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Siua.ViewModels;

namespace Siua.Views.Windows;

public partial class CourseEditView : Window
{
    public CourseEditView()
    {
        InitializeComponent();
        this.Loaded += OnViewLoaded;
    }
    private async void OnViewLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CourseEditViewModel vm)
        {
            vm.RequestClose += Close;
        }
    }
}