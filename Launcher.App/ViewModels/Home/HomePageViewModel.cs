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

namespace Launcher.App.ViewModels.Home;

public sealed partial class HomePageViewModel : ObservableObject
{
    private readonly ILaunchService launchService;
    private readonly AccountPageViewModel accountPage;
    private readonly IStatusService statusService;
    private readonly IFloatingMessageService floatingMessageService;
    private readonly IWindowService windowService;
    private readonly Action<double> reportProgressPercent;
    private readonly Func<GameInstance?, Task> openGameSettingsForInstance;
    private LauncherSettings settings = new();
    private CancellationTokenSource? launchCancellationTokenSource;

    [ObservableProperty]
    private bool isLaunching;

    [ObservableProperty]
    private string launchStatusMessage = string.Empty;

    [ObservableProperty]
    private double launchProgressPercent;

    [ObservableProperty]
    private string launchDownloadSpeedText = string.Empty;

    public HomePageViewModel(
        ILaunchService launchService,
        IGameVersionService gameVersionService,
        AccountPageViewModel accountPage,
        IStatusService statusService,
        IFloatingMessageService floatingMessageService,
        IWindowService windowService,
        Action<double> reportProgressPercent,
        Func<GameInstance, Task<bool>> selectLaunchInstance,
        Func<GameInstance?, Task> openGameSettingsForInstance)
    {
        this.launchService = launchService;
        this.accountPage = accountPage;
        this.statusService = statusService;
        this.floatingMessageService = floatingMessageService;
        this.windowService = windowService;
        this.reportProgressPercent = reportProgressPercent;
        this.openGameSettingsForInstance = openGameSettingsForInstance;

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

    public bool CanLaunchSelectedGame => HasSelectedAccount && SelectedInstance is not null && !IsLaunching;

    public bool HasLaunchProgress => IsLaunching && !string.IsNullOrWhiteSpace(LaunchStatusMessage);

    public bool HasLaunchDownloadSpeedText => IsLaunching && !string.IsNullOrWhiteSpace(LaunchDownloadSpeedText);

    public ObservableCollection<HomeLaunchInstanceItem> LaunchInstances => LaunchGames.LaunchInstances;

    public bool HasLaunchInstances => LaunchGames.HasLaunchInstances;

    public bool HasNoLaunchInstances => LaunchGames.HasNoLaunchInstances;

    public HomeLaunchInstanceItem? SelectedLaunchInstanceItem => LaunchGames.SelectedLaunchInstanceItem;

    public bool HasSelectedLaunchInstance => LaunchGames.HasSelectedLaunchInstance;

    public IAsyncRelayCommand SelectLaunchInstanceCommand => LaunchGames.SelectLaunchInstanceCommand;

    public bool CanOpenSelectedInstanceSettings => SelectedInstance is not null && !IsLaunching;

    public event EventHandler<JavaRequirementNotMetEventArgs>? JavaRequirementNotMet;

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
        var launchInstance = SelectedInstance;
        if (!CanLaunchSelectedGame || launchInstance is null || account is null)
        {
            statusService.Report(Strings.Status_NoLaunchableInstance);
            return;
        }

        try
        {
            var cancellationTokenSource = BeginLaunchProgress();
            await launchService.LaunchAsync(
                launchInstance,
                account,
                settings,
                CreateProgress(),
                cancellationTokenSource.Token);

            if (ShouldMinimizeLauncherAfterLaunch(launchInstance))
                windowService.Minimize();
        }
        catch (OperationCanceledException) when (launchCancellationTokenSource?.IsCancellationRequested == true)
        {
            statusService.Report(Strings.Status_LaunchCanceled);
            floatingMessageService.Show(Strings.Status_LaunchCanceled);
        }
        catch (LaunchAccountSessionException)
        {
            statusService.Report(Strings.Status_LaunchAccountUnavailable);
        }
        catch (LaunchProcessExitedException)
        {
            statusService.Report(Strings.Status_LaunchExitedQuickly);
        }
        catch (InstanceRepairException)
        {
            statusService.Report(Strings.Status_LaunchInstanceRepairFailed);
        }
        catch (JavaRuntimeSelectionException exception)
        {
            if (exception.Reason is JavaRuntimeSelectionFailureReason.AutomaticRuntimeNotFound)
                JavaRequirementNotMet?.Invoke(this, new JavaRequirementNotMetEventArgs(exception.RequiredMajorVersion));

            statusService.Report(Strings.Status_JavaSelectionFailed);
        }
        catch (Exception)
        {
            statusService.Report(Strings.Status_LaunchFailed);
        }
        finally
        {
            ResetLaunchProgress();
        }
    }

    [RelayCommand(CanExecute = nameof(IsLaunching))]
    private void CancelLaunch()
    {
        launchCancellationTokenSource?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanOpenSelectedInstanceSettings))]
    private Task OpenSelectedInstanceSettingsAsync()
    {
        return openGameSettingsForInstance(SelectedInstance);
    }

    private IProgress<LauncherProgress> CreateProgress()
    {
        return new Progress<LauncherProgress>(progress =>
        {
            if (!IsLaunching)
                return;

            var message = FormatLaunchProgress(progress);
            LaunchStatusMessage = message;
            if (progress.DownloadSpeedText is not null)
                LaunchDownloadSpeedText = progress.DownloadSpeedText;

            if (progress.Percent is double percent)
                LaunchProgressPercent = Math.Clamp(percent, 0, 100);

            statusService.Report(message);
            reportProgressPercent(LaunchProgressPercent);
        });
    }

    partial void OnIsLaunchingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanLaunchSelectedGame));
        OnPropertyChanged(nameof(HasLaunchProgress));
        OnPropertyChanged(nameof(HasLaunchDownloadSpeedText));
        OnPropertyChanged(nameof(CanOpenSelectedInstanceSettings));
        LaunchCommand.NotifyCanExecuteChanged();
        CancelLaunchCommand.NotifyCanExecuteChanged();
        OpenSelectedInstanceSettingsCommand.NotifyCanExecuteChanged();
    }

    partial void OnLaunchStatusMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasLaunchProgress));
    }

    partial void OnLaunchDownloadSpeedTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasLaunchDownloadSpeedText));
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
        OnPropertyChanged(nameof(CanOpenSelectedInstanceSettings));
        LaunchCommand.NotifyCanExecuteChanged();
        OpenSelectedInstanceSettingsCommand.NotifyCanExecuteChanged();
    }

    private CancellationTokenSource BeginLaunchProgress()
    {
        launchCancellationTokenSource?.Dispose();
        launchCancellationTokenSource = new CancellationTokenSource();
        IsLaunching = true;
        LaunchProgressPercent = 0;
        LaunchDownloadSpeedText = string.Empty;
        LaunchStatusMessage = Strings.Status_LaunchPreparing;
        statusService.Report(LaunchStatusMessage);
        reportProgressPercent(LaunchProgressPercent);
        return launchCancellationTokenSource;
    }

    private void ResetLaunchProgress()
    {
        launchCancellationTokenSource?.Dispose();
        launchCancellationTokenSource = null;
        LaunchProgressPercent = 0;
        reportProgressPercent(0);
        LaunchDownloadSpeedText = string.Empty;
        LaunchStatusMessage = string.Empty;
        IsLaunching = false;
    }

    private bool ShouldMinimizeLauncherAfterLaunch(GameInstance instance)
    {
        return instance.LaunchSettingsMode is LaunchSettingsMode.UseGlobal
            ? settings.DefaultMinimizeLauncherAfterLaunch
            : instance.MinimizeLauncherAfterLaunch;
    }

    private static string FormatLaunchProgress(LauncherProgress progress)
    {
        return progress.Stage switch
        {
            LaunchProgressStages.CheckingInstance => Strings.Status_LaunchCheckingInstance,
            LaunchProgressStages.RepairingMetadata => Strings.Status_LaunchRepairingMetadata,
            LaunchProgressStages.RepairingJar => Strings.Status_LaunchRepairingJar,
            LaunchProgressStages.RepairingLibraries => Strings.Status_LaunchRepairingLibraries,
            LaunchProgressStages.RepairingAssets => Strings.Status_LaunchRepairingAssets,
            LaunchProgressStages.RepairingLogging => Strings.Status_LaunchRepairingLogging,
            LaunchProgressStages.CheckingJava => Strings.Status_LaunchCheckingJava,
            LaunchProgressStages.RunningPreLaunchCommand => Strings.Status_LaunchRunningPreLaunchCommand,
            LaunchProgressStages.PreparingProcess => Strings.Status_LaunchPreparingProcess,
            LaunchProgressStages.StartingProcess => Strings.Status_LaunchStartingProcess,
            LaunchProgressStages.CheckingFiles => Strings.Status_LaunchCheckingFiles,
            LaunchProgressStages.DownloadingFiles or LaunchProgressStages.DownloadSpeed => Strings.Status_LaunchDownloadingFiles,
            _ when !string.IsNullOrWhiteSpace(progress.Message) => progress.Message,
            _ => Strings.Status_LaunchPreparing
        };
    }
}

public sealed class JavaRequirementNotMetEventArgs : EventArgs
{
    public JavaRequirementNotMetEventArgs(int? requiredMajorVersion)
    {
        RequiredMajorVersion = requiredMajorVersion;
    }

    public int? RequiredMajorVersion { get; }
}

