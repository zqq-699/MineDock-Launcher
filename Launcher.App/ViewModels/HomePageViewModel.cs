using Launcher.Application.Accounts;
using Launcher.App.Resources;
using Launcher.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Domain.Models;
using Launcher.Application.Services;

namespace Launcher.App.ViewModels;

public sealed partial class HomePageViewModel : ObservableObject
{
    private readonly ILaunchService launchService;
    private readonly AccountPageViewModel accountPage;
    private readonly IStatusService statusService;
    private readonly Action<string> navigateToPage;
    private readonly Action<double> reportProgressPercent;
    private LauncherSettings settings = new();

    [ObservableProperty]
    private GameInstance? selectedInstance;

    public HomePageViewModel(
        ILaunchService launchService,
        AccountPageViewModel accountPage,
        IStatusService statusService,
        Action<string> navigateToPage,
        Action<double> reportProgressPercent)
    {
        this.launchService = launchService;
        this.accountPage = accountPage;
        this.statusService = statusService;
        this.navigateToPage = navigateToPage;
        this.reportProgressPercent = reportProgressPercent;

        accountPage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AccountPageViewModel.SelectedAccount))
                NotifyAccountStateChanged();
        };
    }

    public bool HasSelectedAccount => accountPage.SelectedAccount is not null;

    public bool CanLaunchSelectedGame => HasSelectedAccount && SelectedInstance is not null;

    public string? HomeAvatarUrl
    {
        get
        {
            var account = accountPage.SelectedAccount;
            if (account is null)
                return null;

            if (!string.IsNullOrWhiteSpace(account.AvatarSource))
                return account.AvatarSource;

            if (account.IsOffline || string.IsNullOrWhiteSpace(account.Uuid))
                return "https://minotar.net/avatar/Steve/576.png";

            return $"https://crafatar.com/avatars/{account.Uuid}?size=576&overlay";
        }
    }

    public string HomeAccountDisplayName => accountPage.SelectedAccount?.DisplayName ?? Strings.Home_NoAccountSelected;

    public string HomeVersionDisplayName
    {
        get
        {
            if (SelectedInstance is null)
                return Strings.Home_NoVersionSelected;

            if (!string.IsNullOrWhiteSpace(SelectedInstance.Name))
                return SelectedInstance.Name;

            if (!string.IsNullOrWhiteSpace(SelectedInstance.VersionName))
                return SelectedInstance.VersionName;

            return string.IsNullOrWhiteSpace(SelectedInstance.MinecraftVersion)
                ? Strings.Home_NoVersionSelected
                : SelectedInstance.MinecraftVersion;
        }
    }

    public void Initialize(LauncherSettings launcherSettings, GameInstance? instance)
    {
        settings = launcherSettings;
        SelectedInstance = instance;
        NotifyAccountStateChanged();
        NotifyInstanceStateChanged();
    }

    public void SetSettings(LauncherSettings launcherSettings)
    {
        settings = launcherSettings;
    }

    public void SetSelectedInstance(GameInstance? instance)
    {
        SelectedInstance = instance;
    }

    [RelayCommand(CanExecute = nameof(CanLaunchSelectedGame))]
    private async Task LaunchAsync()
    {
        if (!CanLaunchSelectedGame || SelectedInstance is null)
        {
            statusService.Report(Strings.Status_NoLaunchableInstance);
            return;
        }

        await launchService.LaunchAsync(SelectedInstance, settings, CreateProgress());
    }

    [RelayCommand]
    private void ChangeHomeVersion()
    {
        navigateToPage("GameSettings");
    }

    partial void OnSelectedInstanceChanged(GameInstance? value)
    {
        NotifyInstanceStateChanged();
    }

    private IProgress<LauncherProgress> CreateProgress()
    {
        return new Progress<LauncherProgress>(progress =>
        {
            statusService.Report(progress.Message);
            reportProgressPercent(progress.Percent ?? 0);
        });
    }

    private void NotifyAccountStateChanged()
    {
        OnPropertyChanged(nameof(HasSelectedAccount));
        OnPropertyChanged(nameof(HomeAvatarUrl));
        OnPropertyChanged(nameof(HomeAccountDisplayName));
        OnPropertyChanged(nameof(CanLaunchSelectedGame));
        LaunchCommand.NotifyCanExecuteChanged();
    }

    private void NotifyInstanceStateChanged()
    {
        OnPropertyChanged(nameof(HomeVersionDisplayName));
        OnPropertyChanged(nameof(CanLaunchSelectedGame));
        LaunchCommand.NotifyCanExecuteChanged();
    }
}
