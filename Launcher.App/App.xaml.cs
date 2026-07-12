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

using System.Globalization;
using System.Windows;
using Launcher.Application.DependencyInjection;
using Launcher.Application.Services;
using Launcher.App.Logging;
using Launcher.App.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.DependencyInjection;
using Launcher.Infrastructure.Persistence;
using Launcher.Infrastructure.Updates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Launcher.App;

public partial class App : System.Windows.Application
{
    private ServiceProvider? serviceProvider;
    private bool isUpdateApplyMode;

    public App()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (LauncherUpdateApplyOptions.Parse(args) is null
            && LauncherUpdateRecoveryOptions.Parse(args) is null)
        {
            ApplyLauncherCulture(
                new JsonSettingsService().LoadLauncherLanguageForBootstrap());
        }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        var updateApplyOptions = LauncherUpdateApplyOptions.Parse(e.Args);
        if (updateApplyOptions is not null)
        {
            isUpdateApplyMode = true;
            var exitCode = new LauncherUpdateApplyRunner().Run(updateApplyOptions);
            Shutdown(exitCode);
            return;
        }

        var updateRecoveryOptions = LauncherUpdateRecoveryOptions.Parse(e.Args);
        if (updateRecoveryOptions is not null)
        {
            isUpdateApplyMode = true;
            var exitCode = new LauncherUpdateApplyRunner().RunRecovery(updateRecoveryOptions);
            Shutdown(exitCode);
            return;
        }

        Log.Logger = LauncherLogConfiguration.CreateLogger();
        RegisterUnhandledExceptionLogging();

        try
        {
            Log.Information("Launcher startup started. ArgumentCount={ArgumentCount}", e.Args.Length);
            if (LauncherUpdateStartupCoordinator.TryStartPendingRecovery(
                    e.Args,
                    Environment.ProcessPath,
                    Environment.ProcessId))
            {
                Log.Information("Pending launcher update recovery process started.");
                Shutdown(0);
                return;
            }

            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog(Log.Logger, dispose: false);
            });
            services.AddLauncherApplication();
            services.AddLauncherInfrastructure();
            services.AddSingleton<IStatusService, StatusService>();
            services.AddSingleton<IFloatingMessageService, FloatingMessageService>();
            services.AddSingleton<IWindowService, WindowService>();
            services.AddSingleton<IClipboardService, ClipboardService>();
            services.AddSingleton<IFilePickerService, FilePickerService>();
            services.AddSingleton<IInstanceFolderService, InstanceFolderService>();
            services.AddSingleton<IExternalLinkService, ExternalLinkService>();
            services.AddSingleton<IApplicationExitService, ApplicationExitService>();
            services.AddSingleton<IAccountDialogService, AccountDialogService>();
            services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<IHomePageViewModelFactory, HomePageViewModelFactory>();
            services.AddSingleton<LauncherSessionCoordinator>();
            services.AddSingleton<LauncherStateSyncService>();
            services.AddSingleton<LauncherShutdownService>();
            services.AddSingleton<LaunchStatusDialogViewModel>();
            services.AddSingleton<UserAgreementDialogViewModel>();
            services.AddSingleton<AccountListViewModel>();
            services.AddSingleton<AccountDialogViewModel>();
            services.AddSingleton<AccountAppearanceViewModel>();
            services.AddSingleton<AccountOfflineUuidViewModel>();
            services.AddSingleton<AccountSkinModelDialogViewModel>();
            services.AddSingleton<AccountPageViewModel>();
            services.AddSingleton<DownloadTasksPageViewModel>();
            services.AddSingleton<DownloadLocalImportDialogViewModel>();
            services.AddSingleton<DownloadPageViewModel>();
            services.AddSingleton<GameSettingsEditDialogViewModel>();
            services.AddSingleton<GameSettingsDetailsViewModel>();
            services.AddSingleton<GameSettingsInstanceListViewModel>();
            services.AddSingleton<GameSettingsDialogsViewModel>();
            services.AddSingleton<GameSettingsPageViewModel>();
            services.AddSingleton<ResourcesPageViewModel>();
            services.AddSingleton<SettingsPageViewModel>();
            services.AddSingleton<InstanceManagementViewModel>();
            services.AddSingleton<LoaderSelectionViewModel>();
            services.AddSingleton<LocalModsViewModel>();
            services.AddSingleton<LocalSavesViewModel>();
            services.AddSingleton<LocalResourcePacksViewModel>();
            services.AddSingleton<LocalShaderPacksViewModel>();
            services.AddSingleton<ModrinthSearchViewModel>();
            services.AddSingleton<GameManagementViewModel>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();

            serviceProvider = services.BuildServiceProvider();
            Log.Information("Service provider built.");

            var updateCacheCleaner = serviceProvider.GetRequiredService<LauncherUpdateCacheCleaner>();
            updateCacheCleaner.CleanupStaleCache(Environment.ProcessPath);

            var startupSettings = await serviceProvider.GetRequiredService<ISettingsService>().LoadAsync();
            ApplyLauncherCulture(startupSettings.LauncherLanguage);
            Log.Information("Launcher culture initialized. Language={Language}", CultureInfo.CurrentUICulture.Name);
            base.OnStartup(e);

            await CleanupModpackWorkspacesOnStartupAsync();

            await RecoverPendingInstanceRenamesOnStartupAsync(
                serviceProvider.GetRequiredService<IInstanceRenameRecoveryService>());

            var mainViewModel = serviceProvider.GetRequiredService<MainViewModel>();
            await mainViewModel.PrimeAsync(startupSettings);
            serviceProvider.GetRequiredService<IThemeService>().ApplyPreference(
                mainViewModel.Settings.Theme,
                mainViewModel.Settings.ThemeFollowSystem,
                mainViewModel.Settings.LauncherBackgroundOpacityPercent,
                mainViewModel.Settings.DisableBackgroundBlur);
            serviceProvider.GetRequiredService<IThemeService>().ApplyAccent(mainViewModel.Settings.AccentColor);
            var mainWindow = serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
            Log.Information("Main window shown.");
            _ = CleanupPendingInstanceDeletionsOnStartupAsync(
                serviceProvider.GetRequiredService<IInstanceDeletionCleanupService>());
            _ = CleanupPendingInstanceInstallsOnStartupAsync(
                serviceProvider.GetRequiredService<IInstanceInstallCleanupService>());
            _ = CleanupModpackSandboxesOnStartupAsync(
                serviceProvider.GetRequiredService<IModpackSandboxCleanupService>());
            try
            {
                if (LauncherUpdateStartupCoordinator.TryConfirmStartup(
                        e.Args,
                        Environment.ProcessPath,
                        out var confirmedUpdaterPath))
                {
                    Log.Information("Launcher update startup confirmed.");
                    if (confirmedUpdaterPath is not null)
                        _ = CleanupConfirmedUpdateCacheAsync(updateCacheCleaner, confirmedUpdaterPath);
                }
            }
            catch (Exception exception)
            {
                Log.Error(exception, "Failed to confirm launcher update startup.");
            }
            _ = CheckForLauncherUpdatesAfterAgreementAsync(mainViewModel);
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Launcher startup failed.");
            Shutdown(-1);
        }
    }

    private static async Task CleanupConfirmedUpdateCacheAsync(
        LauncherUpdateCacheCleaner cacheCleaner,
        string updaterPath)
    {
        try
        {
            await cacheCleaner.CleanupConfirmedUpdateAsync(updaterPath).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Confirmed launcher update cache cleanup failed; startup cleanup will retry later.");
        }
    }

    private static async Task CleanupPendingInstanceDeletionsOnStartupAsync(
        IInstanceDeletionCleanupService cleanupService)
    {
        try
        {
            await cleanupService.CleanupPendingAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Pending instance deletion cleanup failed; startup cleanup will retry later.");
        }
    }

    private static async Task CleanupPendingInstanceInstallsOnStartupAsync(
        IInstanceInstallCleanupService cleanupService)
    {
        try
        {
            await cleanupService.CleanupPendingAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Pending instance installation cleanup failed; startup cleanup will retry later.");
        }
    }

    private static async Task CleanupModpackSandboxesOnStartupAsync(
        IModpackSandboxCleanupService cleanupService)
    {
        try
        {
            await cleanupService.CleanupStaleAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Modpack loader sandbox cleanup failed; startup cleanup will retry later.");
        }
    }

    private static async Task RecoverPendingInstanceRenamesOnStartupAsync(
        IInstanceRenameRecoveryService recoveryService)
    {
        try
        {
            await recoveryService.RecoverPendingAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Pending instance rename recovery failed before instance scanning.");
        }
    }

    private static async Task CheckForLauncherUpdatesAfterAgreementAsync(MainViewModel mainViewModel)
    {
        try
        {
            if (!await mainViewModel.WaitForUserAgreementDecisionAsync())
                return;

            await mainViewModel.SettingsPage.Info.CheckUpdatesOnStartupAsync();
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Unexpected failure while running startup launcher update check.");
        }
    }

    private static void ApplyLauncherCulture(string? language)
    {
        var normalizedLanguage = LauncherLanguages.Normalize(language);
        var culture = CultureInfo.GetCultureInfo(normalizedLanguage);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (isUpdateApplyMode)
        {
            base.OnExit(e);
            return;
        }

        try
        {
            Log.Information("Launcher exit started. ExitCode={ExitCode}", e.ApplicationExitCode);
            serviceProvider?.Dispose();
            LauncherLogConfiguration.PruneOldLogFiles(
                LauncherLogConfiguration.ResolveLogDirectory(),
                DateTimeOffset.Now);
            Log.Information("Launcher exit completed.");
        }
        finally
        {
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }

    private async Task CleanupModpackWorkspacesOnStartupAsync()
    {
        if (serviceProvider is null)
            return;

        try
        {
            await serviceProvider.GetRequiredService<IModpackWorkspaceCleanupService>()
                .CleanupAllAsync()
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Failed to clean modpack workspace cache on startup.");
        }
    }

    private void RegisterUnhandledExceptionLogging()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled dispatcher exception.");
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                Log.Fatal(exception, "Unhandled app domain exception. IsTerminating={IsTerminating}", args.IsTerminating);
            else
                Log.Fatal("Unhandled app domain exception object. IsTerminating={IsTerminating}", args.IsTerminating);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception.");
        };
    }
}
