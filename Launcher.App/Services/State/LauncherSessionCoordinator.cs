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

using System.ComponentModel;
using Launcher.App.Models;
using Launcher.App.Resources;
using Launcher.App.ViewModels.Download;
using Launcher.App.ViewModels.GameSettings;
using Launcher.App.ViewModels.Home;
using Launcher.App.ViewModels.Resources;
using Launcher.App.ViewModels.Settings;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.Services;

public sealed class LauncherSessionCoordinator : IDisposable
{
    private readonly IDownloadSpeedLimitState downloadSpeedLimitState;
    private readonly ISettingsService settingsService;
    private readonly IStatusService statusService;
    private readonly DownloadPageViewModel downloadPage;
    private readonly GameSettingsPageViewModel gameSettingsPage;
    private readonly ResourcesPageViewModel resourcesPage;
    private readonly SettingsPageViewModel settingsPage;
    private readonly GameManagementViewModel gameManagement;
    private readonly ILogger<LauncherSessionCoordinator> logger;
    private readonly SemaphoreSlim stateSynchronizationLock = new(1, 1);
    private HomePageViewModel? homePage;
    private LauncherSettings? settings;
    private bool isAttached;
    private bool isInitialized;

    public LauncherSessionCoordinator(
        IDownloadSpeedLimitState downloadSpeedLimitState,
        ISettingsService settingsService,
        IStatusService statusService,
        DownloadPageViewModel downloadPage,
        GameSettingsPageViewModel gameSettingsPage,
        ResourcesPageViewModel resourcesPage,
        SettingsPageViewModel settingsPage,
        GameManagementViewModel gameManagement,
        ILogger<LauncherSessionCoordinator>? logger = null)
    {
        this.downloadSpeedLimitState = downloadSpeedLimitState;
        this.settingsService = settingsService;
        this.statusService = statusService;
        this.downloadPage = downloadPage;
        this.gameSettingsPage = gameSettingsPage;
        this.resourcesPage = resourcesPage;
        this.settingsPage = settingsPage;
        this.gameManagement = gameManagement;
        this.logger = logger ?? NullLogger<LauncherSessionCoordinator>.Instance;
    }

    public event Action<string>? NavigationRequested;

    public event Action<double>? ProgressChanged;

    public void Attach(HomePageViewModel homePage)
    {
        ArgumentNullException.ThrowIfNull(homePage);
        if (isAttached)
            throw new InvalidOperationException("The launcher session coordinator is already attached.");

        this.homePage = homePage;
        downloadPage.InstanceInstalled += DownloadPage_InstanceInstalled;
        resourcesPage.ModpackImported += DownloadPage_InstanceInstalled;
        resourcesPage.ModpackManualDownloadsRequested += ResourcesPage_ModpackManualDownloadsRequested;
        gameManagement.PropertyChanged += GameManagement_PropertyChanged;
        gameSettingsPage.LaunchInstanceRequested += GameSettingsPage_LaunchInstanceRequested;
        gameSettingsPage.OnlineModInstallRequested += GameSettingsPage_OnlineModInstallRequested;
        gameSettingsPage.InstancesChanged += GameSettingsPage_InstancesChanged;
        settingsPage.LaunchDefaultsChanged += SettingsPage_LaunchDefaultsChanged;
        settingsPage.DownloadSourceChanged += SettingsPage_DownloadSourceChanged;
        settingsPage.DownloadSpeedLimitChanged += SettingsPage_DownloadSpeedLimitChanged;
        settingsPage.MinecraftDirectoryChanged += SettingsPage_MinecraftDirectoryChanged;
        isAttached = true;
    }

    public async Task PrimeAsync(LauncherSettings settings)
    {
        this.settings = settings;
        downloadSpeedLimitState.SetDownloadSpeedLimitMbPerSecond(settings.DownloadSpeedLimitMbPerSecond);
        await gameManagement.PrimeInstancesAsync(settings);
        homePage?.SetSettings(settings);
        homePage?.SetLaunchInstances(gameManagement.Instances);
        homePage?.Initialize(settings, gameManagement.SelectedInstance);
        downloadPage.PrimeFromSettings(settings);
        gameSettingsPage.PrimeFromSettings(settings);
        settingsPage.PrimeFromSettings(settings);
    }

    public async Task InitializeAsync()
    {
        if (isInitialized || settings is null)
            return;

        homePage?.SetSettings(settings);
        await gameManagement.InitializeAsync(settings);
        if (homePage is not null)
        {
            await homePage.EnsureVersionTypesLoadedAsync();
            SynchronizeHomeInstances();
            homePage.Initialize(settings, gameManagement.SelectedInstance);
        }

        isInitialized = true;
    }

    public async Task SyncCurrentStateAsync(string currentPage)
    {
        if (!isInitialized || !await stateSynchronizationLock.WaitAsync(0))
            return;

        try
        {
            if (NavigationCatalog.IsPage(currentPage, NavigationCatalog.HomePage))
            {
                await RefreshHomeInstancesAsync();
            }
            else
            {
                await gameManagement.EnsureInstancesLoadedAsync();
                if (homePage is not null)
                    await homePage.EnsureVersionTypesLoadedAsync();
                SynchronizeHomeInstances();
            }

            if (NavigationCatalog.IsPage(currentPage, NavigationCatalog.DownloadPage))
                await downloadPage.EnsureVersionsLoadedAsync();

            if (NavigationCatalog.IsPage(currentPage, NavigationCatalog.GameSettingsPage))
                await gameSettingsPage.RefreshInstancesForPageActivationAsync();
        }
        finally
        {
            stateSynchronizationLock.Release();
        }
    }

    public Task<bool> SelectLaunchInstanceAsync(GameInstance instance)
    {
        return gameManagement.SelectLaunchInstanceAsync(instance);
    }

    public async Task<bool> SetHomeLaunchMenuPinnedAsync(bool isPinned)
    {
        if (settings is null)
            return false;

        var previousValue = settings.IsHomeLaunchMenuPinned;
        if (previousValue == isPinned)
            return true;

        settings.IsHomeLaunchMenuPinned = isPinned;
        try
        {
            await settingsService.SaveAsync(settings);
            logger.LogInformation("Home launch menu pin preference saved. IsPinned={IsPinned}", isPinned);
            return true;
        }
        catch (Exception exception)
        {
            settings.IsHomeLaunchMenuPinned = previousValue;
            logger.LogWarning(
                exception,
                "Failed to save home launch menu pin preference. IsPinned={IsPinned}",
                isPinned);
            return false;
        }
    }

    public void Dispose()
    {
        if (!isAttached)
            return;

        downloadPage.InstanceInstalled -= DownloadPage_InstanceInstalled;
        resourcesPage.ModpackImported -= DownloadPage_InstanceInstalled;
        resourcesPage.ModpackManualDownloadsRequested -= ResourcesPage_ModpackManualDownloadsRequested;
        gameManagement.PropertyChanged -= GameManagement_PropertyChanged;
        gameSettingsPage.LaunchInstanceRequested -= GameSettingsPage_LaunchInstanceRequested;
        gameSettingsPage.OnlineModInstallRequested -= GameSettingsPage_OnlineModInstallRequested;
        gameSettingsPage.InstancesChanged -= GameSettingsPage_InstancesChanged;
        settingsPage.LaunchDefaultsChanged -= SettingsPage_LaunchDefaultsChanged;
        settingsPage.DownloadSourceChanged -= SettingsPage_DownloadSourceChanged;
        settingsPage.DownloadSpeedLimitChanged -= SettingsPage_DownloadSpeedLimitChanged;
        settingsPage.MinecraftDirectoryChanged -= SettingsPage_MinecraftDirectoryChanged;
        stateSynchronizationLock.Dispose();
        isAttached = false;
    }

    private async Task RefreshHomeInstancesAsync()
    {
        await gameManagement.RefreshInstancesAsync();
        if (homePage is not null)
            await homePage.EnsureVersionTypesLoadedAsync();
        SynchronizeHomeInstances();
    }

    private void SynchronizeHomeInstances()
    {
        homePage?.SetLaunchInstances(gameManagement.Instances);
        homePage?.SetSelectedInstance(gameManagement.SelectedInstance);
    }

    private void GameManagement_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameManagementViewModel.SelectedInstance))
            homePage?.SetSelectedInstance(gameManagement.SelectedInstance);

        if (e.PropertyName == nameof(GameManagementViewModel.ProgressPercent))
            ProgressChanged?.Invoke(gameManagement.ProgressPercent);
    }

    private void DownloadPage_InstanceInstalled(object? sender, GameInstance instance)
    {
        Observe(HandleInstalledInstanceAsync(instance), "synchronize an installed instance");
    }

    private async Task HandleInstalledInstanceAsync(GameInstance instance)
    {
        if (gameManagement.Instances.All(existing => existing.Id != instance.Id))
            gameManagement.Instances.Add(instance);

        try
        {
            var saved = await gameManagement.SelectLaunchInstanceAsync(instance);
            if (!saved)
                gameManagement.SelectedInstance = instance;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to persist installed instance selection. InstanceId={InstanceId}", instance.Id);
            gameManagement.SelectedInstance = instance;
        }

        gameSettingsPage.AddOrUpdateInstance(instance);
        homePage?.SetLaunchInstances(gameManagement.Instances);
        homePage?.SetSelectedInstance(instance);
    }

    private void ResourcesPage_ModpackManualDownloadsRequested(
        object? sender,
        ResourcesModpackManualDownloadsRequestedEventArgs args)
    {
        downloadPage.ModpackManualDownloadsDialog.Show(args.Instance, args.ManualDownloads);
    }

    private void GameSettingsPage_LaunchInstanceRequested(GameInstance instance)
    {
        Observe(HandleGameSettingsLaunchRequestAsync(instance), "select a launch instance from game settings");
    }

    private async Task HandleGameSettingsLaunchRequestAsync(GameInstance instance)
    {
        try
        {
            var saved = await gameManagement.SelectLaunchInstanceAsync(instance);
            if (!saved)
            {
                statusService.Report(Strings.Status_LaunchInstanceSelectionFailed);
                return;
            }

            SynchronizeHomeInstances();
            NavigationRequested?.Invoke(NavigationCatalog.HomePage);
            statusService.Report(string.Format(Strings.Status_LaunchInstanceSelectedFormat, instance.Name));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to select launch instance from game settings. InstanceId={InstanceId}", instance.Id);
            statusService.Report(Strings.Status_LaunchInstanceSelectionFailed);
        }
    }

    private void GameSettingsPage_OnlineModInstallRequested(GameInstance instance)
    {
        Observe(OpenResourcesModsForInstanceAsync(instance), "open online resources for an instance");
    }

    private async Task OpenResourcesModsForInstanceAsync(GameInstance instance)
    {
        NavigationRequested?.Invoke(NavigationCatalog.ResourcesPage);
        await resourcesPage.OpenModsForInstanceAsync(instance);
    }

    private void GameSettingsPage_InstancesChanged(GameSettingsInstancesChangedEventArgs args)
    {
        if (args.Kind is GameSettingsInstancesChangedKind.Updated && args.UpdatedInstance is not null)
        {
            gameManagement.ApplyUpdatedInstance(args.UpdatedInstance);
            SynchronizeHomeInstances();
            return;
        }

        Observe(SynchronizeInstancesFromGameSettingsAsync(), "synchronize instances from game settings");
    }

    private async Task SynchronizeInstancesFromGameSettingsAsync()
    {
        try
        {
            await gameManagement.RefreshInstancesAsync();
            SynchronizeHomeInstances();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to synchronize instances from game settings.");
            statusService.Report(Strings.Status_LoadInstancesFailed);
        }
    }

    private void SettingsPage_LaunchDefaultsChanged(object? sender, EventArgs e)
    {
        if (settings is null)
            return;

        homePage?.SetSettings(settings);
        gameSettingsPage.PrimeFromSettings(settings);
    }

    private void SettingsPage_DownloadSourceChanged(object? sender, SettingsDownloadSourceChangedEventArgs e)
    {
        if (settings is not null)
            settings.DownloadSourcePreference = e.Preference;
        downloadPage.ApplyDownloadSourcePreference(e.Preference);
        gameManagement.ApplyDownloadSourcePreference(e.Preference);
    }

    private void SettingsPage_DownloadSpeedLimitChanged(object? sender, SettingsDownloadSpeedLimitChangedEventArgs e)
    {
        if (settings is not null)
            settings.DownloadSpeedLimitMbPerSecond = e.DownloadSpeedLimitMbPerSecond;
        downloadSpeedLimitState.SetDownloadSpeedLimitMbPerSecond(e.DownloadSpeedLimitMbPerSecond);
        downloadPage.ApplyDownloadSpeedLimit(e.DownloadSpeedLimitMbPerSecond);
        gameManagement.ApplyDownloadSpeedLimit(e.DownloadSpeedLimitMbPerSecond);
    }

    private void SettingsPage_MinecraftDirectoryChanged(object? sender, SettingsMinecraftDirectoryChangedEventArgs e)
    {
        if (settings is null)
            return;

        settings.MinecraftDirectory = e.MinecraftDirectory;
        homePage?.SetSettings(settings);
        gameSettingsPage.PrimeFromSettings(settings);
        Observe(RefreshMinecraftDirectoryInstancesAsync(), "refresh instances after changing the Minecraft directory");
    }

    private async Task RefreshMinecraftDirectoryInstancesAsync()
    {
        try
        {
            await gameManagement.RefreshInstancesAsync();
            if (homePage is not null)
                await homePage.EnsureVersionTypesLoadedAsync();
            SynchronizeHomeInstances();
            await gameSettingsPage.RefreshInstancesSilentlyAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to refresh instances after changing the Minecraft directory.");
            statusService.Report(Strings.Status_LoadInstancesFailed);
        }
    }

    private void Observe(Task task, string operation)
    {
        _ = ObserveAsync(task, operation);
    }

    private async Task ObserveAsync(Task task, string operation)
    {
        try
        {
            await task;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to {Operation}.", operation);
        }
    }
}
