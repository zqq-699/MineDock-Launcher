using Launcher.Application.Accounts;
using Launcher.Application.Repositories;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Accounts;
using Launcher.Infrastructure.FileSystem;
using Launcher.Infrastructure.Minecraft;
using Launcher.Infrastructure.Modrinth;
using Launcher.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Launcher.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLauncherInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<IGameInstanceRepository, JsonGameInstanceRepository>();
        services.AddSingleton<IGameVersionService, GameVersionService>();
        services.AddSingleton<ILoaderProvider, VanillaLoaderProvider>();
        services.AddSingleton<ILoaderProvider, FabricLoaderProvider>();
        services.AddSingleton<ILoaderProvider>(_ => new PlaceholderLoaderProvider(LoaderKind.Forge, "Forge"));
        services.AddSingleton<ILoaderProvider>(_ => new PlaceholderLoaderProvider(LoaderKind.NeoForge, "NeoForge"));
        services.AddSingleton<ILoaderProvider>(_ => new PlaceholderLoaderProvider(LoaderKind.Quilt, "Quilt"));
        services.AddSingleton<ILaunchService, LaunchService>();
        services.AddSingleton<IModService, ModService>();
        services.AddSingleton<IModrinthService, ModrinthService>();
        services.AddSingleton<IMicrosoftAccountService, MicrosoftAccountService>();
        return services;
    }
}
