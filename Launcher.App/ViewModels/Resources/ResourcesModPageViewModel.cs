using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.Utilities;
using Launcher.App.ViewModels.Download;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.Resources;

public sealed partial class ResourcesModPageViewModel : ResourcesSectionViewModelBase
{
    private const int SearchDebounceMilliseconds = 350;
    private const int CatalogPageSize = 20;
    private const int AvailableVersionPageSize = 10000;
    private const int InitialProjectBatchSize = 12;
    private const int AppendProjectBatchSize = 8;
    private readonly IResourceCatalogService? resourceCatalogService;
    private readonly IGameVersionService? gameVersionService;
    private readonly IGameInstanceService? gameInstanceService;
    private readonly IStatusService? statusService;
    private readonly IFilePickerService? filePickerService;
    private readonly IFloatingMessageService? floatingMessageService;
    private readonly DownloadTasksPageViewModel? downloadTasksPage;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger? logger;
    private readonly object releaseVersionOrderGate = new();
    private CancellationTokenSource? refreshCancellationTokenSource;
    private CancellationTokenSource? installTargetsCancellationTokenSource;
    private CancellationTokenSource? projectVersionsCancellationTokenSource;
    private bool hasRequestedInitialProjectLoad;
    private bool isApplyingVersionFilterOptions;
    private bool isApplyingInstanceFilters;
    private bool isApplyingAvailableVersionFilterOptions;
    private IReadOnlyList<ResourceProjectVersion> availableVersionSourceVersions = [];
    private readonly HashSet<string> loadedAvailableVersionIds = new(StringComparer.OrdinalIgnoreCase);
    private int nextAvailableVersionOffset;
    private Task<ReleaseVersionData?>? releaseVersionDataTask;

    public ResourcesModPageViewModel(
        ResourcesPageViewModel parent,
        IResourceCatalogService? resourceCatalogService = null,
        ILogger? logger = null,
        IUiDispatcher? uiDispatcher = null,
        IGameVersionService? gameVersionService = null,
        IGameInstanceService? gameInstanceService = null,
        IStatusService? statusService = null,
        IFilePickerService? filePickerService = null,
        IFloatingMessageService? floatingMessageService = null,
        DownloadTasksPageViewModel? downloadTasksPage = null)
        : base(parent, Strings.Resources_SectionMods)
    {
        this.resourceCatalogService = resourceCatalogService;
        this.gameVersionService = gameVersionService;
        this.gameInstanceService = gameInstanceService;
        this.statusService = statusService;
        this.filePickerService = filePickerService;
        this.floatingMessageService = floatingMessageService;
        this.downloadTasksPage = downloadTasksPage;
        this.uiDispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;
        this.logger = logger;

        VersionOptions = [new ResourcesFilterOptionItem { Id = "all", Title = Strings.Resources_ModFilterAllVersions }];
        LoaderOptions =
        [
            new ResourcesFilterOptionItem { Id = "all", Title = Strings.Resources_ModFilterAllLoaders },
            new ResourcesFilterOptionItem { Id = "fabric", Title = Strings.Download_FabricLoaderTitle },
            new ResourcesFilterOptionItem { Id = "forge", Title = Strings.Download_ForgeLoaderTitle },
            new ResourcesFilterOptionItem { Id = "neoforge", Title = Strings.Download_NeoForgeLoaderTitle },
            new ResourcesFilterOptionItem { Id = "quilt", Title = Strings.Download_QuiltLoaderTitle }
        ];
        SourceOptions =
        [
            new ResourcesFilterOptionItem { Id = "all", Title = Strings.Resources_ModFilterAllSources },
            new ResourcesFilterOptionItem { Id = "modrinth", Title = Strings.Resources_ModSourceModrinth },
            new ResourcesFilterOptionItem { Id = "curseforge", Title = Strings.Resources_ModSourceCurseForge }
        ];
        TypeOptions = [new ResourcesFilterOptionItem { Id = "all", Title = Strings.Resources_ModFilterAllTypes }];
        AvailableVersionFilterOptions = [CreateAllAvailableVersionFilterOption()];
        AvailableLoaderFilterOptions = [.. CreateDefaultAvailableLoaderFilterOptions()];

        selectedVersionOption = VersionOptions[0];
        selectedLoaderOption = LoaderOptions[0];
        selectedSourceOption = SourceOptions[0];
        selectedTypeOption = TypeOptions[0];
        selectedAvailableVersionFilterOption = AvailableVersionFilterOptions[0];
        selectedAvailableLoaderFilterOption = AvailableLoaderFilterOptions[0];
    }

    public ObservableCollection<ResourcesFilterOptionItem> VersionOptions { get; }

    public ObservableCollection<ResourcesFilterOptionItem> LoaderOptions { get; }

    public ObservableCollection<ResourcesFilterOptionItem> SourceOptions { get; }

    public ObservableCollection<ResourcesFilterOptionItem> TypeOptions { get; }

    public ObservableCollection<ResourcesFilterOptionItem> AvailableVersionFilterOptions { get; }

    public ObservableCollection<ResourcesFilterOptionItem> AvailableLoaderFilterOptions { get; }

    public ObservableCollection<ResourcesModProjectItemViewModel> VisibleProjects { get; } = [];

    public ObservableCollection<ResourcesModInstallTargetItemViewModel> InstallTargets { get; } = [];

    public ObservableCollection<ResourcesModVersionItemViewModel> AvailableVersions { get; } = [];

    public ObservableCollection<object> AvailableVersionListItems { get; } = [];

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvailableVersionsEmptyMessage))]
    private string availableVersionSearchQuery = string.Empty;

    [ObservableProperty]
    private bool isLoadingProjects;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowLoadMoreState))]
    private bool isLoadingMoreProjects;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadErrorMessage))]
    private string loadErrorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowLoadMoreState))]
    private string loadMoreMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPartialWarningMessage))]
    private string partialWarningMessage = string.Empty;

    [ObservableProperty]
    private int listEntranceAnimationToken;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProjectListStep))]
    [NotifyPropertyChangedFor(nameof(IsProjectDetailsStep))]
    [NotifyPropertyChangedFor(nameof(IsProjectVersionsStep))]
    [NotifyPropertyChangedFor(nameof(IsProjectContentStep))]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyPropertyChangedFor(nameof(PageTitleIconSource))]
    private ResourcesModPageStep currentStep = ResourcesModPageStep.ProjectList;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyPropertyChangedFor(nameof(PageTitleIconSource))]
    private ResourcesModProjectItemViewModel? selectedProject;

    [ObservableProperty]
    private ResourcesFilterOptionItem? selectedVersionOption;

    [ObservableProperty]
    private ResourcesFilterOptionItem? selectedLoaderOption;

    [ObservableProperty]
    private ResourcesFilterOptionItem? selectedSourceOption;

    [ObservableProperty]
    private ResourcesFilterOptionItem? selectedTypeOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvailableVersionsEmptyMessage))]
    private ResourcesFilterOptionItem? selectedAvailableVersionFilterOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvailableVersionsEmptyMessage))]
    private ResourcesFilterOptionItem? selectedAvailableLoaderFilterOption;

    [ObservableProperty]
    private bool isFilterDialogOpen;

    [ObservableProperty]
    private bool isProjectVersionFileExistsDialogOpen;

    [ObservableProperty]
    private string projectVersionFileExistsDialogMessage = string.Empty;

    [ObservableProperty]
    private bool isUnknownInstanceVersionDialogOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowInstallTargetsLoadingState))]
    [NotifyPropertyChangedFor(nameof(CanShowInstallTargetsLoadErrorState))]
    private bool isLoadingInstallTargets;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstallTargetsLoadErrorMessage))]
    [NotifyPropertyChangedFor(nameof(CanShowInstallTargetsLoadErrorState))]
    private string installTargetsLoadErrorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvailableVersionsEmptyMessage))]
    [NotifyPropertyChangedFor(nameof(AvailableVersionsTitle))]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyPropertyChangedFor(nameof(PageTitleIconSource))]
    private ResourcesModInstallTargetItemViewModel? selectedInstallTarget;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowAvailableVersionsLoadingState))]
    [NotifyPropertyChangedFor(nameof(CanShowAvailableVersionsEmptyState))]
    [NotifyPropertyChangedFor(nameof(CanShowAvailableVersionsLoadErrorState))]
    private bool isLoadingAvailableVersions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvailableVersionsLoadErrorMessage))]
    [NotifyPropertyChangedFor(nameof(CanShowAvailableVersionsEmptyState))]
    [NotifyPropertyChangedFor(nameof(CanShowAvailableVersionsLoadErrorState))]
    private string availableVersionsLoadErrorMessage = string.Empty;

    [ObservableProperty]
    private int availableVersionListEntranceAnimationToken;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowAvailableVersionsLoadingState))]
    [NotifyPropertyChangedFor(nameof(CanShowAvailableVersionsEmptyState))]
    [NotifyPropertyChangedFor(nameof(CanShowAvailableVersionsLoadMoreState))]
    private int visibleAvailableVersionCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowAvailableVersionsLoadMoreState))]
    private bool isLoadingMoreAvailableVersions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowAvailableVersionsLoadMoreState))]
    private string availableVersionsLoadMoreMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowAvailableVersionsLoadMoreState))]
    private bool hasMoreAvailableVersions;

    [ObservableProperty]
    private bool isInstallingAvailableVersion;

    [ObservableProperty]
    private ResourcesFilterOptionItem? pendingVersionOption;

    [ObservableProperty]
    private ResourcesFilterOptionItem? pendingLoaderOption;

    [ObservableProperty]
    private ResourcesFilterOptionItem? pendingSourceOption;

    [ObservableProperty]
    private ResourcesFilterOptionItem? pendingTypeOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowLoadMoreState))]
    private bool hasMoreProjects;

    private int nextPageOffset;

    public bool HasVisibleProjects => VisibleProjects.Count > 0;

    public bool HasLoadErrorMessage => !string.IsNullOrWhiteSpace(LoadErrorMessage);

    public bool HasPartialWarningMessage => !string.IsNullOrWhiteSpace(PartialWarningMessage);

    public string EmptyMessage => Strings.Resources_ModProjectsEmpty;

    public bool CanShowLoadingState => IsLoadingProjects && !HasVisibleProjects;

    public bool CanShowEmptyState => !IsLoadingProjects && !HasVisibleProjects && !HasLoadErrorMessage;

    public bool CanShowLoadErrorState => !IsLoadingProjects && !HasVisibleProjects && HasLoadErrorMessage;

    public bool CanShowLoadMoreState => HasVisibleProjects
        && (IsLoadingMoreProjects || !string.IsNullOrWhiteSpace(LoadMoreMessage));

    public bool IsProjectListStep => CurrentStep is ResourcesModPageStep.ProjectList;

    public bool IsProjectDetailsStep => CurrentStep is ResourcesModPageStep.ProjectDetails;

    public bool IsProjectVersionsStep => CurrentStep is ResourcesModPageStep.ProjectVersions;

    public bool IsProjectContentStep => CurrentStep is not ResourcesModPageStep.ProjectList;

    public bool HasInstallTargetsLoadErrorMessage => !string.IsNullOrWhiteSpace(InstallTargetsLoadErrorMessage);

    public bool CanShowInstallTargetsLoadingState => IsLoadingInstallTargets && InstallTargets.Count == 0;

    public bool CanShowInstallTargetsLoadErrorState => !IsLoadingInstallTargets && HasInstallTargetsLoadErrorMessage;

    public bool HasAvailableVersionsLoadErrorMessage => !string.IsNullOrWhiteSpace(AvailableVersionsLoadErrorMessage);

    public bool CanShowAvailableVersionsLoadingState => IsLoadingAvailableVersions && VisibleAvailableVersionCount == 0;

    public bool CanShowAvailableVersionsEmptyState => !IsLoadingAvailableVersions
        && VisibleAvailableVersionCount == 0
        && !HasMoreAvailableVersions
        && !HasAvailableVersionsLoadErrorMessage;

    public bool CanShowAvailableVersionsLoadErrorState => !IsLoadingAvailableVersions && HasAvailableVersionsLoadErrorMessage;

    public bool CanShowAvailableVersionsLoadMoreState => VisibleAvailableVersionCount > 0
        && (IsLoadingMoreAvailableVersions || !string.IsNullOrWhiteSpace(AvailableVersionsLoadMoreMessage));

    public string AvailableVersionsEmptyMessage => HasAvailableVersionFilters
        ? Strings.Resources_ModVersionsFilterEmpty
        : SelectedInstallTarget?.IsLocalDownload == true || IsUnknownInstanceVersionTarget(SelectedInstallTarget)
            ? Strings.Resources_ModVersionsEmptyLocal
            : Strings.Resources_ModVersionsEmpty;

    public bool HasAvailableVersionFilters => !string.IsNullOrWhiteSpace(AvailableVersionSearchQuery)
        || (SelectedAvailableVersionFilterOption?.Id is { } versionId
            && !string.Equals(versionId, "all", StringComparison.OrdinalIgnoreCase))
        || (SelectedAvailableLoaderFilterOption?.Id is { } loaderId
            && !string.Equals(loaderId, "all", StringComparison.OrdinalIgnoreCase));

    public string AvailableVersionsTitle => FormatAvailableVersionsTitle(SelectedInstallTarget);

    public string PageTitle => IsProjectVersionsStep && SelectedInstallTarget?.IsLocalDownload == false
        ? SelectedInstallTarget.Title
        : IsProjectContentStep
        ? SelectedProject?.Title ?? Title
        : Title;

    public string? PageTitleIconSource => IsProjectVersionsStep && SelectedInstallTarget?.IsLocalDownload == false
        ? SelectedInstallTarget.IconSource
        : IsProjectContentStep
        ? SelectedProject?.IconSource
        : null;

    [RelayCommand]
    public async Task RefreshProjectsAsync()
    {
        await RefreshProjectsCoreAsync();
    }

    [RelayCommand]
    public async Task LoadMoreProjectsAsync()
    {
        await LoadMoreProjectsCoreAsync();
    }

    [RelayCommand]
    private void OpenFilterDialog()
    {
        PendingVersionOption = SelectedVersionOption;
        PendingLoaderOption = SelectedLoaderOption;
        PendingSourceOption = SelectedSourceOption;
        PendingTypeOption = SelectedTypeOption;
        IsFilterDialogOpen = true;
        logger?.LogDebug("Resources mod filter dialog opened.");
    }

    [RelayCommand]
    private void CancelFilterDialog()
    {
        IsFilterDialogOpen = false;
    }

    [RelayCommand]
    private void ConfirmFilterDialog()
    {
        ApplyPendingOption(PendingVersionOption, SelectedVersionOption, value => SelectedVersionOption = value);
        ApplyPendingOption(PendingLoaderOption, SelectedLoaderOption, value => SelectedLoaderOption = value);
        ApplyPendingOption(PendingSourceOption, SelectedSourceOption, value => SelectedSourceOption = value);
        ApplyPendingOption(PendingTypeOption, SelectedTypeOption, value => SelectedTypeOption = value);
        IsFilterDialogOpen = false;
    }

    [RelayCommand]
    private void CloseProjectVersionFileExistsDialog()
    {
        IsProjectVersionFileExistsDialogOpen = false;
        ProjectVersionFileExistsDialogMessage = string.Empty;
    }

    [RelayCommand]
    private void CloseUnknownInstanceVersionDialog()
    {
        IsUnknownInstanceVersionDialogOpen = false;
    }

    [RelayCommand]
    private void SelectProject(ResourcesModProjectItemViewModel? project)
    {
        if (project is null)
            return;

        SelectedProject = project;
        CurrentStep = ResourcesModPageStep.ProjectDetails;
        SelectedInstallTarget = null;
        AvailableVersions.Clear();
        AvailableVersionListItems.Clear();
        ClearAvailableVersionState(resetFilters: true);
        AvailableVersionsLoadErrorMessage = string.Empty;
        _ = LoadInstallTargetsAsync();
        logger?.LogInformation(
            "Resources mod project selected. Source={Source}, ProjectId={ProjectId}",
            project.Project.Source,
            project.Project.ProjectId);
    }

    [RelayCommand]
    public void BackToProjectList()
    {
        if (CurrentStep is ResourcesModPageStep.ProjectVersions)
        {
            CurrentStep = ResourcesModPageStep.ProjectDetails;
            return;
        }

        ResetToProjectList();
    }

    public void ResetToProjectList()
    {
        CurrentStep = ResourcesModPageStep.ProjectList;
    }

    [RelayCommand]
    private void SelectInstallTarget(ResourcesModInstallTargetItemViewModel? target)
    {
        if (target is null || SelectedProject is null || resourceCatalogService is null)
            return;

        SelectedInstallTarget = target;
        CurrentStep = ResourcesModPageStep.ProjectVersions;
        if (IsUnknownInstanceVersionTarget(target))
            IsUnknownInstanceVersionDialogOpen = true;

        _ = LoadAvailableVersionsAsync(target);
        if (target.Instance is null)
        {
            logger?.LogInformation(
                "Resources mod local download target selected. ProjectId={ProjectId}",
                SelectedProject.Project.ProjectId);
        }
        else
        {
            logger?.LogInformation(
                "Resources mod install target selected. ProjectId={ProjectId} InstanceId={InstanceId} IsUnknownMinecraftVersion={IsUnknownMinecraftVersion}",
                SelectedProject.Project.ProjectId,
                target.Instance.Id,
                IsUnknownInstanceVersionTarget(target));
        }
    }

    public async Task LoadInstallTargetsAsync()
    {
        installTargetsCancellationTokenSource?.Cancel();
        installTargetsCancellationTokenSource?.Dispose();
        installTargetsCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = installTargetsCancellationTokenSource.Token;

        uiDispatcher.Invoke(() => BeginInstallTargetLoad(cancellationToken));

        try
        {
            IReadOnlyList<GameInstance> instances = [];
            if (gameInstanceService is not null)
                instances = await gameInstanceService.GetInstancesAsync(cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
                return;

            uiDispatcher.Invoke(() => ApplyInstallTargets(instances, loadErrorMessage: string.Empty, cancellationToken));
            logger?.LogInformation("Resources mod install targets loaded. InstanceCount={InstanceCount}", instances.Count);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            uiDispatcher.Invoke(() => ApplyInstallTargets([], Strings.Resources_ModInstallTargetsLoadError, cancellationToken));
            logger?.LogError(exception, "Failed to load resources mod install targets.");
        }
    }

    public async Task LoadAvailableVersionsAsync(ResourcesModInstallTargetItemViewModel target)
    {
        projectVersionsCancellationTokenSource?.Cancel();
        projectVersionsCancellationTokenSource?.Dispose();
        projectVersionsCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = projectVersionsCancellationTokenSource.Token;

        uiDispatcher.Invoke(() => BeginAvailableVersionLoad(cancellationToken));

        try
        {
            var project = SelectedProject?.Project;
            var instance = target.Instance;
            if (resourceCatalogService is null || project is null)
            {
                uiDispatcher.Invoke(() => ApplyAvailableVersions([], Strings.Resources_ModVersionsLoadError, hasMore: false, cancellationToken));
                return;
            }

            if (!target.IsLocalDownload && instance is null)
            {
                uiDispatcher.Invoke(() => ApplyAvailableVersions([], Strings.Resources_ModVersionsLoadError, hasMore: false, cancellationToken));
                return;
            }

            BeginAvailableVersionRequestState();
            var page = await LoadNextAvailableVersionPageAsync(project, cancellationToken)
                .ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
                return;

            var errorMessage = page.Result.IsCurseForgeUnavailable
                ? Strings.Resources_ModVersionsLoadError
                : string.Empty;
            uiDispatcher.Invoke(() => ApplyAvailableVersions(
                page.Versions,
                errorMessage,
                hasMore: !page.Result.IsCurseForgeUnavailable && page.Result.HasMore,
                cancellationToken));
            logger?.LogInformation(
                "Resources mod versions loaded. ProjectId={ProjectId} InstanceId={InstanceId} VersionCount={VersionCount}",
                project.ProjectId,
                instance?.Id,
                page.Versions.Count);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            uiDispatcher.Invoke(() => ApplyAvailableVersions([], Strings.Resources_ModVersionsLoadError, hasMore: false, cancellationToken));
            logger?.LogError(exception, "Failed to load resources mod versions.");
        }
    }

    public void BeginLoadMoreAvailableVersions()
    {
        if (resourceCatalogService is null
            || SelectedProject is null
            || SelectedInstallTarget is null
            || !HasMoreAvailableVersions
            || IsLoadingAvailableVersions
            || IsLoadingMoreAvailableVersions)
        {
            return;
        }

        _ = LoadMoreAvailableVersionsAsync();
    }

    public async Task LoadMoreAvailableVersionsAsync()
    {
        var project = SelectedProject?.Project;
        var target = SelectedInstallTarget;
        if (resourceCatalogService is null
            || project is null
            || target is null
            || !HasMoreAvailableVersions
            || IsLoadingAvailableVersions
            || IsLoadingMoreAvailableVersions)
        {
            return;
        }

        var cancellationToken = projectVersionsCancellationTokenSource?.Token ?? CancellationToken.None;

        IsLoadingMoreAvailableVersions = true;
        AvailableVersionsLoadMoreMessage = Strings.Resources_ModVersionsLoadingMore;
        logger?.LogInformation(
            "Loading more resource mod versions. ProjectId={ProjectId}",
            project.ProjectId);

        try
        {
            var page = await LoadNextAvailableVersionPageAsync(project, cancellationToken)
                .ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
                return;

            if (page.Result.IsCurseForgeUnavailable)
            {
                uiDispatcher.Invoke(() => FailAvailableVersionLoadMore(cancellationToken));
                logger?.LogWarning(
                    "Resource mod versions append failed because CurseForge is unavailable. ProjectId={ProjectId}",
                    project.ProjectId);
                return;
            }

            uiDispatcher.Invoke(() => ApplyMoreAvailableVersions(
                page.Versions,
                hasMore: !page.Result.IsCurseForgeUnavailable && page.Result.HasMore,
                cancellationToken));
            logger?.LogInformation(
                "Resource mod versions appended. ProjectId={ProjectId} ResultCount={ResultCount} HasMore={HasMore}",
                project.ProjectId,
                page.Versions.Count,
                HasMoreAvailableVersions);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            uiDispatcher.Invoke(() => FailAvailableVersionLoadMore(cancellationToken));
            logger?.LogError(
                exception,
                "Failed to load more resource mod versions. ProjectId={ProjectId}",
                project.ProjectId);
        }
    }

    [RelayCommand]
    private async Task InstallAvailableVersionAsync(ResourcesModVersionItemViewModel? item)
    {
        var target = SelectedInstallTarget;
        if (item is null || target is null || resourceCatalogService is null || IsInstallingAvailableVersion)
            return;

        IsInstallingAvailableVersion = true;
        DownloadTaskItem? downloadTask = null;
        try
        {
            if (target.IsLocalDownload)
            {
                var targetDirectory = filePickerService?.PickFolder(Strings.FilePicker_ModDownloadDirectoryTitle);
                if (string.IsNullOrWhiteSpace(targetDirectory))
                    return;

                if (await resourceCatalogService.ProjectVersionDownloadExistsAsync(item.Version, targetDirectory).ConfigureAwait(false))
                {
                    ShowProjectVersionFileExistsDialog(item);
                    logger?.LogInformation(
                        "Skipped resources mod local download because target file exists. ProjectId={ProjectId} VersionId={VersionId} TargetDirectory={TargetDirectory}",
                        SelectedProject?.Project.ProjectId,
                        item.Version.VersionId,
                        targetDirectory);
                    return;
                }

                downloadTask = BeginModDownloadTask(item, targetDirectory);
                floatingMessageService?.Show(Strings.Status_ModDownloading);
                ReportStatus(string.Format(Strings.Status_ModDownloadingFormat, item.Title));
                await resourceCatalogService.DownloadProjectVersionAsync(
                        item.Version,
                        targetDirectory,
                        downloadTask?.CancellationToken ?? CancellationToken.None)
                    .ConfigureAwait(false);
                if (CompleteCanceledModDownloadTask(downloadTask))
                    return;

                var downloadedMessage = string.Format(Strings.Status_ModDownloadedFormat, ResolveVersionFileNameForDisplay(item));
                CompleteModDownloadTask(downloadTask, downloadedMessage);
                ReportStatus(downloadedMessage);
                logger?.LogInformation(
                    "Resources mod version downloaded locally. ProjectId={ProjectId} VersionId={VersionId} TargetDirectory={TargetDirectory}",
                    SelectedProject?.Project.ProjectId,
                    item.Version.VersionId,
                    targetDirectory);
                return;
            }

            var instance = target.Instance;
            if (instance is null)
                return;

            if (await resourceCatalogService.ProjectVersionInstallExistsAsync(item.Version, instance).ConfigureAwait(false))
            {
                ShowProjectVersionFileExistsDialog(item);
                logger?.LogInformation(
                    "Skipped resources mod version install because target file exists. ProjectId={ProjectId} VersionId={VersionId} InstanceId={InstanceId}",
                    SelectedProject?.Project.ProjectId,
                    item.Version.VersionId,
                    instance.Id);
                return;
            }

            downloadTask = BeginModDownloadTask(item, target.Title);
            floatingMessageService?.Show(Strings.Status_ModDownloading);
            ReportStatus(string.Format(Strings.Status_ModDownloadingFormat, item.Title));
            await resourceCatalogService.InstallProjectVersionAsync(
                    item.Version,
                    instance,
                    downloadTask?.CancellationToken ?? CancellationToken.None)
                .ConfigureAwait(false);
            if (CompleteCanceledModDownloadTask(downloadTask))
                return;

            var installedMessage = string.Format(Strings.Status_ModInstalledFormat, SelectedProject?.Title ?? item.Title);
            CompleteModDownloadTask(downloadTask, installedMessage);
            ReportStatus(installedMessage);
            logger?.LogInformation(
                "Resources mod version installed. ProjectId={ProjectId} VersionId={VersionId} InstanceId={InstanceId}",
                SelectedProject?.Project.ProjectId,
                item.Version.VersionId,
                instance.Id);
        }
        catch (OperationCanceledException) when (downloadTask?.IsCancellationRequested == true)
        {
            CompleteCanceledModDownloadTask(downloadTask);
            logger?.LogInformation(
                "Resources mod version download canceled. ProjectId={ProjectId} VersionId={VersionId}",
                SelectedProject?.Project.ProjectId,
                item.Version.VersionId);
        }
        catch (Exception exception)
        {
            ReportStatus(target.IsLocalDownload ? Strings.Status_ModDownloadFailed : Strings.Status_ModInstallFailed);
            if (target.IsLocalDownload)
            {
                floatingMessageService?.Show(Strings.Status_ModDownloadFailed);
                FailModDownloadTask(downloadTask, Strings.Status_ModDownloadFailed);
                logger?.LogError(
                    exception,
                    "Failed to download resources mod version locally. ProjectId={ProjectId} VersionId={VersionId}",
                    SelectedProject?.Project.ProjectId,
                    item.Version.VersionId);
            }
            else
            {
                floatingMessageService?.Show(Strings.Status_ModInstallFailed);
                FailModDownloadTask(downloadTask, Strings.Status_ModInstallFailed);
                logger?.LogError(
                    exception,
                    "Failed to install resources mod version. ProjectId={ProjectId} VersionId={VersionId} InstanceId={InstanceId}",
                    SelectedProject?.Project.ProjectId,
                    item.Version.VersionId,
                    target.Instance?.Id);
            }
        }
        finally
        {
            IsInstallingAvailableVersion = false;
        }
    }

    public void BeginEnsureProjectsLoaded()
    {
        BeginEnsureVersionOptionsLoaded();

        if (hasRequestedInitialProjectLoad || resourceCatalogService is null)
            return;

        _ = RefreshProjectsCoreAsync();
    }

    public async Task ApplyInstanceFiltersAsync(GameInstance instance)
    {
        await EnsureVersionOptionsLoadedAsync().ConfigureAwait(true);

        var versionOption = ResolveVersionOptionForInstance(instance);
        var loaderOption = ResolveLoaderOptionForInstance(instance);

        try
        {
            isApplyingInstanceFilters = true;
            SelectedVersionOption = versionOption;
            SelectedLoaderOption = loaderOption;
            ResetToProjectList();
        }
        finally
        {
            isApplyingInstanceFilters = false;
        }

        logger?.LogInformation(
            "Applied resource mod filters from instance. InstanceId={InstanceId}, MinecraftVersion={MinecraftVersion}, VersionFilter={VersionFilter}, Loader={Loader}, LoaderFilter={LoaderFilter}",
            instance.Id,
            instance.MinecraftVersion,
            SelectedVersionOption?.Id,
            instance.Loader,
            SelectedLoaderOption?.Id);
        await RefreshProjectsCoreAsync().ConfigureAwait(true);
    }

    public void BeginLoadMoreProjects()
    {
        if (resourceCatalogService is null
            || !HasVisibleProjects
            || !HasMoreProjects
            || IsLoadingProjects
            || IsLoadingMoreProjects)
        {
            return;
        }

        _ = LoadMoreProjectsCoreAsync();
    }

    partial void OnSearchQueryChanged(string value)
    {
        ResetToProjectList();
        ScheduleProjectsRefresh();
    }

    partial void OnSelectedVersionOptionChanged(ResourcesFilterOptionItem? value)
    {
        if (isApplyingVersionFilterOptions || isApplyingInstanceFilters)
            return;

        ResetToProjectList();
        LogFilterSelection("version", value);
        ScheduleProjectsRefresh();
    }

    partial void OnSelectedLoaderOptionChanged(ResourcesFilterOptionItem? value)
    {
        if (isApplyingInstanceFilters)
            return;

        ResetToProjectList();
        LogFilterSelection("loader", value);
        ScheduleProjectsRefresh();
    }

    partial void OnSelectedSourceOptionChanged(ResourcesFilterOptionItem? value)
    {
        ResetToProjectList();
        LogFilterSelection("source", value);
        ScheduleProjectsRefresh();
    }

    partial void OnSelectedTypeOptionChanged(ResourcesFilterOptionItem? value)
    {
        ResetToProjectList();
        LogFilterSelection("type", value);
        ScheduleProjectsRefresh();
    }

    partial void OnAvailableVersionSearchQueryChanged(string value)
    {
        if (isApplyingAvailableVersionFilterOptions)
            return;

        RebuildAvailableVersionListItems();
    }

    partial void OnSelectedAvailableVersionFilterOptionChanged(ResourcesFilterOptionItem? value)
    {
        if (isApplyingAvailableVersionFilterOptions)
            return;

        LogFilterSelection("available-version", value);
        RebuildAvailableVersionListItems();
    }

    partial void OnSelectedAvailableLoaderFilterOptionChanged(ResourcesFilterOptionItem? value)
    {
        if (isApplyingAvailableVersionFilterOptions)
            return;

        LogFilterSelection("available-loader", value);
        RebuildAvailableVersionListItems();
    }

    partial void OnIsFilterDialogOpenChanged(bool value)
    {
        if (!value)
            ClearPendingFilterOptions();
    }

    partial void OnIsLoadingProjectsChanged(bool value)
    {
        RaiseProjectStatePropertiesChanged();
    }

    partial void OnIsLoadingMoreProjectsChanged(bool value)
    {
        RaiseProjectStatePropertiesChanged();
    }

    partial void OnLoadErrorMessageChanged(string value)
    {
        RaiseProjectStatePropertiesChanged();
    }

    partial void OnLoadMoreMessageChanged(string value)
    {
        RaiseProjectStatePropertiesChanged();
    }

    partial void OnHasMoreProjectsChanged(bool value)
    {
        RaiseProjectStatePropertiesChanged();
    }

    partial void OnIsLoadingMoreAvailableVersionsChanged(bool value)
    {
        RaiseAvailableVersionStatePropertiesChanged();
    }

    partial void OnAvailableVersionsLoadMoreMessageChanged(string value)
    {
        UpdateAvailableVersionFooterItem();
        RaiseAvailableVersionStatePropertiesChanged();
    }

    partial void OnHasMoreAvailableVersionsChanged(bool value)
    {
        UpdateAvailableVersionFooterItem();
        RaiseAvailableVersionStatePropertiesChanged();
    }

    private async Task RunProjectLoadAsync(
        ResourceCatalogSearchRequest request,
        ProjectLoadKind loadKind,
        CancellationToken cancellationToken,
        TaskCompletionSource completion)
    {
        if (resourceCatalogService is null)
        {
            completion.TrySetResult();
            return;
        }

        try
        {
            var result = await Task
                .Run(
                    async () =>
                    {
                        var releaseVersionOrderTask = GetReleaseVersionOrderAsync(cancellationToken);
                        var catalogResult = await resourceCatalogService.SearchModsAsync(request, cancellationToken).ConfigureAwait(false);
                        var minecraftReleaseVersionOrder = await releaseVersionOrderTask.ConfigureAwait(false);
                        var items = catalogResult.Projects
                            .Select(project => new ResourcesModProjectItemViewModel(project, minecraftReleaseVersionOrder))
                            .ToList();
                        return new ProjectLoadResult(catalogResult, items, loadKind, request.Offset);
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetResult();
                return;
            }

            uiDispatcher.Post(() =>
            {
                if (result.LoadKind is ProjectLoadKind.LoadMore)
                    CompleteProjectLoadMore(result, cancellationToken, completion);
                else
                    CompleteProjectLoad(result, cancellationToken, completion);
            });
        }
        catch (OperationCanceledException)
        {
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetResult();
                return;
            }

            uiDispatcher.Post(() =>
            {
                if (loadKind is ProjectLoadKind.LoadMore)
                    FailProjectLoadMore(exception, cancellationToken, completion);
                else
                    FailProjectLoad(exception, cancellationToken, completion);
            });
        }
    }

    private async Task<IReadOnlyList<string>?> GetReleaseVersionOrderAsync(CancellationToken cancellationToken)
    {
        var data = await GetReleaseVersionDataAsync(cancellationToken).ConfigureAwait(false);
        return data?.ReleaseVersionOrder;
    }

    private void BeginEnsureVersionOptionsLoaded()
    {
        if (gameVersionService is null)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await GetReleaseVersionDataAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger?.LogWarning(exception, "Failed to start resource mod version filter load.");
            }
        });
    }

    private async Task EnsureVersionOptionsLoadedAsync()
    {
        if (gameVersionService is null)
            return;

        try
        {
            var data = await GetReleaseVersionDataAsync(CancellationToken.None).ConfigureAwait(true);
            if (data?.VersionOptions.Count > 0)
                uiDispatcher.Invoke(() => ApplyVersionFilterOptions(data.VersionOptions));
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Failed to load Minecraft release versions before applying instance resource mod filters.");
        }
    }

    private async Task<ReleaseVersionData?> GetReleaseVersionDataAsync(CancellationToken cancellationToken)
    {
        if (gameVersionService is null)
            return null;

        Task<ReleaseVersionData?> task;
        lock (releaseVersionOrderGate)
        {
            releaseVersionDataTask ??= LoadReleaseVersionDataAsync();
            task = releaseVersionDataTask;
        }

        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ReleaseVersionData?> LoadReleaseVersionDataAsync()
    {
        var versionService = gameVersionService;
        if (versionService is null)
            return null;

        try
        {
            var versions = await versionService.GetVersionsAsync()
                .ConfigureAwait(false);
            var resolvedOrder = versions
                .Where(version => string.Equals(version.Type, "release", StringComparison.OrdinalIgnoreCase))
                .Select(version => version.Name)
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .ToList();

            var options = CreateVersionFilterOptions(resolvedOrder);
            uiDispatcher.Post(() => ApplyVersionFilterOptions(options));
            return new ReleaseVersionData(resolvedOrder, options);
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Failed to load Minecraft release versions for resource mod filters.");
            return new ReleaseVersionData([], []);
        }
    }

    private static IReadOnlyList<ResourcesFilterOptionItem> CreateVersionFilterOptions(IReadOnlyList<string> releaseVersions)
    {
        return releaseVersions
            .Select(version => new
            {
                Original = version.Trim(),
                Major = TryNormalizeMajorMinecraftVersion(version)
            })
            .Where(version => !string.IsNullOrWhiteSpace(version.Original) && version.Major is not null)
            .GroupBy(version => version.Major!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ResourcesFilterOptionItem
            {
                Id = group.Key,
                Title = group.Key,
                MinecraftVersions = group
                    .Select(version => version.Original)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .ToList();
    }

    private void ApplyVersionFilterOptions(IReadOnlyList<ResourcesFilterOptionItem> options)
    {
        if (options.Count == 0)
            return;

        var selectedId = SelectedVersionOption?.Id ?? "all";
        VersionOptions.Clear();
        VersionOptions.Add(new ResourcesFilterOptionItem { Id = "all", Title = Strings.Resources_ModFilterAllVersions });
        foreach (var option in options)
            VersionOptions.Add(option);

        try
        {
            isApplyingVersionFilterOptions = true;
            SelectedVersionOption = VersionOptions.FirstOrDefault(
                option => string.Equals(option.Id, selectedId, StringComparison.OrdinalIgnoreCase)) ?? VersionOptions[0];
        }
        finally
        {
            isApplyingVersionFilterOptions = false;
        }
    }

    private ResourcesFilterOptionItem ResolveVersionOptionForInstance(GameInstance instance)
    {
        var allOption = VersionOptions.FirstOrDefault(option => option.Id == "all") ?? VersionOptions.FirstOrDefault();
        if (allOption is null || string.IsNullOrWhiteSpace(instance.MinecraftVersion))
            return allOption ?? new ResourcesFilterOptionItem { Id = "all", Title = Strings.Resources_ModFilterAllVersions };

        var minecraftVersion = instance.MinecraftVersion.Trim();
        return VersionOptions.FirstOrDefault(option =>
                option.Id != "all"
                && (string.Equals(option.Id, minecraftVersion, StringComparison.OrdinalIgnoreCase)
                    || option.MinecraftVersions.Any(version => string.Equals(version, minecraftVersion, StringComparison.OrdinalIgnoreCase))))
            ?? allOption;
    }

    private ResourcesFilterOptionItem ResolveLoaderOptionForInstance(GameInstance instance)
    {
        var loaderId = instance.Loader switch
        {
            LoaderKind.Fabric => "fabric",
            LoaderKind.Forge => "forge",
            LoaderKind.NeoForge => "neoforge",
            LoaderKind.Quilt => "quilt",
            _ => "all"
        };

        return LoaderOptions.FirstOrDefault(option => string.Equals(option.Id, loaderId, StringComparison.OrdinalIgnoreCase))
            ?? LoaderOptions[0];
    }

    private static string? TryNormalizeMajorMinecraftVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var trimmed = version.Trim();
        if (int.TryParse(trimmed, out var singlePartVersion))
            return singlePartVersion.ToString();

        var parts = trimmed.Split('.');
        if (parts.Length < 2
            || !TryParseLeadingNumber(parts[0], out var major)
            || !TryParseLeadingNumber(parts[1], out var minor))
        {
            return null;
        }

        return major == 1 ? $"{major}.{minor}" : major.ToString();
    }

    private static bool TryParseLeadingNumber(string value, out int number)
    {
        number = 0;
        var end = 0;
        while (end < value.Length && char.IsDigit(value[end]))
            end++;

        return end > 0 && int.TryParse(value[..end], out number);
    }

    private async void ScheduleProjectsRefresh()
    {
        if (resourceCatalogService is null)
            return;

        var cancellationToken = BeginProjectLoad();

        try
        {
            await Task.Delay(SearchDebounceMilliseconds, cancellationToken).ConfigureAwait(true);
            await QueueProjectLoadAsync(CreateSearchRequest(offset: 0), ProjectLoadKind.Refresh, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private Task RefreshProjectsCoreAsync()
    {
        if (resourceCatalogService is null)
            return Task.CompletedTask;

        var cancellationToken = BeginProjectLoad();
        return QueueProjectLoadAsync(CreateSearchRequest(offset: 0), ProjectLoadKind.Refresh, cancellationToken);
    }

    private Task LoadMoreProjectsCoreAsync()
    {
        if (resourceCatalogService is null
            || !HasVisibleProjects
            || !HasMoreProjects
            || IsLoadingProjects
            || IsLoadingMoreProjects)
        {
            return Task.CompletedTask;
        }

        var cancellationToken = refreshCancellationTokenSource?.Token ?? CancellationToken.None;
        IsLoadingMoreProjects = true;
        LoadMoreMessage = Strings.Resources_ModProjectsLoadingMore;
        return QueueProjectLoadAsync(CreateSearchRequest(nextPageOffset), ProjectLoadKind.LoadMore, cancellationToken);
    }

    private CancellationToken BeginProjectLoad()
    {
        hasRequestedInitialProjectLoad = true;
        refreshCancellationTokenSource?.Cancel();
        refreshCancellationTokenSource?.Dispose();
        refreshCancellationTokenSource = new CancellationTokenSource();

        IsLoadingProjects = true;
        IsLoadingMoreProjects = false;
        HasMoreProjects = false;
        nextPageOffset = 0;
        LoadErrorMessage = string.Empty;
        LoadMoreMessage = string.Empty;
        PartialWarningMessage = string.Empty;

        return refreshCancellationTokenSource.Token;
    }

    private Task QueueProjectLoadAsync(
        ResourceCatalogSearchRequest request,
        ProjectLoadKind loadKind,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        uiDispatcher.Post(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetResult();
                return;
            }

            _ = RunProjectLoadAsync(request, loadKind, cancellationToken, completion);
        });
        return completion.Task;
    }

    private void CompleteProjectLoad(
        ProjectLoadResult result,
        CancellationToken cancellationToken,
        TaskCompletionSource completion)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetResult();
                return;
            }

            ResetToProjectList();
            VisibleProjects.Clear();
            PartialWarningMessage = string.Empty;
            nextPageOffset = result.Offset + CatalogPageSize;
            HasMoreProjects = result.CatalogResult.HasMore;
            LoadMoreMessage = HasMoreProjects || result.Items.Count == 0
                ? string.Empty
                : Strings.Resources_ModProjectsNoMore;
            ListEntranceAnimationToken++;
            AddProjectBatch(result.Items, startIndex: 0, InitialProjectBatchSize);
            RaiseProjectStatePropertiesChanged();
            IsLoadingProjects = false;

            logger?.LogInformation(
                "Resources mod projects loaded. ResultCount={ResultCount} IsCurseForgeUnavailable={IsCurseForgeUnavailable}",
                result.Items.Count,
                result.CatalogResult.IsCurseForgeUnavailable);

            if (result.Items.Count <= InitialProjectBatchSize)
            {
                completion.TrySetResult();
                return;
            }

            QueueProjectBatchAppend(result.Items, InitialProjectBatchSize, cancellationToken, completion);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private void CompleteProjectLoadMore(
        ProjectLoadResult result,
        CancellationToken cancellationToken,
        TaskCompletionSource completion)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetResult();
                return;
            }

            nextPageOffset = result.Offset + CatalogPageSize;
            HasMoreProjects = result.CatalogResult.HasMore;
            if (result.Items.Count == 0)
                HasMoreProjects = false;
            LoadMoreMessage = HasMoreProjects ? string.Empty : Strings.Resources_ModProjectsNoMore;
            AddProjectBatch(result.Items, startIndex: 0, AppendProjectBatchSize);
            RaiseProjectStatePropertiesChanged();
            IsLoadingMoreProjects = false;

            logger?.LogInformation(
                "Resources mod projects appended. ResultCount={ResultCount} HasMore={HasMore}",
                result.Items.Count,
                result.CatalogResult.HasMore);

            if (result.Items.Count == 0)
            {
                if (!HasMoreProjects)
                    LoadMoreMessage = Strings.Resources_ModProjectsNoMore;
                completion.TrySetResult();
                return;
            }

            if (result.Items.Count <= AppendProjectBatchSize)
            {
                completion.TrySetResult();
                return;
            }

            QueueProjectBatchAppend(result.Items, AppendProjectBatchSize, cancellationToken, completion);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private void FailProjectLoad(
        Exception exception,
        CancellationToken cancellationToken,
        TaskCompletionSource completion)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetResult();
            return;
        }

        VisibleProjects.Clear();
        ResetToProjectList();
        LoadErrorMessage = Strings.Resources_ModProjectsLoadError;
        LoadMoreMessage = string.Empty;
        PartialWarningMessage = string.Empty;
        IsLoadingProjects = false;
        IsLoadingMoreProjects = false;
        HasMoreProjects = false;
        logger?.LogError(exception, "Failed to load resources mod projects.");
        RaiseProjectStatePropertiesChanged();
        completion.TrySetResult();
    }

    private void FailProjectLoadMore(
        Exception exception,
        CancellationToken cancellationToken,
        TaskCompletionSource completion)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetResult();
            return;
        }

        LoadMoreMessage = Strings.Resources_ModProjectsLoadMoreError;
        IsLoadingMoreProjects = false;
        logger?.LogError(exception, "Failed to load more resources mod projects.");
        RaiseProjectStatePropertiesChanged();
        completion.TrySetResult();
    }

    private void QueueProjectBatchAppend(
        IReadOnlyList<ResourcesModProjectItemViewModel> items,
        int startIndex,
        CancellationToken cancellationToken,
        TaskCompletionSource completion)
    {
        uiDispatcher.Post(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetResult();
                return;
            }

            AddProjectBatch(items, startIndex, AppendProjectBatchSize);
            RaiseProjectStatePropertiesChanged();

            var nextIndex = startIndex + AppendProjectBatchSize;
            if (nextIndex >= items.Count)
            {
                completion.TrySetResult();
                return;
            }

            QueueProjectBatchAppend(items, nextIndex, cancellationToken, completion);
        });
    }

    private void AddProjectBatch(
        IReadOnlyList<ResourcesModProjectItemViewModel> items,
        int startIndex,
        int count)
    {
        var endIndex = Math.Min(items.Count, startIndex + count);
        for (var index = startIndex; index < endIndex; index++)
            VisibleProjects.Add(items[index]);
    }

    private void ApplyInstallTargets(
        IReadOnlyList<GameInstance> instances,
        string loadErrorMessage,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        InstallTargets.Clear();
        var targets = instances
            .Where(instance => instance.Loader is not LoaderKind.Vanilla)
            .Select(ResourcesModInstallTargetItemViewModel.FromInstance)
            .Append(ResourcesModInstallTargetItemViewModel.CreateLocalDownload())
            .ToList();

        for (var index = 0; index < targets.Count; index++)
            targets[index].SetVisiblePosition(index == 0, index == targets.Count - 1);

        foreach (var target in targets)
            InstallTargets.Add(target);

        InstallTargetsLoadErrorMessage = loadErrorMessage;
        IsLoadingInstallTargets = false;
        RaiseInstallTargetStatePropertiesChanged();
    }

    private void BeginInstallTargetLoad(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        InstallTargets.Clear();
        InstallTargetsLoadErrorMessage = string.Empty;
        IsLoadingInstallTargets = true;
        RaiseInstallTargetStatePropertiesChanged();
    }

    private void ApplyAvailableVersions(
        IReadOnlyList<ResourceProjectVersion> versions,
        string loadErrorMessage,
        bool hasMore,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        AvailableVersions.Clear();
        AvailableVersionListItems.Clear();
        availableVersionSourceVersions = versions.ToList();
        foreach (var version in versions)
            AvailableVersions.Add(new ResourcesModVersionItemViewModel(version, SelectedProject));

        ApplyAvailableVersionFilterOptions(versions, SelectedInstallTarget);
        RebuildAvailableVersionListItems();

        AvailableVersionsLoadErrorMessage = loadErrorMessage;
        HasMoreAvailableVersions = hasMore;
        AvailableVersionsLoadMoreMessage = HasMoreAvailableVersions ? string.Empty : Strings.Resources_ModVersionsNoMore;
        IsLoadingMoreAvailableVersions = false;
        IsLoadingAvailableVersions = false;
        RaiseAvailableVersionStatePropertiesChanged();
    }

    private void ApplyMoreAvailableVersions(
        IReadOnlyList<ResourceProjectVersion> versions,
        bool hasMore,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        var sourceVersions = availableVersionSourceVersions.ToList();
        sourceVersions.AddRange(versions);
        availableVersionSourceVersions = sourceVersions;

        foreach (var version in versions)
            AvailableVersions.Add(new ResourcesModVersionItemViewModel(version, SelectedProject));

        UpdateAvailableVersionFilterOptionsPreservingSelection(availableVersionSourceVersions);
        AppendAvailableVersionListItems(versions);

        HasMoreAvailableVersions = hasMore;
        AvailableVersionsLoadMoreMessage = HasMoreAvailableVersions
            ? string.Empty
            : Strings.Resources_ModVersionsNoMore;
        IsLoadingMoreAvailableVersions = false;
        RaiseAvailableVersionStatePropertiesChanged();
    }

    private void FailAvailableVersionLoadMore(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        IsLoadingMoreAvailableVersions = false;
        AvailableVersionsLoadMoreMessage = Strings.Resources_ModVersionsLoadMoreError;
        RaiseAvailableVersionStatePropertiesChanged();
    }

    private void BeginAvailableVersionLoad(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        AvailableVersions.Clear();
        ClearAvailableVersionState(resetFilters: true);
        AddInitialAvailableVersionsHeader();
        AvailableVersionsLoadErrorMessage = string.Empty;
        AvailableVersionsLoadMoreMessage = string.Empty;
        HasMoreAvailableVersions = false;
        IsLoadingMoreAvailableVersions = false;
        IsLoadingAvailableVersions = true;
        RaiseAvailableVersionStatePropertiesChanged();
    }

    private void BeginAvailableVersionRequestState()
    {
        nextAvailableVersionOffset = 0;
        loadedAvailableVersionIds.Clear();
    }

    private async Task<AvailableVersionPageResult> LoadNextAvailableVersionPageAsync(
        ResourceProject project,
        CancellationToken cancellationToken)
    {
        if (resourceCatalogService is null)
            return new AvailableVersionPageResult(new ResourceProjectVersionsResult(), []);

        var request = CreateProjectVersionsRequest(project);
        var result = await resourceCatalogService.GetProjectVersionsAsync(request, cancellationToken)
            .ConfigureAwait(false);
        var versions = DeduplicateAvailableVersions(result.Versions);
        nextAvailableVersionOffset = request.Offset + AvailableVersionPageSize;

        logger?.LogInformation(
            "Resource mod versions page loaded. ProjectId={ProjectId} Offset={Offset} PageSize={PageSize} ResultCount={ResultCount} UniqueCount={UniqueCount} HasMore={HasMore}",
            project.ProjectId,
            request.Offset,
            request.PageSize,
            result.Versions.Count,
            versions.Count,
            result.HasMore);

        return new AvailableVersionPageResult(result, versions);
    }

    private ResourceProjectVersionsRequest CreateProjectVersionsRequest(ResourceProject project)
    {
        return new ResourceProjectVersionsRequest
        {
            Source = project.Source,
            ProjectId = project.ProjectId,
            Slug = project.Slug,
            MinecraftVersion = string.Empty,
            Loader = LoaderKind.Vanilla,
            IncludeAllVersions = true,
            Offset = nextAvailableVersionOffset,
            PageSize = AvailableVersionPageSize
        };
    }

    private IReadOnlyList<ResourceProjectVersion> DeduplicateAvailableVersions(IReadOnlyList<ResourceProjectVersion> versions)
    {
        var uniqueVersions = new List<ResourceProjectVersion>();
        foreach (var version in versions)
        {
            var key = string.IsNullOrWhiteSpace(version.VersionId)
                ? version.FileName
                : version.VersionId;
            if (string.IsNullOrWhiteSpace(key) || !loadedAvailableVersionIds.Add(key))
                continue;

            uniqueVersions.Add(version);
        }

        return uniqueVersions;
    }

    private ResourceCatalogSearchRequest CreateSearchRequest(int offset)
    {
        var selectedMinecraftVersions = SelectedVersionOption?.Id is { } versionId && versionId != "all"
            ? ResolveSelectedMinecraftVersions(SelectedVersionOption)
            : Array.Empty<string>();

        return new ResourceCatalogSearchRequest
        {
            Query = SearchQuery,
            MinecraftVersion = selectedMinecraftVersions.Count == 1 ? selectedMinecraftVersions[0] : string.Empty,
            MinecraftVersions = selectedMinecraftVersions,
            Loader = SelectedLoaderOption?.Id switch
            {
                "fabric" => LoaderKind.Fabric,
                "forge" => LoaderKind.Forge,
                "neoforge" => LoaderKind.NeoForge,
                "quilt" => LoaderKind.Quilt,
                _ => LoaderKind.Vanilla
            },
            Source = SelectedSourceOption?.Id switch
            {
                "modrinth" => ResourceProjectSource.Modrinth,
                "curseforge" => ResourceProjectSource.CurseForge,
                _ => null
            },
            Offset = offset,
            PageSize = CatalogPageSize
        };
    }

    private static IReadOnlyList<string> ResolveSelectedMinecraftVersions(ResourcesFilterOptionItem? option)
    {
        if (option is null || option.Id == "all")
            return [];

        if (option.MinecraftVersions.Count > 0)
            return option.MinecraftVersions;

        return [option.Id];
    }

    private void RaiseProjectStatePropertiesChanged()
    {
        OnPropertyChanged(nameof(HasVisibleProjects));
        OnPropertyChanged(nameof(CanShowLoadingState));
        OnPropertyChanged(nameof(CanShowEmptyState));
        OnPropertyChanged(nameof(CanShowLoadErrorState));
        OnPropertyChanged(nameof(HasLoadErrorMessage));
        OnPropertyChanged(nameof(HasPartialWarningMessage));
        OnPropertyChanged(nameof(CanShowLoadMoreState));
    }

    private void RaiseInstallTargetStatePropertiesChanged()
    {
        OnPropertyChanged(nameof(CanShowInstallTargetsLoadingState));
        OnPropertyChanged(nameof(HasInstallTargetsLoadErrorMessage));
        OnPropertyChanged(nameof(CanShowInstallTargetsLoadErrorState));
    }

    private void RaiseAvailableVersionStatePropertiesChanged()
    {
        OnPropertyChanged(nameof(CanShowAvailableVersionsLoadingState));
        OnPropertyChanged(nameof(CanShowAvailableVersionsEmptyState));
        OnPropertyChanged(nameof(HasAvailableVersionsLoadErrorMessage));
        OnPropertyChanged(nameof(CanShowAvailableVersionsLoadErrorState));
        OnPropertyChanged(nameof(CanShowAvailableVersionsLoadMoreState));
        OnPropertyChanged(nameof(AvailableVersionsEmptyMessage));
    }

    private static string ResolveVersionFileNameForDisplay(ResourcesModVersionItemViewModel item)
    {
        return string.IsNullOrWhiteSpace(item.Version.FileName)
            ? item.Title
            : item.Version.FileName;
    }

    private static bool IsUnknownInstanceVersionTarget(ResourcesModInstallTargetItemViewModel? target)
    {
        return target?.IsLocalDownload is false
            && string.IsNullOrWhiteSpace(target.Instance?.MinecraftVersion);
    }

    private bool ShouldGroupAvailableVersionsByCompatibility()
    {
        return true;
    }

    private void AddInitialAvailableVersionsHeader()
    {
        VisibleAvailableVersionCount = 0;
        AvailableVersionListItems.Add(new ResourcesModVersionListHeaderItem(AvailableVersionsTitle));
    }

    private void ClearAvailableVersionState(bool resetFilters)
    {
        availableVersionSourceVersions = [];
        AvailableVersionListItems.Clear();
        VisibleAvailableVersionCount = 0;
        HasMoreAvailableVersions = false;
        IsLoadingMoreAvailableVersions = false;
        AvailableVersionsLoadMoreMessage = string.Empty;
        nextAvailableVersionOffset = 0;
        loadedAvailableVersionIds.Clear();

        if (!resetFilters)
            return;

        try
        {
            isApplyingAvailableVersionFilterOptions = true;
            AvailableVersionSearchQuery = string.Empty;
            ResetAvailableVersionFilterOptions();
        }
        finally
        {
            isApplyingAvailableVersionFilterOptions = false;
        }

        OnPropertyChanged(nameof(AvailableVersionsEmptyMessage));
    }

    private void ResetAvailableVersionFilterOptions()
    {
        AvailableVersionFilterOptions.Clear();
        AvailableVersionFilterOptions.Add(CreateAllAvailableVersionFilterOption());
        AvailableLoaderFilterOptions.Clear();
        foreach (var option in CreateDefaultAvailableLoaderFilterOptions())
            AvailableLoaderFilterOptions.Add(option);

        SelectedAvailableVersionFilterOption = AvailableVersionFilterOptions[0];
        SelectedAvailableLoaderFilterOption = AvailableLoaderFilterOptions[0];
    }

    private void UpdateAvailableVersionFilterOptionsPreservingSelection(IReadOnlyList<ResourceProjectVersion> versions)
    {
        var selectedVersionId = SelectedAvailableVersionFilterOption?.Id ?? "all";
        var selectedLoaderId = SelectedAvailableLoaderFilterOption?.Id ?? "all";
        var versionOptions = CreateAvailableVersionFilterOptions(versions);
        var loaderOptions = CreateAvailableLoaderFilterOptions(versions);

        try
        {
            isApplyingAvailableVersionFilterOptions = true;

            AvailableVersionFilterOptions.Clear();
            AvailableVersionFilterOptions.Add(CreateAllAvailableVersionFilterOption());
            foreach (var option in versionOptions)
                AvailableVersionFilterOptions.Add(option);

            AvailableLoaderFilterOptions.Clear();
            foreach (var option in loaderOptions)
                AvailableLoaderFilterOptions.Add(option);

            EnsureAvailableVersionFilterOption(selectedVersionId);
            EnsureAvailableLoaderFilterOption(selectedLoaderId);
            SelectedAvailableVersionFilterOption = AvailableVersionFilterOptions.FirstOrDefault(
                option => string.Equals(option.Id, selectedVersionId, StringComparison.OrdinalIgnoreCase)) ?? AvailableVersionFilterOptions[0];
            SelectedAvailableLoaderFilterOption = AvailableLoaderFilterOptions.FirstOrDefault(
                option => string.Equals(option.Id, selectedLoaderId, StringComparison.OrdinalIgnoreCase)) ?? AvailableLoaderFilterOptions[0];
        }
        finally
        {
            isApplyingAvailableVersionFilterOptions = false;
        }
    }

    private void ApplyAvailableVersionFilterOptions(
        IReadOnlyList<ResourceProjectVersion> versions,
        ResourcesModInstallTargetItemViewModel? target)
    {
        var selectedVersionId = ResolveDefaultAvailableVersionFilterId(target);
        var selectedLoaderId = ResolveDefaultAvailableLoaderFilterId(target);
        var versionOptions = CreateAvailableVersionFilterOptions(versions);
        var loaderOptions = CreateAvailableLoaderFilterOptions(versions);

        try
        {
            isApplyingAvailableVersionFilterOptions = true;
            AvailableVersionFilterOptions.Clear();
            AvailableVersionFilterOptions.Add(CreateAllAvailableVersionFilterOption());
            foreach (var option in versionOptions)
                AvailableVersionFilterOptions.Add(option);

            AvailableLoaderFilterOptions.Clear();
            foreach (var option in loaderOptions)
                AvailableLoaderFilterOptions.Add(option);

            EnsureAvailableVersionFilterOption(selectedVersionId);
            EnsureAvailableLoaderFilterOption(selectedLoaderId);
            SelectedAvailableVersionFilterOption = AvailableVersionFilterOptions.FirstOrDefault(
                option => string.Equals(option.Id, selectedVersionId, StringComparison.OrdinalIgnoreCase)) ?? AvailableVersionFilterOptions[0];
            SelectedAvailableLoaderFilterOption = AvailableLoaderFilterOptions.FirstOrDefault(
                option => string.Equals(option.Id, selectedLoaderId, StringComparison.OrdinalIgnoreCase)) ?? AvailableLoaderFilterOptions[0];
        }
        finally
        {
            isApplyingAvailableVersionFilterOptions = false;
        }
    }

    private void EnsureAvailableVersionFilterOption(string versionId)
    {
        if (string.IsNullOrWhiteSpace(versionId)
            || string.Equals(versionId, "all", StringComparison.OrdinalIgnoreCase)
            || AvailableVersionFilterOptions.Any(option => string.Equals(option.Id, versionId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        AvailableVersionFilterOptions.Add(new ResourcesFilterOptionItem { Id = versionId, Title = versionId });
    }

    private void EnsureAvailableLoaderFilterOption(string loaderId)
    {
        if (string.IsNullOrWhiteSpace(loaderId)
            || string.Equals(loaderId, "all", StringComparison.OrdinalIgnoreCase)
            || AvailableLoaderFilterOptions.Any(option => string.Equals(option.Id, loaderId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        AvailableLoaderFilterOptions.Add(new ResourcesFilterOptionItem { Id = loaderId, Title = GetAvailableLoaderFilterTitle(loaderId) });
    }

    private static string ResolveDefaultAvailableVersionFilterId(ResourcesModInstallTargetItemViewModel? target)
    {
        if (target?.IsLocalDownload != false || IsUnknownInstanceVersionTarget(target))
            return "all";

        var minecraftVersion = target.Instance?.MinecraftVersion?.Trim();
        return string.IsNullOrWhiteSpace(minecraftVersion) ? "all" : minecraftVersion;
    }

    private static string ResolveDefaultAvailableLoaderFilterId(ResourcesModInstallTargetItemViewModel? target)
    {
        if (target?.IsLocalDownload != false || IsUnknownInstanceVersionTarget(target) || target.Instance is null)
            return "all";

        return target.Instance.Loader switch
        {
            LoaderKind.Fabric => "fabric",
            LoaderKind.Forge => "forge",
            LoaderKind.NeoForge => "neoforge",
            LoaderKind.Quilt => "quilt",
            _ => "all"
        };
    }

    private static IReadOnlyList<ResourcesFilterOptionItem> CreateAvailableVersionFilterOptions(IReadOnlyList<ResourceProjectVersion> versions)
    {
        return versions
            .SelectMany(version => NormalizeGameVersionCompatibilityValues(version.GameVersions))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(version => new ResourcesFilterOptionItem { Id = version, Title = version })
            .ToList();
    }

    private static IReadOnlyList<ResourcesFilterOptionItem> CreateAvailableLoaderFilterOptions(IReadOnlyList<ResourceProjectVersion> versions)
    {
        var options = CreateDefaultAvailableLoaderFilterOptions();
        var loaderIds = versions
            .SelectMany(ResolveCompatibilityLoaders)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(loader => !string.Equals(loader, "all", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var loaderId in loaderIds)
            options.Add(new ResourcesFilterOptionItem { Id = loaderId, Title = GetAvailableLoaderFilterTitle(loaderId) });

        return options
            .DistinctBy(option => option.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ResourcesFilterOptionItem CreateAllAvailableVersionFilterOption()
    {
        return new ResourcesFilterOptionItem { Id = "all", Title = Strings.Resources_ModFilterAllVersions };
    }

    private static List<ResourcesFilterOptionItem> CreateDefaultAvailableLoaderFilterOptions()
    {
        return
        [
            new ResourcesFilterOptionItem { Id = "all", Title = Strings.Resources_ModFilterAllLoaders }
        ];
    }

    private static string GetAvailableLoaderFilterTitle(string loaderId)
    {
        return loaderId switch
        {
            "fabric" => Strings.Download_FabricLoaderTitle,
            "forge" => Strings.Download_ForgeLoaderTitle,
            "neoforge" => Strings.Download_NeoForgeLoaderTitle,
            "quilt" => Strings.Download_QuiltLoaderTitle,
            _ => loaderId
        };
    }

    private void RebuildAvailableVersionListItems(bool playEntranceAnimation = true)
    {
        AvailableVersionListItems.Clear();
        VisibleAvailableVersionCount = 0;

        var filteredVersions = availableVersionSourceVersions
            .Where(MatchesAvailableVersionFilters)
            .ToList();

        if (ShouldGroupAvailableVersionsByCompatibility())
            AddGroupedAvailableVersionItems(filteredVersions);
        else
            AddFlatAvailableVersionItems(filteredVersions);

        if (playEntranceAnimation)
            AvailableVersionListEntranceAnimationToken++;

        UpdateAvailableVersionFooterItem();
        RaiseAvailableVersionStatePropertiesChanged();
        OnPropertyChanged(nameof(AvailableVersionsEmptyMessage));
    }

    private void AppendAvailableVersionListItems(IReadOnlyList<ResourceProjectVersion> versions)
    {
        RemoveAvailableVersionFooterItem();

        var filteredVersions = versions
            .Where(MatchesAvailableVersionFilters)
            .ToList();

        if (filteredVersions.Count == 0)
        {
            UpdateAvailableVersionFooterItem();
            RaiseAvailableVersionStatePropertiesChanged();
            OnPropertyChanged(nameof(AvailableVersionsEmptyMessage));
            return;
        }

        if (ShouldGroupAvailableVersionsByCompatibility())
            AppendGroupedAvailableVersionItems(filteredVersions);
        else
            AppendFlatAvailableVersionItems(filteredVersions);

        UpdateAvailableVersionFooterItem();
        RaiseAvailableVersionStatePropertiesChanged();
        OnPropertyChanged(nameof(AvailableVersionsEmptyMessage));
    }

    private void UpdateAvailableVersionFooterItem()
    {
        RemoveAvailableVersionFooterItem();
        if (!CanShowAvailableVersionsLoadMoreState
            || string.IsNullOrWhiteSpace(AvailableVersionsLoadMoreMessage))
        {
            return;
        }

        AvailableVersionListItems.Add(new ResourcesListFooterStatusItem(AvailableVersionsLoadMoreMessage));
    }

    private void RemoveAvailableVersionFooterItem()
    {
        for (var index = AvailableVersionListItems.Count - 1; index >= 0; index--)
        {
            if (AvailableVersionListItems[index] is ResourcesListFooterStatusItem)
                AvailableVersionListItems.RemoveAt(index);
        }
    }

    private bool MatchesAvailableVersionFilters(ResourceProjectVersion version)
    {
        return MatchesAvailableVersionSearch(version)
            && MatchesAvailableVersionFilter(version)
            && MatchesAvailableLoaderFilter(version);
    }

    private bool MatchesAvailableVersionSearch(ResourceProjectVersion version)
    {
        var query = AvailableVersionSearchQuery.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return ContainsSearchText(version.Name, query)
            || ContainsSearchText(version.VersionNumber, query)
            || ContainsSearchText(version.FileName, query)
            || ContainsSearchText(version.VersionType, query);
    }

    private bool MatchesAvailableVersionFilter(ResourceProjectVersion version)
    {
        var selectedVersion = SelectedAvailableVersionFilterOption?.Id;
        return MatchesAvailableVersionFilter(version, selectedVersion);
    }

    private static bool MatchesAvailableVersionFilter(ResourceProjectVersion version, string? selectedVersion)
    {
        if (string.IsNullOrWhiteSpace(selectedVersion)
            || string.Equals(selectedVersion, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return NormalizeGameVersionCompatibilityValues(version.GameVersions)
            .Any(versionId => string.Equals(versionId, selectedVersion, StringComparison.OrdinalIgnoreCase));
    }

    private bool MatchesAvailableLoaderFilter(ResourceProjectVersion version)
    {
        var selectedLoader = SelectedAvailableLoaderFilterOption?.Id;
        return MatchesAvailableLoaderFilter(version, selectedLoader);
    }

    private static bool MatchesAvailableLoaderFilter(ResourceProjectVersion version, string? selectedLoader)
    {
        if (string.IsNullOrWhiteSpace(selectedLoader)
            || string.Equals(selectedLoader, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ResolveCompatibilityLoaders(version)
            .Any(loader => string.Equals(loader, selectedLoader, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsSearchText(string value, string query)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void AddFlatAvailableVersionItems(IReadOnlyList<ResourceProjectVersion> versions)
    {
        AvailableVersionListItems.Add(new ResourcesModVersionListHeaderItem(AvailableVersionsTitle));
        foreach (var version in versions)
        {
            AvailableVersionListItems.Add(new ResourcesModVersionItemViewModel(version, SelectedProject));
            VisibleAvailableVersionCount++;
        }
    }

    private void AppendFlatAvailableVersionItems(IReadOnlyList<ResourceProjectVersion> versions)
    {
        if (!AvailableVersionListItems.OfType<ResourcesModVersionListHeaderItem>().Any())
            AvailableVersionListItems.Add(new ResourcesModVersionListHeaderItem(AvailableVersionsTitle));

        foreach (var version in versions)
        {
            AvailableVersionListItems.Add(new ResourcesModVersionItemViewModel(version, SelectedProject));
            VisibleAvailableVersionCount++;
        }
    }

    private void AddGroupedAvailableVersionItems(IReadOnlyList<ResourceProjectVersion> versions)
    {
        var groups = new List<AvailableVersionCompatibilityGroup>();
        var groupsByTitle = new Dictionary<string, AvailableVersionCompatibilityGroup>(StringComparer.OrdinalIgnoreCase);

        foreach (var version in versions)
        {
            foreach (var title in CreateFilteredCompatibilityGroupTitles(version))
            {
                if (!groupsByTitle.TryGetValue(title, out var group))
                {
                    group = new AvailableVersionCompatibilityGroup(title);
                    groupsByTitle.Add(title, group);
                    groups.Add(group);
                }

                group.Versions.Add(version);
            }
        }

        if (groups.Count == 0)
        {
            AvailableVersionListItems.Add(new ResourcesModVersionListHeaderItem(AvailableVersionsTitle));
            return;
        }

        foreach (var group in groups)
        {
            AvailableVersionListItems.Add(new ResourcesModVersionListHeaderItem(group.Title));
            foreach (var version in group.Versions)
            {
                AvailableVersionListItems.Add(new ResourcesModVersionItemViewModel(version, SelectedProject));
                VisibleAvailableVersionCount++;
            }
        }
    }

    private void AppendGroupedAvailableVersionItems(IReadOnlyList<ResourceProjectVersion> versions)
    {
        RemoveEmptyAvailableVersionsPlaceholderHeader();

        foreach (var version in versions)
        {
            foreach (var title in CreateFilteredCompatibilityGroupTitles(version))
            {
                var insertIndex = FindAvailableVersionGroupInsertIndex(title);
                AvailableVersionListItems.Insert(insertIndex, new ResourcesModVersionItemViewModel(version, SelectedProject));
                VisibleAvailableVersionCount++;
            }
        }
    }

    private int FindAvailableVersionGroupInsertIndex(string title)
    {
        for (var index = 0; index < AvailableVersionListItems.Count; index++)
        {
            if (AvailableVersionListItems[index] is not ResourcesModVersionListHeaderItem header
                || !string.Equals(header.Title, title, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var insertIndex = index + 1;
            while (insertIndex < AvailableVersionListItems.Count
                && AvailableVersionListItems[insertIndex] is not ResourcesModVersionListHeaderItem)
            {
                insertIndex++;
            }

            return insertIndex;
        }

        AvailableVersionListItems.Add(new ResourcesModVersionListHeaderItem(title));
        return AvailableVersionListItems.Count;
    }

    private void RemoveEmptyAvailableVersionsPlaceholderHeader()
    {
        if (VisibleAvailableVersionCount == 0
            && AvailableVersionListItems.Count == 1
            && AvailableVersionListItems[0] is ResourcesModVersionListHeaderItem header
            && string.Equals(header.Title, AvailableVersionsTitle, StringComparison.OrdinalIgnoreCase))
        {
            AvailableVersionListItems.Clear();
        }
    }

    private IEnumerable<string> CreateFilteredCompatibilityGroupTitles(ResourceProjectVersion version)
    {
        var gameVersions = NormalizeGameVersionCompatibilityValues(version.GameVersions);
        var loaders = ResolveCompatibilityLoaders(version);
        var selectedVersion = SelectedAvailableVersionFilterOption?.Id;
        var selectedLoader = SelectedAvailableLoaderFilterOption?.Id;

        if (!string.IsNullOrWhiteSpace(selectedVersion)
            && !string.Equals(selectedVersion, "all", StringComparison.OrdinalIgnoreCase))
        {
            gameVersions = gameVersions
                .Where(gameVersion => string.Equals(gameVersion, selectedVersion, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(selectedLoader)
            && !string.Equals(selectedLoader, "all", StringComparison.OrdinalIgnoreCase))
        {
            loaders = loaders
                .Where(loader => string.Equals(loader, selectedLoader, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        foreach (var gameVersion in gameVersions)
        {
            foreach (var loader in loaders)
                yield return $"{gameVersion}-{loader}";
        }
    }

    private static IEnumerable<string> CreateCompatibilityGroupTitles(ResourceProjectVersion version)
    {
        var gameVersions = NormalizeGameVersionCompatibilityValues(version.GameVersions);
        var loaders = ResolveCompatibilityLoaders(version);

        foreach (var gameVersion in gameVersions)
        {
            foreach (var loader in loaders)
                yield return $"{gameVersion}-{loader}";
        }
    }

    private static IReadOnlyList<string> NormalizeGameVersionCompatibilityValues(IReadOnlyList<string> values)
    {
        var normalized = values
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Where(IsMinecraftVersionLike)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalized.Count == 0 ? [Strings.Resources_ModVersionsUnknown] : normalized;
    }

    private static IReadOnlyList<string> ResolveCompatibilityLoaders(ResourceProjectVersion version)
    {
        var loaders = NormalizeLoaderCompatibilityValues(version.Loaders);
        if (loaders.Count > 0)
            return loaders;

        loaders = NormalizeLoaderCompatibilityValues(version.GameVersions);
        if (loaders.Count > 0)
            return loaders;

        loaders = InferLoadersFromVersionText(version);
        return loaders.Count == 0 ? [Strings.Resources_ModLoadersUnknown] : loaders;
    }

    private static IReadOnlyList<string> NormalizeLoaderCompatibilityValues(IReadOnlyList<string> values)
    {
        return values
            .Select(value => TryNormalizeLoaderId(value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    private static IReadOnlyList<string> InferLoadersFromVersionText(ResourceProjectVersion version)
    {
        var text = string.Join(
            ' ',
            version.FileName,
            version.Name,
            version.VersionNumber);

        var loaders = new List<string>();
        AddLoaderIfFound(text, "neoforge", loaders);
        AddLoaderIfFound(text, "fabric", loaders);
        AddLoaderIfFound(text, "forge", loaders);
        AddLoaderIfFound(text, "quilt", loaders);
        return loaders;
    }

    private static void AddLoaderIfFound(string text, string loader, ICollection<string> loaders)
    {
        if (ContainsLoaderToken(text, loader)
            && !loaders.Contains(loader, StringComparer.OrdinalIgnoreCase))
        {
            loaders.Add(loader);
        }
    }

    private static bool ContainsLoaderToken(string text, string loader)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var index = text.IndexOf(loader, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var before = index == 0 ? '\0' : text[index - 1];
            var afterIndex = index + loader.Length;
            var after = afterIndex >= text.Length ? '\0' : text[afterIndex];
            if (!char.IsLetterOrDigit(before) && !char.IsLetterOrDigit(after))
                return true;

            index = text.IndexOf(loader, index + loader.Length, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsMinecraftVersionLike(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || !char.IsDigit(trimmed[0]))
            return false;

        return trimmed.All(character =>
            char.IsLetterOrDigit(character)
            || character is '.' or '-' or '_');
    }

    private static string? TryNormalizeLoaderId(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "fabric" => "fabric",
            "forge" => "forge",
            "neoforge" => "neoforge",
            "quilt" => "quilt",
            _ => null
        };
    }

    private static string FormatAvailableVersionsTitle(ResourcesModInstallTargetItemViewModel? target)
    {
        if (target?.IsLocalDownload != false || IsUnknownInstanceVersionTarget(target))
            return Strings.Resources_ModVersionsAllTitle;

        if (target.Instance is not { } instance)
            return Strings.Resources_ModVersionsAllTitle;

        var minecraftVersion = instance.MinecraftVersion?.Trim();
        if (string.IsNullOrWhiteSpace(minecraftVersion))
            return Strings.Resources_ModVersionsAllTitle;

        return $"{minecraftVersion}-{GetLoaderId(instance.Loader)}";
    }

    private static string GetLoaderId(LoaderKind loader)
    {
        return loader switch
        {
            LoaderKind.Fabric => "fabric",
            LoaderKind.Forge => "forge",
            LoaderKind.NeoForge => "neoforge",
            LoaderKind.Quilt => "quilt",
            _ => "vanilla"
        };
    }

    private void ShowProjectVersionFileExistsDialog(ResourcesModVersionItemViewModel item)
    {
        uiDispatcher.Invoke(() =>
        {
            ProjectVersionFileExistsDialogMessage = string.Format(
                Strings.Resources_ModDownloadFileExistsMessageFormat,
                ResolveVersionFileNameForDisplay(item));
            IsProjectVersionFileExistsDialogOpen = true;
        });
    }

    private void LogFilterSelection(string filterId, ResourcesFilterOptionItem? option)
    {
        if (option is null)
            return;

        logger?.LogInformation(
            "Resources mod filter selected. FilterId={FilterId}, OptionId={OptionId}",
            filterId,
            option.Id);
    }

    private DownloadTaskItem? BeginModDownloadTask(ResourcesModVersionItemViewModel item, string subtitle)
    {
        var task = downloadTasksPage?.BeginTask(item.Title, subtitle);
        task?.Report(new LauncherProgress(
            ModProgressStages.DownloadingFile,
            string.Format(Strings.Status_ModDownloadingFormat, item.Title)));
        return task;
    }

    private void CompleteModDownloadTask(DownloadTaskItem? task, string message)
    {
        if (task is null)
            return;

        uiDispatcher.Invoke(() => task.Complete(message));
    }

    private void FailModDownloadTask(DownloadTaskItem? task, string message)
    {
        if (task is null)
            return;

        uiDispatcher.Invoke(() => task.Fail(message));
    }

    private bool CompleteCanceledModDownloadTask(DownloadTaskItem? task)
    {
        return task?.IsCancellationRequested == true;
    }

    private void ReportStatus(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            statusService?.Report(message);
    }

    private static void ApplyPendingOption(
        ResourcesFilterOptionItem? pendingValue,
        ResourcesFilterOptionItem? currentValue,
        Action<ResourcesFilterOptionItem?> apply)
    {
        if (!ReferenceEquals(pendingValue, currentValue))
            apply(pendingValue);
    }

    private void ClearPendingFilterOptions()
    {
        PendingVersionOption = null;
        PendingLoaderOption = null;
        PendingSourceOption = null;
        PendingTypeOption = null;
    }

    private enum ProjectLoadKind
    {
        Refresh,
        LoadMore
    }

    private sealed record ProjectLoadResult(
        ResourceCatalogSearchResult CatalogResult,
        IReadOnlyList<ResourcesModProjectItemViewModel> Items,
        ProjectLoadKind LoadKind,
        int Offset);

    private sealed record ReleaseVersionData(
        IReadOnlyList<string> ReleaseVersionOrder,
        IReadOnlyList<ResourcesFilterOptionItem> VersionOptions);

    private sealed record AvailableVersionPageResult(
        ResourceProjectVersionsResult Result,
        IReadOnlyList<ResourceProjectVersion> Versions);

    private sealed class AvailableVersionCompatibilityGroup
    {
        public AvailableVersionCompatibilityGroup(string title)
        {
            Title = title;
        }

        public string Title { get; }

        public List<ResourceProjectVersion> Versions { get; } = [];
    }
}
