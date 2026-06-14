using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Domain.Models;
using Launcher.Application.Services;

namespace Launcher.App.ViewModels;

public sealed partial class DownloadPageViewModel : ObservableObject
{
    private readonly IGameVersionService gameVersionService;
    private readonly IGameInstanceService instanceService;
    private readonly DownloadTasksPageViewModel downloadTasksPage;
    private readonly object instanceNameStateSync = new();
    private readonly HashSet<string> existingInstanceNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> pendingInstanceNames = new(StringComparer.OrdinalIgnoreCase);
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
    {
        this.gameVersionService = gameVersionService;
        this.instanceService = instanceService;
        this.downloadTasksPage = downloadTasksPage;

        VersionCategories.Add(new DownloadVersionCategory("release", "\u6b63\u5f0f\u7248", string.Empty, "instance_download_page/release"));
        VersionCategories.Add(new DownloadVersionCategory("snapshot", "\u5feb\u7167\u7248", string.Empty, "instance_download_page/snapshot"));
        VersionCategories.Add(new DownloadVersionCategory("old_beta", "beta", "\u03b2"));
        VersionCategories.Add(new DownloadVersionCategory("old_alpha", "alpha", "\u03b1"));

        LoaderOptions.Add(new DownloadLoaderOption(LoaderKind.Vanilla, "\u539f\u7248", "\u4e0d\u5b89\u88c5 Mod \u52a0\u8f7d\u5668", string.Empty, "/Assets/Icons/block/grass_block.png"));
        LoaderOptions.Add(new DownloadLoaderOption(LoaderKind.Fabric, "Fabric", "\u6682\u672a\u63a5\u5165\u5b89\u88c5", "\uE8B7"));
        LoaderOptions.Add(new DownloadLoaderOption(LoaderKind.Forge, "Forge", "\u6682\u672a\u63a5\u5165\u5b89\u88c5", "\uE8B7"));
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

    public string InstallButtonText => "\u5b89\u88c5";

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
        catch (Exception ex)
        {
            VersionLoadError = $"\u7248\u672c\u5217\u8868\u52a0\u8f7d\u5931\u8d25\uff1a{ex.Message}";
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
    private void SelectMinecraftVersion(DownloadMinecraftVersionItem version)
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
        var installTask = downloadTasksPage.BeginTask($"\u539f\u7248 {versionName}", instanceName);

        var activeInstallCountAfterStart = Interlocked.Increment(ref this.activeInstallCount);
        IsInstalling = activeInstallCountAfterStart > 0;
        InstallError = string.Empty;
        InstallProgressPercent = 0;
        InstallStatusMessage = $"\u6b63\u5728\u5b89\u88c5\u539f\u7248 {versionName}...";
        installTask.Report(new LauncherProgress("Install", InstallStatusMessage, 0));
        AddPendingInstanceName(instanceName);
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
                installProgress);

            RemovePendingInstanceName(instanceName);
            SetLatestInstallCompletion(installSequence, $"\u5b9e\u4f8b {instance.Name} \u5df2\u5b89\u88c5");
            AddExistingInstanceName(instance.Name);
            AddExistingInstanceName(instance.VersionName);
            installTask.Complete($"\u5b9e\u4f8b {instance.Name} \u5df2\u5b89\u88c5");
            InstanceInstalled?.Invoke(this, instance);
        }
        catch (Exception ex)
        {
            RemovePendingInstanceName(instanceName);
            SetLatestInstallFailure(installSequence, $"\u5b89\u88c5\u5931\u8d25\uff1a{ex.Message}");
            installTask.Fail($"\u5b89\u88c5\u5931\u8d25\uff1a{ex.Message}");
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
            InstallStatusMessage = "\u8be5\u52a0\u8f7d\u5668\u5c06\u5728\u540e\u7eed\u7248\u672c\u63a5\u5165\u5b89\u88c5\u3002";
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
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (defer && dispatcher is not null && dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(
                () =>
                {
                    if (requestVersion == refreshRequestVersion)
                        RefreshVisibleVersions();
                },
                DispatcherPriority.Background);
            return;
        }

        RefreshVisibleVersions();
    }

    private void RefreshVisibleVersions()
    {
        VersionEmptyMessage = string.Empty;
        IReadOnlyList<DownloadMinecraftVersionItem> nextVersions = Array.Empty<DownloadMinecraftVersionItem>();

        if (HasVersionLoadError)
        {
            SetFilteredVersions(nextVersions);
            return;
        }

        if (SelectedVersionCategory?.Id is not ("release" or "snapshot"))
        {
            VersionEmptyMessage = "\u8be5\u5206\u7c7b\u7a0d\u540e\u5b9e\u73b0\u3002";
            ClearSelectedVersion();
            SetFilteredVersions(nextVersions);
            return;
        }

        var query = VersionSearchQuery.Trim();
        var versions = SelectedVersionCategory?.Id switch
        {
            "snapshot" => AllVersions.Where(version => version.IsSnapshot),
            _ => AllVersions.Where(version => version.IsRelease)
        };
        if (!string.IsNullOrWhiteSpace(query))
            versions = versions.Where(version => version.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

        nextVersions = SortVersionsForCategory(versions, SelectedVersionCategory?.Id).ToList();

        if (nextVersions.Count == 0 && hasLoadedVersions && !IsLoadingVersions)
        {
            var categoryTitle = SelectedVersionCategory?.Title ?? "\u7248\u672c";
            VersionEmptyMessage = string.IsNullOrWhiteSpace(query)
                ? $"\u6ca1\u6709\u627e\u5230{categoryTitle}\u7248\u672c\u3002"
                : "\u6ca1\u6709\u627e\u5230\u5339\u914d\u7684\u7248\u672c\u3002";
        }

        if (SelectedMinecraftVersion is not null && !nextVersions.Contains(SelectedMinecraftVersion))
            ClearSelectedVersion();

        SetFilteredVersions(nextVersions);
    }

    private void SetFilteredVersions(IReadOnlyList<DownloadMinecraftVersionItem> versions)
    {
        VisibleVersions = versions;
    }

    private async Task RefreshExistingInstanceNamesAsync(CancellationToken cancellationToken)
    {
        lock (instanceNameStateSync)
        {
            existingInstanceNames.Clear();
        }

        foreach (var instance in await instanceService.GetInstancesAsync(cancellationToken))
        {
            AddExistingInstanceName(instance.Name);
            AddExistingInstanceName(instance.VersionName);
        }

        RefreshInstanceNameDuplicateMessage();
    }

    private void AddExistingInstanceName(string? name)
    {
        var normalized = NormalizeInstanceName(name);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            lock (instanceNameStateSync)
            {
                existingInstanceNames.Add(normalized);
            }
        }
    }

    private void AddPendingInstanceName(string? name)
    {
        var normalized = NormalizeInstanceName(name);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            lock (instanceNameStateSync)
            {
                pendingInstanceNames.Add(normalized);
            }
        }
    }

    private void RemovePendingInstanceName(string? name)
    {
        var normalized = NormalizeInstanceName(name);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            lock (instanceNameStateSync)
            {
                pendingInstanceNames.Remove(normalized);
            }
        }
    }

    private void RefreshInstanceNameDuplicateMessage()
    {
        var normalized = NormalizeInstanceName(InstanceName);
        var hasDuplicateName = false;
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            lock (instanceNameStateSync)
            {
                hasDuplicateName = existingInstanceNames.Contains(normalized)
                    || pendingInstanceNames.Contains(normalized);
            }
        }

        InstanceNameDuplicateMessage = !string.IsNullOrWhiteSpace(normalized)
            && hasDuplicateName
                ? "\u5df2\u5b58\u5728\u540c\u540d\u7248\u672c"
                : string.Empty;
    }

    private static string NormalizeInstanceName(string? name)
    {
        return name?.Trim() ?? string.Empty;
    }

    private DownloadInstallProgress CreateProgress(DownloadTaskItem installTask, long installSequence)
    {
        return new DownloadInstallProgress(this, installTask, installSequence);
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

    private sealed class DownloadInstallProgress : IProgress<LauncherProgress>, IDisposable
    {
        private static readonly TimeSpan UiUpdateInterval = TimeSpan.FromMilliseconds(120);

        private readonly object syncRoot = new();
        private readonly DownloadPageViewModel viewModel;
        private readonly DownloadTaskItem installTask;
        private readonly long installSequence;
        private LauncherProgress? pendingProgress;
        private DateTimeOffset lastFlushedAt = DateTimeOffset.MinValue;
        private bool isFlushQueued;
        private bool isDisposed;

        public DownloadInstallProgress(
            DownloadPageViewModel viewModel,
            DownloadTaskItem installTask,
            long installSequence)
        {
            this.viewModel = viewModel;
            this.installTask = installTask;
            this.installSequence = installSequence;
        }

        public void Report(LauncherProgress value)
        {
            TimeSpan delay;
            lock (syncRoot)
            {
                if (isDisposed)
                    return;

                pendingProgress = value;
                if (isFlushQueued)
                    return;

                var elapsed = DateTimeOffset.UtcNow - lastFlushedAt;
                delay = elapsed >= UiUpdateInterval
                    ? TimeSpan.Zero
                    : UiUpdateInterval - elapsed;
                isFlushQueued = true;
            }

            QueueFlush(delay);
        }

        public void Dispose()
        {
            lock (syncRoot)
            {
                isDisposed = true;
                pendingProgress = null;
                isFlushQueued = false;
            }
        }

        private void QueueFlush(TimeSpan delay)
        {
            if (delay <= TimeSpan.Zero)
            {
                PostFlush();
                return;
            }

            _ = FlushAfterDelayAsync(delay);
        }

        private async Task FlushAfterDelayAsync(TimeSpan delay)
        {
            try
            {
                await Task.Delay(delay);
                PostFlush();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void PostFlush()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(Flush, DispatcherPriority.Background);
                return;
            }

            Flush();
        }

        private void Flush()
        {
            LauncherProgress? progress;
            lock (syncRoot)
            {
                if (isDisposed)
                    return;

                progress = pendingProgress;
                pendingProgress = null;
                lastFlushedAt = DateTimeOffset.UtcNow;
                isFlushQueued = false;
            }

            if (progress is not null)
                viewModel.ReportInstallProgress(installTask, progress, installSequence);
        }
    }

    private static IEnumerable<DownloadMinecraftVersionItem> SortVersionsForCategory(
        IEnumerable<DownloadMinecraftVersionItem> versions,
        string? categoryId)
    {
        if (categoryId == "snapshot")
        {
            return versions
                .OrderByDescending(version => version.Version.ReleaseTime ?? DateTimeOffset.MinValue)
                .ThenByDescending(version => version.Name, StringComparer.OrdinalIgnoreCase);
        }

        return versions;
    }

    private void ClearSelectedVersion()
    {
        SelectedMinecraftVersion = null;
        foreach (var item in AllVersions)
            item.IsSelected = false;
    }

}

public enum DownloadPageStep
{
    VersionList,
    InstanceOptions
}

public sealed partial class DownloadVersionCategory : ObservableObject
{
    public DownloadVersionCategory(string id, string title, string icon, string? iconKey = null)
    {
        Id = id;
        Title = title;
        Icon = icon;
        IconKey = iconKey;
    }

    public string Id { get; }

    public string Title { get; }

    public string Icon { get; }

    public string? IconKey { get; }

    public string IconMode => string.IsNullOrWhiteSpace(IconKey) ? "Glyph" : "Svg";

    [ObservableProperty]
    private bool isSelected;
}

public sealed partial class DownloadLoaderOption : ObservableObject
{
    public DownloadLoaderOption(LoaderKind kind, string title, string subtitle, string icon, string? iconSource = null)
    {
        Kind = kind;
        Title = title;
        Subtitle = subtitle;
        Icon = icon;
        IconSource = iconSource;
    }

    public LoaderKind Kind { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string Icon { get; }

    public string? IconSource { get; }

    [ObservableProperty]
    private bool isSelected;
}

public sealed partial class DownloadMinecraftVersionItem : ObservableObject
{
    public DownloadMinecraftVersionItem(MinecraftVersionInfo version)
    {
        Version = version;
    }

    public MinecraftVersionInfo Version { get; }

    public string Name => Version.Name;

    public string Type => Version.Type;

    public string TypeLabel => Version.Type.ToLowerInvariant() switch
    {
        "release" => "\u6b63\u5f0f\u7248",
        "snapshot" => "\u5feb\u7167\u7248",
        _ => Version.Type
    };

    public string ReleaseDateText => Version.ReleaseTime is { } releaseTime
        ? releaseTime.ToLocalTime().ToString("yyyy-MM-dd")
        : string.Empty;

    public bool IsRelease => Version.Type.Equals("Release", StringComparison.OrdinalIgnoreCase);

    public bool IsSnapshot => Version.Type.Equals("Snapshot", StringComparison.OrdinalIgnoreCase);

    public string IconSource => IsSnapshot
        ? "/Assets/Icons/block/dirt_block.png"
        : "/Assets/Icons/block/grass_block.png";

    [ObservableProperty]
    private bool isSelected;
}
