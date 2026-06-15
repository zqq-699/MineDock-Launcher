using System.Windows;
using Launcher.Application.DependencyInjection;
using Launcher.App.Services;
using Launcher.App.ViewModels;
using Launcher.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Launcher.App;

public partial class App : System.Windows.Application
{
    private ServiceProvider? serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddLauncherApplication();
        services.AddLauncherInfrastructure();
        services.AddSingleton<IStatusService, StatusService>();
        services.AddSingleton<IWindowService, WindowService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IAccountDialogService, AccountDialogService>();
        services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();
        services.AddSingleton<IHomePageViewModelFactory, HomePageViewModelFactory>();
        services.AddSingleton<AccountListViewModel>();
        services.AddSingleton<AccountDialogViewModel>();
        services.AddSingleton<AccountAppearanceViewModel>();
        services.AddSingleton<AccountOfflineUuidViewModel>();
        services.AddSingleton<AccountSkinModelDialogViewModel>();
        services.AddSingleton<AccountPageViewModel>();
        services.AddSingleton<DownloadTasksPageViewModel>();
        services.AddSingleton<DownloadPageViewModel>();
        services.AddSingleton<GameSettingsPageViewModel>();
        services.AddSingleton<InstanceManagementViewModel>();
        services.AddSingleton<LoaderSelectionViewModel>();
        services.AddSingleton<LocalModsViewModel>();
        services.AddSingleton<ModrinthSearchViewModel>();
        services.AddSingleton<GameManagementViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        serviceProvider = services.BuildServiceProvider();
        var mainWindow = serviceProvider.GetRequiredService<MainWindow>();
        await serviceProvider.GetRequiredService<MainViewModel>().PrimeAsync();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
