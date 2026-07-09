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
    private static readonly TimeSpan ExitBackgroundTaskWaitTimeout = TimeSpan.FromSeconds(5);
    private ServiceProvider? serviceProvider;
    private bool isUpdateApplyMode;

    public App()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (LauncherUpdateApplyOptions.Parse(args) is null)
            ApplyLauncherCulture(new JsonSettingsService().LoadAsync().GetAwaiter().GetResult().LauncherLanguage);
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

        Log.Logger = LauncherLogConfiguration.CreateLogger();
        RegisterUnhandledExceptionLogging();

        try
        {
            Log.Information("Launcher startup started. ArgumentCount={ArgumentCount}", e.Args.Length);
            Log.Information("Launcher culture initialized. Language={Language}", CultureInfo.CurrentUICulture.Name);
            base.OnStartup(e);

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
            services.AddSingleton<LaunchStatusDialogViewModel>();
            services.AddSingleton<AccountListViewModel>();
            services.AddSingleton<AccountDialogViewModel>();
            services.AddSingleton<AccountAppearanceViewModel>();
            services.AddSingleton<AccountOfflineUuidViewModel>();
            services.AddSingleton<AccountSkinModelDialogViewModel>();
            services.AddSingleton<AccountPageViewModel>();
            services.AddSingleton<DownloadTasksPageViewModel>();
            services.AddSingleton<DownloadLocalImportDialogViewModel>();
            services.AddSingleton<DownloadPageViewModel>();
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

            await CleanupModpackWorkspacesOnStartupAsync();

            var mainViewModel = serviceProvider.GetRequiredService<MainViewModel>();
            await mainViewModel.PrimeAsync();
            serviceProvider.GetRequiredService<IThemeService>().ApplyPreference(
                mainViewModel.Settings.Theme,
                mainViewModel.Settings.ThemeFollowSystem,
                mainViewModel.Settings.LauncherBackgroundOpacityPercent,
                mainViewModel.Settings.DisableBackgroundBlur);
            serviceProvider.GetRequiredService<IThemeService>().ApplyAccent(mainViewModel.Settings.AccentColor);
            var mainWindow = serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
            Log.Information("Main window shown.");
            _ = CheckForLauncherUpdatesOnStartupAsync(mainViewModel);
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Launcher startup failed.");
            Log.CloseAndFlush();
            throw;
        }
    }

    private static async Task CheckForLauncherUpdatesOnStartupAsync(MainViewModel mainViewModel)
    {
        try
        {
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
            CancelAndCleanupBackgroundDownloadsOnExit();
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

    private void CancelAndCleanupBackgroundDownloadsOnExit()
    {
        if (serviceProvider is null)
            return;

        try
        {
            var downloadTasksPage = serviceProvider.GetService<DownloadTasksPageViewModel>();
            if (downloadTasksPage is not null)
            {
                downloadTasksPage.CancelAllRunningTasks();
                var completed = downloadTasksPage
                    .WaitForTrackedBackgroundTasksAsync(ExitBackgroundTaskWaitTimeout)
                    .GetAwaiter()
                    .GetResult();
                if (!completed)
                    Log.Warning("Timed out waiting for background download tasks during launcher exit.");
            }
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Failed to cancel background download tasks during launcher exit.");
        }

        try
        {
            serviceProvider.GetRequiredService<IModpackWorkspaceCleanupService>()
                .CleanupAllAsync()
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Failed to clean modpack workspace cache during launcher exit.");
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
