using Avalonia.Controls;
using Avalonia.Interactivity;
using Siua.ViewModels;

namespace Siua.Views.Windows;

public partial class AiSettingsView : Window
{
    public AiSettingsView()
    {
        InitializeComponent();
        this.Loaded += OnViewLoaded;
    }
    private async void OnViewLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AiSettingsViewModel vm)
        {
            vm.RequestClose += Close;
        }
    }
}