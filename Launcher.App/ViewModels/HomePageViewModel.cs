using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Models;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Accounts;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels;

public sealed partial class HomePageViewModel : ObservableObject
{
    private readonly ILaunchService launchService;
    private readonly AccountPageViewModel accountPage;
    private readonly IStatusService statusService;
    private readonly Action<string> navigateToPage;
    private readonly Action<double> reportProgressPercent;
    private LauncherSettings settings = new();

    public HomePageViewModel(
        ILaunchService launchService,
        IGameVersionService gameVersionService,
        AccountPageViewModel accountPage,
        IStatusService statusService,
        Action<string> navigateToPage,
        Action<double> reportProgressPercent,
        Func<GameInstance, Task<bool>> selectLaunchInstance)
    {
        this.launchService = launchService;
        this.accountPage = accountPage;
        this.statusService = statusService;
        this.navigateToPage = navigateToPage;
        this.reportProgressPercent = reportProgressPercent;

        LaunchGames = new HomeLaunchGameListViewModel(gameVersionService, statusService, selectLaunchInstance);
        LaunchGames.PropertyChanged += LaunchGames_PropertyChanged;

        accountPage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AccountPageViewModel.SelectedAccount))
                NotifyAccountStateChanged();
        };
    }

    public HomeLaunchGameListViewModel LaunchGames { get; }

    public bool HasSelectedAccount => accountPage.SelectedAccount is not null;

    public GameInstance? SelectedInstance => LaunchGames.SelectedInstance;

    public bool CanLaunchSelectedGame => HasSelectedAccount && SelectedInstance is not null;

    public ObservableCollection<HomeLaunchInstanceItem> LaunchInstances => LaunchGames.LaunchInstances;

    public bool HasLaunchInstances => LaunchGames.HasLaunchInstances;

    public bool HasNoLaunchInstances => LaunchGames.HasNoLaunchInstances;

    public HomeLaunchInstanceItem? SelectedLaunchInstanceItem => LaunchGames.SelectedLaunchInstanceItem;

    public bool HasSelectedLaunchInstance => LaunchGames.HasSelectedLaunchInstance;

    public IAsyncRelayCommand SelectLaunchInstanceCommand => LaunchGames.SelectLaunchInstanceCommand;

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
        SetSelectedInstance(instance);
        NotifyAccountStateChanged();
        NotifyInstanceStateChanged();
    }

    public void SetSettings(LauncherSettings launcherSettings)
    {
        settings = launcherSettings;
    }

    public void SetSelectedInstance(GameInstance? instance)
    {
        LaunchGames.SetSelectedInstance(instance);
    }

    public void SetLaunchInstances(IEnumerable<GameInstance> instances)
    {
        LaunchGames.SetLaunchInstances(instances);
    }

    public Task EnsureVersionTypesLoadedAsync(CancellationToken cancellationToken = default)
    {
        return LaunchGames.EnsureVersionTypesLoadedAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanLaunchSelectedGame))]
    private async Task LaunchAsync()
    {
        var account = accountPage.SelectedAccount;
        if (!CanLaunchSelectedGame || SelectedInstance is null || account is null)
        {
            statusService.Report(Strings.Status_NoLaunchableInstance);
            return;
        }

        try
        {
            await launchService.LaunchAsync(SelectedInstance, account, settings, CreateProgress());
        }
        catch (LaunchAccountSessionException)
        {
            statusService.Report(Strings.Status_LaunchAccountUnavailable);
        }
        catch (Exception)
        {
            statusService.Report(Strings.Status_LaunchFailed);
        }
    }

    [RelayCommand]
    private void ChangeHomeVersion()
    {
        navigateToPage(NavigationCatalog.GameSettingsPage);
    }

    private IProgress<LauncherProgress> CreateProgress()
    {
        return new Progress<LauncherProgress>(progress =>
        {
            statusService.Report(progress.Message);
            reportProgressPercent(progress.Percent ?? 0);
        });
    }

    private void LaunchGames_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HomeLaunchGameListViewModel.SelectedInstance):
                OnPropertyChanged(nameof(SelectedInstance));
                NotifyInstanceStateChanged();
                break;
            case nameof(HomeLaunchGameListViewModel.HasLaunchInstances):
                OnPropertyChanged(nameof(HasLaunchInstances));
                break;
            case nameof(HomeLaunchGameListViewModel.HasNoLaunchInstances):
                OnPropertyChanged(nameof(HasNoLaunchInstances));
                break;
            case nameof(HomeLaunchGameListViewModel.SelectedLaunchInstanceItem):
                OnPropertyChanged(nameof(SelectedLaunchInstanceItem));
                break;
            case nameof(HomeLaunchGameListViewModel.HasSelectedLaunchInstance):
                OnPropertyChanged(nameof(HasSelectedLaunchInstance));
                break;
        }
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
