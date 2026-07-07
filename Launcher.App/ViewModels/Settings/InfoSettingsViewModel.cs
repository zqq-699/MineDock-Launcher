using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;

namespace Launcher.App.ViewModels.Settings;

public sealed partial class InfoSettingsViewModel : SettingsSectionViewModelBase
{
    public const string GithubRepositoryUrl = "https://github.com/zhouquan050906-cpu/launcher_z";

    private readonly IStatusService statusService;
    private readonly IFloatingMessageService floatingMessageService;
    private readonly IExternalLinkService externalLinkService;
    private readonly ILauncherUpdateService launcherUpdateService;
    private string? updateDialogReleasePageUrl;
    private string? updateDialogDownloadUrl;

    public InfoSettingsViewModel(
        SettingsPageViewModel parent,
        IStatusService statusService,
        IFloatingMessageService floatingMessageService,
        IExternalLinkService externalLinkService,
        ILauncherUpdateService launcherUpdateService)
        : base(parent)
    {
        this.statusService = statusService;
        this.floatingMessageService = floatingMessageService;
        this.externalLinkService = externalLinkService;
        this.launcherUpdateService = launcherUpdateService;
        LauncherVersionText = ResolveLauncherVersion();
    }

    public string LauncherVersionText { get; }

    [ObservableProperty]
    private bool isUpdateAvailableDialogOpen;

    [ObservableProperty]
    private string updateDialogVersionText = string.Empty;

    [ObservableProperty]
    private string updateDialogMessage = string.Empty;

    [ObservableProperty]
    private bool isCheckingUpdates;

    public string CheckUpdatesButtonText => IsCheckingUpdates
        ? Strings.Status_CheckingUpdates
        : Strings.Settings_CheckUpdatesButton;

    [RelayCommand]
    private void OpenGithubRepository()
    {
        try
        {
            if (!externalLinkService.TryOpen(GithubRepositoryUrl))
                statusService.Report(Strings.Status_OpenGithubRepositoryFailed);
        }
        catch (Exception)
        {
            statusService.Report(Strings.Status_OpenGithubRepositoryFailed);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCheckUpdates))]
    private async Task CheckUpdatesAsync()
    {
        if (IsCheckingUpdates)
            return;

        IsCheckingUpdates = true;
        statusService.Report(Strings.Status_CheckingUpdates);

        try
        {
            LauncherUpdateCheckResult result;
            try
            {
                result = await launcherUpdateService.CheckForUpdatesAsync(LauncherVersionText);
            }
            catch (Exception)
            {
                ReportVisibleStatus(Strings.Status_CheckUpdatesFailed);
                return;
            }

            if (result.IsFailed)
            {
                ReportVisibleStatus(Strings.Status_CheckUpdatesFailed);
                return;
            }

            if (!result.IsUpdateAvailable || result.Update is null)
            {
                ReportVisibleStatus(Strings.Status_LauncherAlreadyLatest);
                return;
            }

            var update = result.Update;
            UpdateDialogVersionText = update.DisplayVersion;
            UpdateDialogMessage = string.Format(Strings.Dialog_UpdateAvailableVersionFormat, update.DisplayVersion);
            updateDialogReleasePageUrl = update.ReleasePageUrl;
            updateDialogDownloadUrl = string.IsNullOrWhiteSpace(update.DownloadUrl)
                ? update.ReleasePageUrl
                : update.DownloadUrl;
            IsUpdateAvailableDialogOpen = true;
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    [RelayCommand]
    private void OpenUpdateChangelog()
    {
        if (!TryOpenUpdateUrl(updateDialogReleasePageUrl))
            ReportVisibleStatus(Strings.Status_OpenUpdatePageFailed);
    }

    [RelayCommand]
    private void CancelUpdateDialog()
    {
        IsUpdateAvailableDialogOpen = false;
    }

    [RelayCommand]
    private void ConfirmUpdate()
    {
        if (!TryOpenUpdateUrl(updateDialogDownloadUrl ?? updateDialogReleasePageUrl))
        {
            ReportVisibleStatus(Strings.Status_OpenUpdatePageFailed);
            return;
        }

        IsUpdateAvailableDialogOpen = false;
    }

    private bool CanCheckUpdates()
    {
        return !IsCheckingUpdates;
    }

    partial void OnIsCheckingUpdatesChanged(bool value)
    {
        OnPropertyChanged(nameof(CheckUpdatesButtonText));
        CheckUpdatesCommand.NotifyCanExecuteChanged();
    }

    private void ReportVisibleStatus(string message)
    {
        statusService.Report(message);
        floatingMessageService.Show(message);
    }

    private bool TryOpenUpdateUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        try
        {
            return externalLinkService.TryOpen(url);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string ResolveLauncherVersion()
    {
        var assembly = typeof(InfoSettingsViewModel).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
            return informationalVersion.Trim();

        var assemblyVersion = assembly.GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(assemblyVersion)
            ? Strings.Settings_LauncherVersionUnknown
            : assemblyVersion;
    }
}
