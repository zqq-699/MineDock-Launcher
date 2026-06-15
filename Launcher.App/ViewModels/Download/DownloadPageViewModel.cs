using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Domain.Models;
using Launcher.Application.Services;

namespace Launcher.App.ViewModels.Download;

public sealed partial class DownloadPageViewModel : ObservableObject
{
    private readonly IGameVersionService gameVersionService;
    private readonly IGameInstanceService instanceService;
    private readonly DownloadTasksPageViewModel downloadTasksPage;
    private readonly IUiDispatcher uiDispatcher;
    private readonly DownloadInstanceNameTracker instanceNameTracker = new();
    private bool hasLoadedVersions;
    private int refreshRequestVersion;
    private int activeInstallCount;
    private long latestInstallSequence;

    [ObservableProperty]
    private DownloadPageStep currentStep = DownloadPageStep.VersionList;

    [ObservableProperty]
    private DownloadVersionCategory? selectedVersionCategory;

    [ObservableProperty]
    private DownloadMinecraftVersionItem? selectedMinecraftVersion;

    [ObservableProperty]
    private bool isLoadingVersions;

    [ObservableProperty]
    private string versionLoadError = string.Empty;

    [ObservableProperty]
    private string versionEmptyMessage = string.Empty;

    [ObservableProperty]
    private string versionSearchQuery = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<DownloadMinecraftVersionItem> visibleVersions = Array.Empty<DownloadMinecraftVersionItem>();

    [ObservableProperty]
    private string instanceName = string.Empty;

    [ObservableProperty]
    private DownloadLoaderOption? selectedLoaderOption;

    [ObservableProperty]
    private bool isInstalling;

    [ObservableProperty]
    private string installStatusMessage = string.Empty;

    [ObservableProperty]
    private string installError = string.Empty;

    [ObservableProperty]
    private double installProgressPercent;

    [ObservableProperty]
    private string instanceNameDuplicateMessage = string.Empty;

    [ObservableProperty]
    private int contentRefreshToken;

    [ObservableProperty]
    private int listEntranceAnimationToken;

    public DownloadPageViewModel(
        IGameVersionService gameVersionService,
        IGameInstanceService instanceService,
        DownloadTasksPageViewModel downloadTasksPage)
        : this(gameVersionService, instanceService, downloadTasksPage, ImmediateUiDispatcher.Instance)
    {
    }

    public DownloadPageViewModel(
        IGameVersionService gameVersionService,
        IGameInstanceService instanceService,
        DownloadTasksPageViewModel downloadTasksPage,
        IUiDispatcher uiDispatcher)
    {
        this.gameVersionService = gameVersionService;
        this.instanceService = instanceService;
        this.downloadTasksPage = downloadTasksPage;
        this.uiDispatcher = uiDispatcher;

        VersionCategories.Add(new DownloadVersionCategory("release", Strings.Download_ReleaseCategory, string.Empty, "instance_download_page/release"));
        VersionCategories.Add(new DownloadVersionCategory("snapshot", Strings.Download_SnapshotCategory, string.Empty, "instance_download_page/snapshot"));
        VersionCategories.Add(new DownloadVersionCategory("old_beta", Strings.Download_BetaCategory, "\u03b2"));
        VersionCategories.Add(new DownloadVersionCategory("old_alpha", Strings.Download_AlphaCategory, "\u03b1"));

        LoaderOptions.Add(new DownloadLoaderOption(LoaderKind.Vanilla, Strings.Download_VanillaLoaderTitle, Strings.Download_VanillaLoaderSubtitle, string.Empty, "/Assets/Icons/block/grass_block.png"));
        LoaderOptions.Add(new DownloadLoaderOption(LoaderKind.Fabric, Strings.Download_FabricLoaderTitle, Strings.Download_LoaderPendingSubtitle, "\uE8B7"));
        LoaderOptions.Add(new DownloadLoaderOption(LoaderKind.Forge, Strings.Download_ForgeLoaderTitle, Strings.Download_LoaderPendingSubtitle, "\uE8B7"));
        SelectLoaderOptionCore(LoaderOptions.First());

        SelectVersionCategoryCore(VersionCategories.First(), deferRefresh: false);
    }

    public event EventHandler<GameInstance>? InstanceInstalled;

    public ObservableCollection<DownloadVersionCategory> VersionCategories { get; } = [];

    public ObservableCollection<DownloadLoaderOption> LoaderOptions { get; } = [];

    public List<DownloadMinecraftVersionItem> AllVersions { get; } = [];

    public bool HasVisibleVersions => VisibleVersions.Count > 0;

    public bool HasSelectedMinecraftVersion => SelectedMinecraftVersion is not null;

    public bool HasVersionLoadError => !string.IsNullOrWhiteSpace(VersionLoadError);

    public bool HasVersionEmptyMessage => !string.IsNullOrWhiteSpace(VersionEmptyMessage);

    public bool IsVersionListStep => CurrentStep is DownloadPageStep.VersionList;

    public bool IsInstanceOptionsStep => CurrentStep is DownloadPageStep.InstanceOptions;

    public bool IsDownloadContentVisible => IsInstanceOptionsStep || HasVisibleVersions;

    public bool HasInstallStatus => !string.IsNullOrWhiteSpace(InstallStatusMessage);

    public bool HasInstallError => !string.IsNullOrWhiteSpace(InstallError);

    public bool HasInstanceNameDuplicateMessage => !string.IsNullOrWhiteSpace(InstanceNameDuplicateMessage);

    public bool CanInstallSelectedVersion => CanInstall();

    public string InstallButtonText => Strings.Download_InstallButton;

    public string PageTitle => CurrentStep is DownloadPageStep.InstanceOptions
        ? SelectedMinecraftVersion?.Name ?? string.Empty
        : SelectedVersionCategory?.Title ?? string.Empty;

    public string? PageTitleIconSource => CurrentStep is DownloadPageStep.InstanceOptions
        ? SelectedMinecraftVersion?.IconSource
        : null;

    public async Task EnsureVersionsLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (hasLoadedVersions)
        {
            return;
        }

        if (IsLoadingVersions)
            return;

        IsLoadingVersions = true;
        VersionLoadError = string.Empty;
        VersionEmptyMessage = string.Empty;

        try
        {
            var versions = await gameVersionService.GetVersionsAsync(cancellationToken);
            AllVersions.Clear();
            AllVersions.AddRange(versions.Select(version => new DownloadMinecraftVersionItem(version)));
            await RefreshExistingInstanceNamesAsync(cancellationToken);

            hasLoadedVersions = true;
        }
        catch (Exception)
        {
            VersionLoadError = Strings.Status_LoadVersionsFailed;
        }
        finally
        {
            IsLoadingVersions = false;
            RefreshVisibleVersions();
            if (hasLoadedVersions)
                ListEntranceAnimationToken++;
        }
    }

    [RelayCommand]
    private void SelectVersionCategory(DownloadVersionCategory category)
    {
        var isRefreshingCurrentCategory = ReferenceEquals(SelectedVersionCategory, category);
        SelectVersionCategoryCore(category, deferRefresh: false);

        if (isRefreshingCurrentCategory)
            RefreshCurrentCategoryContent();
        else if (hasLoadedVersions)
            ListEntranceAnimationToken++;
    }

    private void SelectVersionCategoryCore(DownloadVersionCategory category, bool deferRefresh)
    {
        SelectedVersionCategory = category;
        foreach (var item in VersionCategories)
            item.IsSelected = ReferenceEquals(item, category);

        RequestVisibleVersionsRefresh(deferRefresh);
    }

    [RelayCommand]
    private async Task SelectMinecraftVersionAsync(DownloadMinecraftVersionItem version)
    {
        SelectMinecraftVersionCore(version);
        await GoToInstanceOptions();
    }

    private void SelectMinecraftVersionCore(DownloadMinecraftVersionItem version)
    {
        SelectedMinecraftVersion = version;
        foreach (var item in AllVersions)
            item.IsSelected = ReferenceEquals(item, version);
    }

    [RelayCommand(CanExecute = nameof(CanGoToInstanceOptions))]
    private async Task GoToInstanceOptions()
    {
        if (SelectedMinecraftVersion is null)
            return;

        InstanceName = SelectedMinecraftVersion.Name;
        SelectLoaderOptionCore(LoaderOptions.First(option => option.Kind is LoaderKind.Vanilla));
        await RefreshExistingInstanceNamesAsync(CancellationToken.None);
        RefreshInstanceNameDuplicateMessage();
        CurrentStep = DownloadPageStep.InstanceOptions;
    }

    private bool CanGoToInstanceOptions()
    {
        return SelectedMinecraftVersion is not null;
    }

    [RelayCommand]
    private void BackToVersionList()
    {
        CurrentStep = DownloadPageStep.VersionList;
        ClearSelectedVersion();
    }

    [RelayCommand]
    private void SelectLoaderOption(DownloadLoaderOption loaderOption)
    {
        SelectLoaderOptionCore(loaderOption);
    }

    private void SelectLoaderOptionCore(DownloadLoaderOption loaderOption)
    {
        SelectedLoaderOption = loaderOption;
    }

    private void RefreshCurrentCategoryContent()
    {
        CurrentStep = DownloadPageStep.VersionList;
        RequestVisibleVersionsRefresh(defer: false);
        ContentRefreshToken++;
        GoToInstanceOptionsCommand.NotifyCanExecuteChanged();
        NotifyInstallStateChanged();
    }

    [RelayCommand(CanExecute = nameof(CanInstall), AllowConcurrentExecutions = true)]
    private async Task InstallAsync()
    {
        if (!CanInstall() || SelectedMinecraftVersion is null)
            return;

        var versionName = SelectedMinecraftVersion.Name;
        var instanceName = string.IsNullOrWhiteSpace(InstanceName) ? versionName : InstanceName.Trim();
        var installSequence = Interlocked.Increment(ref latestInstallSequence);
        var installTask = downloadTasksPage.BeginTask($"{Strings.Download_VanillaLoaderTitle} {versionName}", instanceName);

        var activeInstallCountAfterStart = Interlocked.Increment(ref this.activeInstallCount);
        IsInstalling = activeInstallCountAfterStart > 0;
        InstallError = string.Empty;
        InstallProgressPercent = 0;
        InstallStatusMessage = string.Format(Strings.Status_InstallingVanillaFormat, versionName);
        installTask.Report(new LauncherProgress("Install", InstallStatusMessage, 0));
        instanceNameTracker.AddPending(instanceName);
        RefreshInstanceNameDuplicateMessage();
        CurrentStep = DownloadPageStep.VersionList;
        NotifyInstallStateChanged();
        GoToInstanceOptionsCommand.NotifyCanExecuteChanged();
        RequestVisibleVersionsRefresh(defer: true);

        try
        {
            using var installProgress = CreateProgress(installTask, installSequence);
            var instance = await instanceService.CreateInstanceAsync(
                versionName,
                LoaderKind.Vanilla,
                null,
                instanceName,
                installProgress,
                installTask.CancellationToken);

            instanceNameTracker.RemovePending(instanceName);
            SetLatestInstallCompletion(installSequence, string.Format(Strings.Status_InstanceInstalledFormat, instance.Name));
            instanceNameTracker.AddExisting(instance.Name);
            instanceNameTracker.AddExisting(instance.VersionName);
            installTask.Complete(string.Format(Strings.Status_InstanceInstalledFormat, instance.Name));
            InstanceInstalled?.Invoke(this, instance);
        }
        catch (OperationCanceledException) when (installTask.IsCancellationRequested)
        {
            instanceNameTracker.RemovePending(instanceName);
            if (installSequence == latestInstallSequence)
            {
                InstallError = string.Empty;
                InstallStatusMessage = string.Empty;
                InstallProgressPercent = 0;
            }

            downloadTasksPage.CancelTask(installTask);
        }
        catch (Exception)
        {
            instanceNameTracker.RemovePending(instanceName);
            SetLatestInstallFailure(installSequence, Strings.Status_InstallFailed);
            installTask.Fail(Strings.Status_InstallFailed);
            throw;
        }
        finally
        {
            var activeInstallCountAfterFinish = Interlocked.Decrement(ref this.activeInstallCount);
            if (activeInstallCountAfterFinish < 0)
            {
                Interlocked.Exchange(ref this.activeInstallCount, 0);
                activeInstallCountAfterFinish = 0;
            }

            IsInstalling = activeInstallCountAfterFinish > 0;
            RefreshInstanceNameDuplicateMessage();
        }
    }

    private bool CanInstall()
    {
        return CurrentStep is DownloadPageStep.InstanceOptions
            && SelectedMinecraftVersion is not null
            && SelectedLoaderOption?.Kind is LoaderKind.Vanilla
            && !string.IsNullOrWhiteSpace(InstanceName)
            && !HasInstanceNameDuplicateMessage;
    }

    partial void OnSelectedLoaderOptionChanged(DownloadLoaderOption? value)
    {
        foreach (var item in LoaderOptions)
            item.IsSelected = ReferenceEquals(item, value);

        if (value?.Kind is not null and not LoaderKind.Vanilla)
        {
            InstallError = string.Empty;
            InstallStatusMessage = Strings.Status_LoaderInstallPending;
        }

        NotifyInstallStateChanged();
    }

    partial void OnCurrentStepChanged(DownloadPageStep value)
    {
        OnPropertyChanged(nameof(IsVersionListStep));
        OnPropertyChanged(nameof(IsInstanceOptionsStep));
        OnPropertyChanged(nameof(IsDownloadContentVisible));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageTitleIconSource));
        NotifyInstallStateChanged();
    }

    partial void OnSelectedVersionCategoryChanged(DownloadVersionCategory? value)
    {
        OnPropertyChanged(nameof(PageTitle));
    }

    partial void OnVersionLoadErrorChanged(string value)
    {
        OnPropertyChanged(nameof(HasVersionLoadError));
    }

    partial void OnVersionEmptyMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasVersionEmptyMessage));
    }

    partial void OnVersionSearchQueryChanged(string value)
    {
        RequestVisibleVersionsRefresh(defer: false);
    }

    partial void OnSelectedMinecraftVersionChanged(DownloadMinecraftVersionItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedMinecraftVersion));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageTitleIconSource));
        GoToInstanceOptionsCommand.NotifyCanExecuteChanged();
        NotifyInstallStateChanged();

        if (value is null && CurrentStep is DownloadPageStep.InstanceOptions)
            CurrentStep = DownloadPageStep.VersionList;
    }

    partial void OnVisibleVersionsChanged(IReadOnlyList<DownloadMinecraftVersionItem> value)
    {
        OnPropertyChanged(nameof(HasVisibleVersions));
        OnPropertyChanged(nameof(HasVersionEmptyMessage));
        OnPropertyChanged(nameof(IsDownloadContentVisible));
    }

    partial void OnInstanceNameChanged(string value)
    {
        RefreshInstanceNameDuplicateMessage();
        NotifyInstallStateChanged();
    }

    partial void OnIsInstallingChanged(bool value)
    {
        NotifyInstallStateChanged();
    }

    partial void OnInstallStatusMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasInstallStatus));
    }

    partial void OnInstallErrorChanged(string value)
    {
        OnPropertyChanged(nameof(HasInstallError));
    }

    partial void OnInstanceNameDuplicateMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasInstanceNameDuplicateMessage));
        NotifyInstallStateChanged();
    }

    private void RequestVisibleVersionsRefresh(bool defer)
    {
        var requestVersion = ++refreshRequestVersion;
        if (defer && uiDispatcher.HasAccess)
        {
            uiDispatcher.Post(
                () =>
                {
                    if (requestVersion == refreshRequestVersion)
                        RefreshVisibleVersions();
                });
            return;
        }

        RefreshVisibleVersions();
    }

    private void RefreshVisibleVersions()
    {
        var result = DownloadVersionFilter.Apply(
            AllVersions,
            SelectedVersionCategory,
            VersionSearchQuery,
            SelectedMinecraftVersion,
            hasLoadedVersions,
            IsLoadingVersions,
            HasVersionLoadError);

        VersionEmptyMessage = result.EmptyMessage;
        if (result.ShouldClearSelectedVersion)
            ClearSelectedVersion();

        SetFilteredVersions(result.Versions);
    }

    private void SetFilteredVersions(IReadOnlyList<DownloadMinecraftVersionItem> versions)
    {
        VisibleVersions = versions;
    }

    private async Task RefreshExistingInstanceNamesAsync(CancellationToken cancellationToken)
    {
        instanceNameTracker.ReplaceExisting(await instanceService.GetInstancesAsync(cancellationToken));
        RefreshInstanceNameDuplicateMessage();
    }

    private void RefreshInstanceNameDuplicateMessage()
    {
        InstanceNameDuplicateMessage = !string.IsNullOrWhiteSpace(InstanceName?.Trim())
            && instanceNameTracker.IsUnavailable(InstanceName)
                ? Strings.Status_DuplicateInstanceName
                : string.Empty;
    }

    private DownloadInstallProgress CreateProgress(DownloadTaskItem installTask, long installSequence)
    {
        return new DownloadInstallProgress(installTask, installSequence, ReportInstallProgress, uiDispatcher);
    }

    private void NotifyInstallStateChanged()
    {
        OnPropertyChanged(nameof(CanInstallSelectedVersion));
        InstallCommand.NotifyCanExecuteChanged();
    }

    private void ReportInstallProgress(DownloadTaskItem installTask, LauncherProgress progress, long installSequence)
    {
        if (installSequence == latestInstallSequence)
        {
            InstallError = string.Empty;
            InstallStatusMessage = progress.Message;
            if (progress.Percent is { } percent)
                InstallProgressPercent = percent;
        }

        installTask.Report(progress);
    }

    private void SetLatestInstallCompletion(long installSequence, string message)
    {
        if (installSequence != latestInstallSequence)
            return;

        InstallError = string.Empty;
        InstallProgressPercent = 100;
        InstallStatusMessage = message;
    }

    private void SetLatestInstallFailure(long installSequence, string message)
    {
        if (installSequence != latestInstallSequence)
            return;

        InstallError = message;
        InstallStatusMessage = string.Empty;
    }


    private void ClearSelectedVersion()
    {
        SelectedMinecraftVersion = null;
        foreach (var item in AllVersions)
            item.IsSelected = false;
    }

}

