using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Siua.Common;
using Siua.ViewModels;
using Siua.Views;
using Microsoft.Extensions.DependencyInjection;
using Siua.Interfaces;
using Siua.Services;
using Siua.Views.Pages;
using Siua.Views.Windows;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace Siua;

public  partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            services.AddSingleton(desktop);
            var views = ConfigureViews(services);
            var provider = ConfigureServices(services);
            DataTemplates.Add(new ViewLocator(views));
            desktop.MainWindow = views.CreateView<MainViewModel>(provider) as Window;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var services = new ServiceCollection();
            services.AddSingleton(singleView);
            var views = ConfigureViews(services);
            var provider = ConfigureServices(services);
            DataTemplates.Add(new ViewLocator(views));
            // Ideally, we want to create a MainView that host app content
            // and use it for both IClassicDesktopStyleApplicationLifetime and ISingleViewApplicationLifetime
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static MainViews ConfigureViews(ServiceCollection services)
    {

        return new MainViews().AddView<MainView, MainViewModel>(services)
            .AddView<AiSettingsView, AiSettingsViewModel>(services)
            .AddView<CourseEditView, CourseEditViewModel>(services)
            .AddView<StartView, StartViewModel>(services)
            .AddView<AboutView, AboutViewModel>(services)
            .AddView<SettingsView, SettingsViewModel>(services)
            .AddView<LogView, LogViewModel>(services);
    }

    private static ServiceProvider ConfigureServices(ServiceCollection services)
    {
        services.AddSingleton<PageNavigationService>();
        services.AddScoped<GlobalSettings>(sp =>
        {
            var gs = new GlobalSettings();
            gs.LoadFromJson();
            return gs;
        });
        services.AddSingleton<PaddleOcrService>();
        services.AddSingleton<AiControlService>();
        services.AddSingleton<ILogService,LogService>();
        services.AddSingleton<ICoreService,CoreService>();
        services.AddSingleton<IShowWindowManager, ShowWindowManager>();
        services.AddSingleton<ISukiToastManager, SukiToastManager>();
        services.AddSingleton<ISukiDialogManager, SukiDialogManager>();
        return services.BuildServiceProvider();
    }
}