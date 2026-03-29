using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using Siua.Interfaces;

namespace Siua.Services;

public class ShowWindowManager:IShowWindowManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;

    public ShowWindowManager(IServiceProvider serviceProvider,IClassicDesktopStyleApplicationLifetime desktop)
    {
        _serviceProvider = serviceProvider;
        _desktop = desktop;
    }
    public void Show<TWindow, TViewModel>()
        where TWindow : Window, new()
        where TViewModel : class
    {
        var window = new TWindow { DataContext = _serviceProvider.GetRequiredService<TViewModel>() };
        window.Show();
    }

    public async Task ShowDialogAsync<TWindow, TViewModel>(Window owner)
        where TWindow : Window, new()
        where TViewModel : class
    {
        var window = new TWindow { DataContext = _serviceProvider.GetRequiredService<TViewModel>() };
        await window.ShowDialog(owner);
    }
    public async Task ShowDialogAsync<TWindow, TViewModel>()
        where TWindow : Window, new()
        where TViewModel : class
    {
        var owner = _desktop.MainWindow 
                    ?? throw new InvalidOperationException("无主窗口");
        await ShowDialogAsync<TWindow, TViewModel>(owner);
    }
}