/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Accounts;
using Launcher.Application.Repositories;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Accounts;
using Launcher.Infrastructure.Accounts.ThirdParty;
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
        services.AddSingleton<IVersionDirectoryState, VersionDirectoryState>();
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
        services.AddSingleton<IResourceProjectInstallationService, ResourceProjectInstallationService>();
        services.AddSingleton<ILauncherUpdateService, RemoteManifestLauncherUpdateService>();
        services.AddSingleton<ILauncherSelfUpdateService, LauncherSelfUpdateService>();
        services.AddSingleton<ILauncherStateMonitor, LauncherStateMonitor>();
        services.AddSingleton<IInstanceDirectoryMonitor, InstanceDirectoryMonitor>();
        services.AddSingleton<IInstanceContentImportPathValidator, InstanceContentImportPathValidator>();
        services.AddSingleton<IExistingFilePathValidator, ExistingFilePathValidator>();
        services.AddSingleton<IMicrosoftAccountService, MicrosoftAccountService>();
        services.AddSingleton<IThirdPartyAccountTokenStore, DpapiThirdPartyAccountTokenStore>();
        services.AddSingleton<IThirdPartyAccountService, ThirdPartyAccountService>();
        services.AddSingleton<IThirdPartyLaunchSessionService, ThirdPartyLaunchSessionService>();
        services.AddSingleton<IAuthlibInjectorProvisioningService, AuthlibInjectorProvisioningService>();
        services.AddSingleton<IAccountSkinLibraryService, AccountSkinLibraryService>();
        services.AddSingleton<IMinecraftSkinFileValidator, MinecraftSkinFileValidator>();
        services.AddSingleton<IOfflineAccountUuidService, OfflineAccountUuidService>();
        services.AddSingleton(serviceProvider => new MicrosoftAuthProvider(serviceProvider.GetRequiredService<LauncherPathProvider>()));
        services.AddSingleton<ILaunchAccountSessionService, LaunchAccountSessionService>();
        return services;
    }
}
