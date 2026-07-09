using Launcher.Application.Accounts;
using Launcher.Application.Repositories;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Accounts;
using Launcher.Infrastructure.CurseForge;
using Launcher.Infrastructure.FileSystem;
using Launcher.Infrastructure.Minecraft;
using Launcher.Infrastructure.Modpacks;
using Launcher.Infrastructure.Modrinth;
using Launcher.Infrastructure.Platform;
using Launcher.Infrastructure.Persistence;
using Launcher.Infrastructure.Resources;
using Launcher.Infrastructure.Updates;
using Microsoft.Extensions.DependencyInjection;

namespace Launcher.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLauncherInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<LauncherPathProvider>();
        services.AddSingleton<IDownloadSpeedLimitState, DownloadSpeedLimitState>();
        services.AddSingleton<IImportConcurrencyLimiter>(_ => ImportConcurrencyLimiter.Shared);
        services.AddSingleton<ICurseForgeApiKeyResolver, CurseForgeApiKeyResolver>();
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<IAccountStateService, JsonAccountStateService>();
        services.AddSingleton<IGameInstanceRepository, JsonGameInstanceRepository>();
        services.AddSingleton<IGameVersionService, GameVersionService>();
        services.AddSingleton<ILoaderProvider, VanillaLoaderProvider>();
        services.AddSingleton<ILoaderProvider, FabricLoaderProvider>();
        services.AddSingleton<ILoaderProvider, ForgeLoaderProvider>();
        services.AddSingleton<ILoaderProvider, NeoForgeLoaderProvider>();
        services.AddSingleton<ILoaderProvider, QuiltLoaderProvider>();
        services.AddSingleton<ILaunchService, LaunchService>();
        services.AddSingleton<IGameLanguageService, GameLanguageService>();
        services.AddSingleton<IJavaRuntimeDiscoveryService, JavaRuntimeDiscoveryService>();
        services.AddSingleton<IJavaRuntimeSelectionService, JavaRuntimeSelectionService>();
        services.AddSingleton<IJavaRuntimeProvisioningService, CmlLibJavaRuntimeProvisioningService>();
        services.AddSingleton<ISystemMemoryService, WindowsSystemMemoryService>();
        services.AddSingleton<IModService, ModService>();
        services.AddSingleton<ILocalModIconEnrichmentService, LocalModIconEnrichmentService>();
        services.AddSingleton<IInstanceBackupService, InstanceBackupService>();
        services.AddSingleton<ILocalSaveService, LocalSaveService>();
        services.AddSingleton<IModpackGameInstaller, ModpackGameInstaller>();
        services.AddSingleton<IModpackInstanceStagingService, ModpackInstanceStagingService>();
        services.AddSingleton<CurseForgeApiClient>();
        services.AddSingleton<ModrinthApiClient>();
        services.AddSingleton<IModpackPackageService, LocalModpackPackageService>();
        services.AddSingleton<IModpackExportService, ModpackExportService>();
        services.AddSingleton<IModpackWorkspaceCleanupService, ModpackWorkspaceCleanupService>();
        services.AddSingleton<ILocalResourcePackService, LocalResourcePackService>();
        services.AddSingleton<ILocalShaderPackService, LocalShaderPackService>();
        services.AddSingleton<IModrinthService, ModrinthService>();
        services.AddSingleton<IResourceCatalogService, ResourceCatalogService>();
        services.AddSingleton<ILauncherUpdateService, GitHubLauncherUpdateService>();
        services.AddSingleton<ILauncherSelfUpdateService, LauncherSelfUpdateService>();
        services.AddSingleton<ILauncherStateMonitor, LauncherStateMonitor>();
        services.AddSingleton<IMicrosoftAccountService, MicrosoftAccountService>();
        services.AddSingleton<IAccountSkinLibraryService, AccountSkinLibraryService>();
        services.AddSingleton<IMinecraftSkinFileValidator, MinecraftSkinFileValidator>();
        services.AddSingleton<IOfflineAccountUuidService, OfflineAccountUuidService>();
        services.AddSingleton(serviceProvider => new MicrosoftAuthProvider(serviceProvider.GetRequiredService<LauncherPathProvider>()));
        services.AddSingleton<ILaunchAccountSessionService, LaunchAccountSessionService>();
        return services;
    }
}
