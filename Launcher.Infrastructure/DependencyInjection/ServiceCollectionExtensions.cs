using Launcher.Application.Accounts;
using Launcher.Application.Repositories;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Accounts;
using Launcher.Infrastructure.FileSystem;
using Launcher.Infrastructure.Minecraft;
using Launcher.Infrastructure.Modrinth;
using Launcher.Infrastructure.Platform;
using Launcher.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Launcher.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLauncherInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IDownloadSpeedLimitState, DownloadSpeedLimitState>();
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<IGameInstanceRepository, JsonGameInstanceRepository>();
        services.AddSingleton<IGameVersionService, GameVersionService>();
        services.AddSingleton<ILoaderProvider, VanillaLoaderProvider>();
        services.AddSingleton<ILoaderProvider, FabricLoaderProvider>();
        services.AddSingleton<ILoaderProvider, ForgeLoaderProvider>();
        services.AddSingleton<ILoaderProvider>(_ => new PlaceholderLoaderProvider(LoaderKind.NeoForge));
        services.AddSingleton<ILoaderProvider>(_ => new PlaceholderLoaderProvider(LoaderKind.Quilt));
        services.AddSingleton<ILaunchService, LaunchService>();
        services.AddSingleton<IJavaRuntimeDiscoveryService, JavaRuntimeDiscoveryService>();
        services.AddSingleton<IJavaRuntimeSelectionService, JavaRuntimeSelectionService>();
        services.AddSingleton<ISystemMemoryService, WindowsSystemMemoryService>();
        services.AddSingleton<IModService, ModService>();
        services.AddSingleton<IModrinthService, ModrinthService>();
        services.AddSingleton<ILauncherStateMonitor, LauncherStateMonitor>();
        services.AddSingleton<IMicrosoftAccountService, MicrosoftAccountService>();
        services.AddSingleton<IAccountSkinLibraryService, AccountSkinLibraryService>();
        services.AddSingleton<IMinecraftSkinFileValidator, MinecraftSkinFileValidator>();
        services.AddSingleton<IOfflineAccountUuidService, OfflineAccountUuidService>();
        services.AddSingleton(_ => new MicrosoftAuthProvider(new LauncherPathProvider()));
        services.AddSingleton<ILaunchAccountSessionService, LaunchAccountSessionService>();
        return services;
    }
}
