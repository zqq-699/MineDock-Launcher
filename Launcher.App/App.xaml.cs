using System.Windows;
using Launcher.App.ViewModels;
using Launcher.Core.Models;
using Launcher.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Launcher.App;

public partial class App : Application
{
    private ServiceProvider? serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<IGameVersionService, GameVersionService>();
        services.AddSingleton<ILoaderProvider, VanillaLoaderProvider>();
        services.AddSingleton<ILoaderProvider, FabricLoaderProvider>();
        services.AddSingleton<ILoaderProvider>(_ => new PlaceholderLoaderProvider(LoaderKind.Forge, "Forge"));
        services.AddSingleton<ILoaderProvider>(_ => new PlaceholderLoaderProvider(LoaderKind.NeoForge, "NeoForge"));
        services.AddSingleton<ILoaderProvider>(_ => new PlaceholderLoaderProvider(LoaderKind.Quilt, "Quilt"));
        services.AddSingleton<IGameInstanceService, GameInstanceService>();
        services.AddSingleton<ILaunchService, LaunchService>();
        services.AddSingleton<IModService, ModService>();
        services.AddSingleton<IModrinthService, ModrinthService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        serviceProvider = services.BuildServiceProvider();
        serviceProvider.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
