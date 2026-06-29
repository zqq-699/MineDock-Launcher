using System.IO;
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

public partial class ResourcesModPageViewModel : ResourcesSectionViewModelBase
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
    private readonly ILocalModpackImportService? localModpackImportService;
    private readonly IModService? modService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger? logger;
    private readonly ResourcesOnlineProjectPageOptions options;
    private readonly object releaseVersionOrderGate = new();
    private readonly Stack<ResourcesModProjectItemViewModel> projectDetailsBackStack = new();
    private CancellationTokenSource? refreshCancellationTokenSource;
    private CancellationTokenSource? installTargetsCancellationTokenSource;
    private CancellationTokenSource? projectVersionsCancellationTokenSource;
    private CancellationTokenSource? projectDependenciesCancellationTokenSource;
    private bool hasRequestedInitialProjectLoad;
    private bool isApplyingVersionFilterOptions;
    private bool isApplyingInstanceFilters;
    private bool isApplyingAvailableVersionFilterOptions;
    private IReadOnlyList<ResourceProjectVersion> availableVersionSourceVersions = [];
    private readonly HashSet<string> loadedAvailableVersionIds = new(StringComparer.OrdinalIgnoreCase);
    private int nextAvailableVersionOffset;
    private Task<ReleaseVersionData?>? releaseVersionDataTask;
    private TaskCompletionSource<RequiredDependenciesDialogChoice>? pendingRequiredDependenciesDialogChoiceSource;

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
        DownloadTasksPageViewModel? downloadTasksPage = null,
        ILocalModpackImportService? localModpackImportService = null,
        IModService? modService = null)
        : this(
            parent,
            CreateModOptions(),
            resourceCatalogService,
            logger,
            uiDispatcher,
            gameVersionService,
            gameInstanceService,
            statusService,
            filePickerService,
            floatingMessageService,
            downloadTasksPage,
            localModpackImportService,
            modService)
    {
    }

    protected ResourcesModPageViewModel(
        ResourcesPageViewModel parent,
        ResourcesOnlineProjectPageOptions options,
        IResourceCatalogService? resourceCatalogService = null,
        ILogger? logger = null,
        IUiDispatcher? uiDispatcher = null,
        IGameVersionService? gameVersionService = null,
        IGameInstanceService? gameInstanceService = null,
        IStatusService? statusService = null,
        IFilePickerService? filePickerService = null,
        IFloatingMessageService? floatingMessageService = null,
        DownloadTasksPageViewModel? downloadTasksPage = null,
        ILocalModpackImportService? localModpackImportService = null,
        IModService? modService = null)
        : base(parent, options.Title)
    {
        this.options = options;
        this.resourceCatalogService = resourceCatalogService;
        this.gameVersionService = gameVersionService;
        this.gameInstanceService = gameInstanceService;
        this.statusService = statusService;
        this.filePickerService = filePickerService;
        this.floatingMessageService = floatingMessageService;
        this.downloadTasksPage = downloadTasksPage;
        this.localModpackImportService = localModpackImportService;
        this.modService = modService;
        this.uiDispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;
        this.logger = logger;

        VersionOptions = [new ResourcesFilterOptionItem { Id = "all", Title = options.AllVersionsText }];
        LoaderOptions = CreateLoaderOptions(options);
        SourceOptions = CreateSourceOptions(options);
        TypeOptions = CreateTypeOptions(options);
        AvailableVersionFilterOptions = [CreateAllAvailableVersionFilterOption()];
        AvailableLoaderFilterOptions = [.. CreateDefaultAvailableLoaderFilterOptions()];

        selectedVersionOption = VersionOptions[0];
        selectedLoaderOption = LoaderOptions[0];
        selectedSourceOption = SourceOptions[0];
        selectedTypeOption = TypeOptions[0];
        selectedAvailableVersionFilterOption = AvailableVersionFilterOptions[0];
        selectedAvailableLoaderFilterOption = AvailableLoaderFilterOptions[0];
    }

    public event EventHandler<GameInstance>? ModpackImported;

    public event EventHandler<ResourcesModpackManualDownloadsRequestedEventArgs>? ModpackManualDownloadsRequested;

    public ObservableCollection<ResourcesFilterOptionItem> VersionOptions { get; }

    public ObservableCollection<ResourcesFilterOptionItem> LoaderOptions { get; }

    public ObservableCollection<ResourcesFilterOptionItem> SourceOptions { get; }

    public ObservableCollection<ResourcesFilterOptionItem> TypeOptions { get; }

    public ObservableCollection<ResourcesFilterOptionItem> AvailableVersionFilterOptions { get; }

    public ObservableCollection<ResourcesFilterOptionItem> AvailableLoaderFilterOptions { get; }

    public bool ShowsLoaderFilters => options.ShowsLoaderFilters;

    public bool ShowsSourceFilters => SourceOptions.Count > 1;

    public string ProjectsLoadingMessage => options.ProjectsLoadingText;

    public string DetailsInfoSectionText => options.DetailsInfoSectionText;

    public string InstallTargetSectionText => options.InstallTargetSectionText;

    public string InstallTargetsLoadingMessage => options.InstallTargetsLoadingText;

    public string VersionsLoadingMessage => options.VersionsLoadingText;

    public ObservableCollection<ResourcesModProjectItemViewModel> VisibleProjects { get; } = [];

    public ObservableCollection<object> ProjectListItems { get; } = [];

    public ObservableCollection<ResourcesModInstallTargetItemViewModel> InstallTargets { get; } = [];

    public ObservableCollection<ResourcesModVersionItemViewModel> AvailableVersions { get; } = [];

    public ObservableCollection<object> AvailableVersionListItems { get; } = [];

    public ObservableCollection<ResourcesModProjectItemViewModel> RequiredDependencies { get; } = [];

    public ObservableCollection<ResourcesModDependencyRequirementItemViewModel> RequiredDependencyDialogItems { get; } = [];

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
    private bool isRequiredDependenciesDialogOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowInstallTargetsLoadingState))]
    [NotifyPropertyChangedFor(nameof(CanShowInstallTargetsLoadErrorState))]
    private bool isLoadingInstallTargets;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowRequiredDependencies))]
    private bool isLoadingProjectDependencies;

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

    public string EmptyMessage => options.ProjectsEmptyText;

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

    public bool CanShowRequiredDependencies => RequiredDependencies.Count > 0;

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
        ? options.VersionsFilterEmptyText
        : SelectedInstallTarget?.IsLocalDownload == true || IsUnknownInstanceVersionTarget(SelectedInstallTarget)
            ? options.VersionsEmptyLocalText
            : options.VersionsEmptyText;

    public bool HasAvailableVersionFilters => !string.IsNullOrWhiteSpace(AvailableVersionSearchQuery)
        || (SelectedAvailableVersionFilterOption?.Id is { } versionId
            && !string.Equals(versionId, "all", StringComparison.OrdinalIgnoreCase))
        || (options.ShowsLoaderFilters
            && SelectedAvailableLoaderFilterOption?.Id is { } loaderId
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
        logger?.LogDebug("Resource project filter dialog opened. Kind={Kind}", options.Kind);
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
    private void CancelRequiredDependenciesDialog()
    {
        ResolveRequiredDependenciesDialog(RequiredDependenciesDialogChoice.Cancel);
    }

    [RelayCommand]
    private void ContinueWithoutRequiredDependencies()
    {
        ResolveRequiredDependenciesDialog(RequiredDependenciesDialogChoice.ContinueWithoutDependencies);
    }

    [RelayCommand]
    private void AutoInstallRequiredDependencies()
    {
        ResolveRequiredDependenciesDialog(RequiredDependenciesDialogChoice.AutoInstallDependencies);
    }

    [RelayCommand]
    private void SelectProject(ResourcesModProjectItemViewModel? project)
    {
        if (project is null)
            return;

        projectDetailsBackStack.Clear();
        OpenProjectDetails(project);
    }

    [RelayCommand]
    private void OpenDependencyProject(ResourcesModProjectItemViewModel? project)
    {
        if (project is null)
            return;

        if (SelectedProject is not null)
            projectDetailsBackStack.Push(SelectedProject);

        OpenProjectDetails(project);
        logger?.LogInformation(
            "Resource project dependency selected. Kind={Kind}, Source={Source}, ProjectId={ProjectId}",
            options.Kind,
            project.Project.Source,
            project.Project.ProjectId);
    }

    private void OpenProjectDetails(ResourcesModProjectItemViewModel project)
    {
        SelectedProject = project;
        CurrentStep = ResourcesModPageStep.ProjectDetails;
        SelectedInstallTarget = null;
        AvailableVersions.Clear();
        AvailableVersionListItems.Clear();
        ClearAvailableVersionState(resetFilters: true);
        ClearRequiredDependencies();
        AvailableVersionsLoadErrorMessage = string.Empty;
        _ = LoadProjectDependenciesAsync(project);
        _ = LoadInstallTargetsAsync();
        logger?.LogInformation(
            "Resource project selected. Kind={Kind}, Source={Source}, ProjectId={ProjectId}",
            options.Kind,
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

        if (CurrentStep is ResourcesModPageStep.ProjectDetails
            && projectDetailsBackStack.TryPop(out var previousProject))
        {
            OpenProjectDetails(previousProject);
            return;
        }

        ResetToProjectList();
    }

    public void ResetToProjectList()
    {
        projectDetailsBackStack.Clear();
        ResolveRequiredDependenciesDialog(RequiredDependenciesDialogChoice.Cancel);
        CancelProjectDependenciesLoad();
        ClearRequiredDependencies();
        CurrentStep = ResourcesModPageStep.ProjectList;
    }

    public async Task LoadProjectDependenciesAsync(ResourcesModProjectItemViewModel project)
    {
        projectDependenciesCancellationTokenSource?.Cancel();
        projectDependenciesCancellationTokenSource?.Dispose();
        projectDependenciesCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = projectDependenciesCancellationTokenSource.Token;

        if (resourceCatalogService is null
            || project.Project.Kind is not ResourceProjectKind.Mod
            || project.Project.Source is not (ResourceProjectSource.Modrinth or ResourceProjectSource.CurseForge))
        {
            IsLoadingProjectDependencies = false;
            return;
        }

        IsLoadingProjectDependencies = true;

        try
        {
            var result = await resourceCatalogService.GetProjectDependenciesAsync(
                    new ResourceProjectDependenciesRequest
                    {
                        Kind = project.Project.Kind,
                        Source = project.Project.Source,
                        ProjectId = project.Project.ProjectId,
                        Slug = project.Project.Slug
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
                return;

            var dependencies = result.RequiredProjects
                .Select(dependency => new ResourcesModProjectItemViewModel(
                    dependency,
                    fallbackIconKey: options.FallbackIconKey))
                .ToList();
            uiDispatcher.Invoke(() => ApplyRequiredDependencies(project, dependencies, cancellationToken));
            logger?.LogInformation(
                "Resource project dependencies loaded. Kind={Kind}, ProjectId={ProjectId}, RequiredCount={RequiredCount}",
                options.Kind,
                project.Project.ProjectId,
                dependencies.Count);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            uiDispatcher.Invoke(ClearRequiredDependencies);
            logger?.LogError(
                exception,
                "Failed to load resource project dependencies. Kind={Kind}, Source={Source}, ProjectId={ProjectId}",
                options.Kind,
                project.Project.Source,
                project.Project.ProjectId);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                uiDispatcher.Invoke(() => IsLoadingProjectDependencies = false);
        }
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
        if (target.IsNewInstanceInstall)
        {
            logger?.LogInformation(
                "Resource modpack new instance install target selected. Kind={Kind}, ProjectId={ProjectId}",
                options.Kind,
                SelectedProject.Project.ProjectId);
        }
        else if (target.Instance is null)
        {
            logger?.LogInformation(
                "Resource project local download target selected. Kind={Kind}, ProjectId={ProjectId}",
                options.Kind,
                SelectedProject.Project.ProjectId);
        }
        else
        {
            logger?.LogInformation(
                "Resource project install target selected. Kind={Kind}, ProjectId={ProjectId} InstanceId={InstanceId} IsUnknownMinecraftVersion={IsUnknownMinecraftVersion}",
                options.Kind,
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
            if (gameInstanceService is not null
                && options.InstallTargetMode is ResourcesOnlineProjectInstallTargetMode.ExistingInstance)
            {
                instances = await gameInstanceService.GetInstancesAsync(cancellationToken).ConfigureAwait(false);
            }

            if (cancellationToken.IsCancellationRequested)
                return;

            uiDispatcher.Invoke(() => ApplyInstallTargets(instances, loadErrorMessage: string.Empty, cancellationToken));
            logger?.LogInformation(
                "Resource project install targets loaded. Kind={Kind}, InstanceCount={InstanceCount}",
                options.Kind,
                instances.Count);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            uiDispatcher.Invoke(() => ApplyInstallTargets([], options.InstallTargetsLoadErrorText, cancellationToken));
            logger?.LogError(exception, "Failed to load resource project install targets. Kind={Kind}", options.Kind);
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
                uiDispatcher.Invoke(() => ApplyAvailableVersions([], options.VersionsLoadErrorText, hasMore: false, cancellationToken));
                return;
            }

            if (!target.IsLocalDownload && !target.IsNewInstanceInstall && instance is null)
            {
                uiDispatcher.Invoke(() => ApplyAvailableVersions([], options.VersionsLoadErrorText, hasMore: false, cancellationToken));
                return;
            }

            BeginAvailableVersionRequestState();
            var page = await LoadNextAvailableVersionPageAsync(project, cancellationToken)
                .ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
                return;

            var errorMessage = page.Result.IsCurseForgeUnavailable
                ? options.VersionsLoadErrorText
                : string.Empty;
            uiDispatcher.Invoke(() => ApplyAvailableVersions(
                page.Versions,
                errorMessage,
                hasMore: !page.Result.IsCurseForgeUnavailable && page.Result.HasMore,
                cancellationToken));
            logger?.LogInformation(
                "Resource project versions loaded. Kind={Kind}, ProjectId={ProjectId} InstanceId={InstanceId} VersionCount={VersionCount}",
                options.Kind,
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

            uiDispatcher.Invoke(() => ApplyAvailableVersions([], options.VersionsLoadErrorText, hasMore: false, cancellationToken));
            logger?.LogError(exception, "Failed to load resource project versions. Kind={Kind}", options.Kind);
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
        AvailableVersionsLoadMoreMessage = options.VersionsLoadingMoreText;
        logger?.LogInformation(
            "Loading more resource project versions. Kind={Kind}, ProjectId={ProjectId}",
            options.Kind,
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
                    "Resource project versions append failed because CurseForge is unavailable. Kind={Kind}, ProjectId={ProjectId}",
                    options.Kind,
                    project.ProjectId);
                return;
            }

            uiDispatcher.Invoke(() => ApplyMoreAvailableVersions(
                page.Versions,
                hasMore: !page.Result.IsCurseForgeUnavailable && page.Result.HasMore,
                cancellationToken));
            logger?.LogInformation(
                "Resource project versions appended. Kind={Kind}, ProjectId={ProjectId} ResultCount={ResultCount} HasMore={HasMore}",
                options.Kind,
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
                "Failed to load more resource project versions. Kind={Kind}, ProjectId={ProjectId}",
                options.Kind,
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
                var targetDirectory = filePickerService?.PickFolder(options.DownloadDirectoryPickerTitle);
                if (string.IsNullOrWhiteSpace(targetDirectory))
                    return;

                if (await resourceCatalogService.ProjectVersionDownloadExistsAsync(item.Version, targetDirectory).ConfigureAwait(false))
                {
                    ShowProjectVersionFileExistsDialog(item);
                    logger?.LogInformation(
                        "Skipped resource project local download because target file exists. Kind={Kind}, ProjectId={ProjectId} VersionId={VersionId} TargetDirectory={TargetDirectory}",
                        options.Kind,
                        SelectedProject?.Project.ProjectId,
                        item.Version.VersionId,
                        targetDirectory);
                    return;
                }

                downloadTask = BeginModDownloadTask(item, targetDirectory);
                floatingMessageService?.Show(options.DownloadingText);
                ReportStatus(string.Format(options.DownloadingFormat, item.Title));
                await resourceCatalogService.DownloadProjectVersionAsync(
                        item.Version,
                        targetDirectory,
                        downloadTask?.CancellationToken ?? CancellationToken.None)
                    .ConfigureAwait(false);
                if (CompleteCanceledModDownloadTask(downloadTask))
                    return;

                var downloadedMessage = string.Format(options.DownloadedFormat, ResolveVersionFileNameForDisplay(item));
                CompleteModDownloadTask(downloadTask, downloadedMessage);
                ReportStatus(downloadedMessage);
                logger?.LogInformation(
                    "Resource project version downloaded locally. Kind={Kind}, ProjectId={ProjectId} VersionId={VersionId} TargetDirectory={TargetDirectory}",
                    options.Kind,
                    SelectedProject?.Project.ProjectId,
                    item.Version.VersionId,
                    targetDirectory);
                return;
            }

            if (target.IsNewInstanceInstall)
            {
                if (localModpackImportService is null)
                    throw new InvalidOperationException("The local modpack import service is unavailable.");

                downloadTask = BeginModDownloadTask(item, target.Title);
                floatingMessageService?.Show(options.DownloadingText);
                ReportStatus(string.Format(options.DownloadingFormat, item.Title));

                var tempDirectory = Path.Combine(Path.GetTempPath(), $"launcher-modpack-install-{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDirectory);

                try
                {
                    var archivePath = await resourceCatalogService.DownloadProjectVersionAsync(
                            item.Version,
                            tempDirectory,
                            downloadTask?.CancellationToken ?? CancellationToken.None)
                        .ConfigureAwait(false);
                    if (CompleteCanceledModDownloadTask(downloadTask))
                        return;

                    var result = await localModpackImportService.ImportFromArchiveAsync(
                            archivePath,
                            CreateModpackImportProgressReporter(downloadTask),
                            downloadTask?.CancellationToken ?? CancellationToken.None)
                        .ConfigureAwait(false);
                    if (CompleteCanceledModDownloadTask(downloadTask))
                        return;

                    if (result.IsSuccess && result.ImportedInstance is not null)
                    {
                        var importedMessage = result.HasManualDownloads
                            ? string.Format(Strings.Status_ModpackImportedWithManualDownloadsFormat, result.ImportedInstance.Name)
                            : string.Format(options.InstalledFormat, result.ImportedInstance.Name);
                        CompleteModDownloadTask(downloadTask, importedMessage);
                        ReportStatus(importedMessage);
                        uiDispatcher.Invoke(() =>
                        {
                            ModpackImported?.Invoke(this, result.ImportedInstance);
                            if (result.HasManualDownloads)
                            {
                                ModpackManualDownloadsRequested?.Invoke(
                                    this,
                                    new ResourcesModpackManualDownloadsRequestedEventArgs(
                                        result.ImportedInstance,
                                        result.ManualDownloads));
                            }
                        });
                        logger?.LogInformation(
                            "Resource modpack imported as new instance. Kind={Kind}, ProjectId={ProjectId} VersionId={VersionId} InstanceId={InstanceId} HasManualDownloads={HasManualDownloads}",
                            options.Kind,
                            SelectedProject?.Project.ProjectId,
                            item.Version.VersionId,
                            result.ImportedInstance.Id,
                            result.HasManualDownloads);
                        return;
                    }

                    var failureMessage = MapModpackImportFailureMessage(result.FailureReason);
                    floatingMessageService?.Show(failureMessage);
                    ReportStatus(failureMessage);
                    FailModDownloadTask(downloadTask, failureMessage);
                    logger?.LogWarning(
                        "Resource modpack import failed. Kind={Kind}, ProjectId={ProjectId} VersionId={VersionId} FailureReason={FailureReason}",
                        options.Kind,
                        SelectedProject?.Project.ProjectId,
                        item.Version.VersionId,
                        result.FailureReason);
                    return;
                }
                finally
                {
                    SafeDeleteDirectory(tempDirectory);
                }
            }

            var instance = target.Instance;
            if (instance is null)
                return;

            if (await resourceCatalogService.ProjectVersionInstallExistsAsync(item.Version, instance).ConfigureAwait(false))
            {
                ShowProjectVersionFileExistsDialog(item);
                logger?.LogInformation(
                    "Skipped resource project version install because target file exists. Kind={Kind}, ProjectId={ProjectId} VersionId={VersionId} InstanceId={InstanceId}",
                    options.Kind,
                    SelectedProject?.Project.ProjectId,
                    item.Version.VersionId,
                    instance.Id);
                return;
            }

            var dependencyPlan = await ResolveRequiredDependencyInstallPlanAsync(item, instance).ConfigureAwait(false);
            if (dependencyPlan.Choice is RequiredDependenciesDialogChoice.Cancel)
                return;

            downloadTask = BeginModDownloadTask(item, target.Title);
            floatingMessageService?.Show(options.DownloadingText);
            if (dependencyPlan.Choice is RequiredDependenciesDialogChoice.AutoInstallDependencies)
            {
                try
                {
                    await InstallRequiredDependenciesAsync(
                            dependencyPlan.MissingDependencies,
                            instance,
                            downloadTask,
                            downloadTask?.CancellationToken ?? CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (RequiredDependencyInstallException exception)
                {
                    var failureMessage = string.Format(
                        Strings.Status_ModRequiredDependenciesAutoInstallFailedFormat,
                        exception.DependencyTitle);
                    floatingMessageService?.Show(failureMessage);
                    ReportStatus(failureMessage);
                    FailModDownloadTask(downloadTask, failureMessage);
                    logger?.LogWarning(
                        exception,
                        "Failed to auto-install required resource project dependency. Kind={Kind}, ProjectId={ProjectId}, DependencyProjectId={DependencyProjectId}, InstanceId={InstanceId}",
                        options.Kind,
                        SelectedProject?.Project.ProjectId,
                        exception.DependencyProjectId,
                        instance.Id);
                    return;
                }
            }

            ReportStatus(string.Format(options.DownloadingFormat, item.Title));
            downloadTask?.Report(new LauncherProgress(
                ModProgressStages.DownloadingFile,
                string.Format(options.DownloadingFormat, item.Title)));
            await resourceCatalogService.InstallProjectVersionAsync(
                    item.Version,
                    instance,
                    downloadTask?.CancellationToken ?? CancellationToken.None)
                .ConfigureAwait(false);
            if (CompleteCanceledModDownloadTask(downloadTask))
                return;

            var installedMessage = string.Format(options.InstalledFormat, SelectedProject?.Title ?? item.Title);
            CompleteModDownloadTask(downloadTask, installedMessage);
            ReportStatus(installedMessage);
            logger?.LogInformation(
                "Resource project version installed. Kind={Kind}, ProjectId={ProjectId} VersionId={VersionId} InstanceId={InstanceId}",
                options.Kind,
                SelectedProject?.Project.ProjectId,
                item.Version.VersionId,
                instance.Id);
        }
        catch (OperationCanceledException) when (downloadTask?.IsCancellationRequested == true)
        {
            CompleteCanceledModDownloadTask(downloadTask);
            logger?.LogInformation(
                "Resource project version download canceled. Kind={Kind}, ProjectId={ProjectId} VersionId={VersionId}",
                options.Kind,
                SelectedProject?.Project.ProjectId,
                item.Version.VersionId);
        }
        catch (Exception exception)
        {
            ReportStatus(target.IsLocalDownload ? options.DownloadFailedText : options.InstallFailedText);
            if (target.IsLocalDownload)
            {
                floatingMessageService?.Show(options.DownloadFailedText);
                FailModDownloadTask(downloadTask, options.DownloadFailedText);
                logger?.LogError(
                    exception,
                    "Failed to download resource project version locally. Kind={Kind}, ProjectId={ProjectId} VersionId={VersionId}",
                    options.Kind,
                    SelectedProject?.Project.ProjectId,
                    item.Version.VersionId);
            }
            else
            {
                floatingMessageService?.Show(options.InstallFailedText);
                FailModDownloadTask(downloadTask, options.InstallFailedText);
                logger?.LogError(
                    exception,
                    "Failed to install resource project version. Kind={Kind}, ProjectId={ProjectId} VersionId={VersionId} InstanceId={InstanceId}",
                    options.Kind,
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
            "Applied resource project filters from instance. Kind={Kind}, InstanceId={InstanceId}, MinecraftVersion={MinecraftVersion}, VersionFilter={VersionFilter}, Loader={Loader}, LoaderFilter={LoaderFilter}",
            options.Kind,
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
        UpdateProjectFooterItem();
        RaiseProjectStatePropertiesChanged();
    }

    partial void OnLoadErrorMessageChanged(string value)
    {
        RaiseProjectStatePropertiesChanged();
    }

    partial void OnLoadMoreMessageChanged(string value)
    {
        UpdateProjectFooterItem();
        RaiseProjectStatePropertiesChanged();
    }

    partial void OnHasMoreProjectsChanged(bool value)
    {
        UpdateProjectFooterItem();
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
                        var catalogResult = await resourceCatalogService.SearchProjectsAsync(request, cancellationToken).ConfigureAwait(false);
                        var minecraftReleaseVersionOrder = await releaseVersionOrderTask.ConfigureAwait(false);
                        var items = catalogResult.Projects
                            .Select(project => new ResourcesModProjectItemViewModel(project, minecraftReleaseVersionOrder, options.FallbackIconKey))
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
                logger?.LogWarning(
                    exception,
                    "Failed to start resource project version filter load. Kind={Kind}",
                    options.Kind);
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
            logger?.LogWarning(
                exception,
                "Failed to load Minecraft release versions before applying instance resource project filters. Kind={Kind}",
                options.Kind);
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
            logger?.LogWarning(
                exception,
                "Failed to load Minecraft release versions for resource project filters. Kind={Kind}",
                options.Kind);
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
        VersionOptions.Add(new ResourcesFilterOptionItem { Id = "all", Title = this.options.AllVersionsText });
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
            return allOption ?? new ResourcesFilterOptionItem { Id = "all", Title = options.AllVersionsText };

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
        LoadMoreMessage = options.ProjectsLoadingMoreText;
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
            ProjectListItems.Clear();
            PartialWarningMessage = string.Empty;
            nextPageOffset = result.Offset + CatalogPageSize;
            HasMoreProjects = result.CatalogResult.HasMore;
            LoadMoreMessage = HasMoreProjects || result.Items.Count == 0
                ? string.Empty
                : options.ProjectsNoMoreText;
            ListEntranceAnimationToken++;
            AddProjectBatch(result.Items, startIndex: 0, InitialProjectBatchSize);
            RaiseProjectStatePropertiesChanged();
            IsLoadingProjects = false;

            logger?.LogInformation(
                "Resource projects loaded. Kind={Kind}, ResultCount={ResultCount} IsCurseForgeUnavailable={IsCurseForgeUnavailable}",
                options.Kind,
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
            LoadMoreMessage = HasMoreProjects ? string.Empty : options.ProjectsNoMoreText;
            AddProjectBatch(result.Items, startIndex: 0, AppendProjectBatchSize);
            RaiseProjectStatePropertiesChanged();
            IsLoadingMoreProjects = false;

            logger?.LogInformation(
                "Resource projects appended. Kind={Kind}, ResultCount={ResultCount} HasMore={HasMore}",
                options.Kind,
                result.Items.Count,
                result.CatalogResult.HasMore);

            if (result.Items.Count == 0)
            {
                if (!HasMoreProjects)
                    LoadMoreMessage = options.ProjectsNoMoreText;
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
        ProjectListItems.Clear();
        ResetToProjectList();
        LoadErrorMessage = options.ProjectsLoadErrorText;
        LoadMoreMessage = string.Empty;
        PartialWarningMessage = string.Empty;
        IsLoadingProjects = false;
        IsLoadingMoreProjects = false;
        HasMoreProjects = false;
        logger?.LogError(exception, "Failed to load resource projects. Kind={Kind}", options.Kind);
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

        LoadMoreMessage = options.ProjectsLoadMoreErrorText;
        IsLoadingMoreProjects = false;
        logger?.LogError(exception, "Failed to load more resource projects. Kind={Kind}", options.Kind);
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
        RemoveProjectFooterItem();
        for (var index = startIndex; index < endIndex; index++)
        {
            VisibleProjects.Add(items[index]);
            ProjectListItems.Add(items[index]);
        }

        UpdateProjectFooterItem();
    }

    private void ApplyRequiredDependencies(
        ResourcesModProjectItemViewModel project,
        IReadOnlyList<ResourcesModProjectItemViewModel> dependencies,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested
            || !ReferenceEquals(SelectedProject, project))
        {
            return;
        }

        RequiredDependencies.Clear();
        foreach (var dependency in dependencies)
            RequiredDependencies.Add(dependency);

        OnPropertyChanged(nameof(CanShowRequiredDependencies));
    }

    private void ClearRequiredDependencies()
    {
        if (RequiredDependencies.Count == 0)
        {
            OnPropertyChanged(nameof(CanShowRequiredDependencies));
            return;
        }

        RequiredDependencies.Clear();
        OnPropertyChanged(nameof(CanShowRequiredDependencies));
    }

    private void CancelProjectDependenciesLoad()
    {
        projectDependenciesCancellationTokenSource?.Cancel();
        projectDependenciesCancellationTokenSource?.Dispose();
        projectDependenciesCancellationTokenSource = null;
        IsLoadingProjectDependencies = false;
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
            .ToList();
        if (options.InstallTargetMode is ResourcesOnlineProjectInstallTargetMode.NewInstance)
        {
            targets =
            [
                ResourcesModInstallTargetItemViewModel.CreateNewInstanceInstall(
                    options.InstallTargetNewInstanceText ?? options.InstallTargetSectionText),
                ResourcesModInstallTargetItemViewModel.CreateLocalDownload(options.InstallTargetLocalText)
            ];
        }
        else
        {
            targets.Add(ResourcesModInstallTargetItemViewModel.CreateLocalDownload(options.InstallTargetLocalText));
        }

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
            AvailableVersions.Add(new ResourcesModVersionItemViewModel(version, SelectedProject, options.FallbackIconKey));

        ApplyAvailableVersionFilterOptions(versions, SelectedInstallTarget);
        RebuildAvailableVersionListItems();

        AvailableVersionsLoadErrorMessage = loadErrorMessage;
        HasMoreAvailableVersions = hasMore;
        AvailableVersionsLoadMoreMessage = HasMoreAvailableVersions ? string.Empty : options.VersionsNoMoreText;
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
            AvailableVersions.Add(new ResourcesModVersionItemViewModel(version, SelectedProject, options.FallbackIconKey));

        UpdateAvailableVersionFilterOptionsPreservingSelection(availableVersionSourceVersions);
        AppendAvailableVersionListItems(versions);

        HasMoreAvailableVersions = hasMore;
        AvailableVersionsLoadMoreMessage = HasMoreAvailableVersions
            ? string.Empty
            : options.VersionsNoMoreText;
        IsLoadingMoreAvailableVersions = false;
        RaiseAvailableVersionStatePropertiesChanged();
    }

    private void FailAvailableVersionLoadMore(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        IsLoadingMoreAvailableVersions = false;
        AvailableVersionsLoadMoreMessage = options.VersionsLoadMoreErrorText;
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
            "Resource project versions page loaded. Kind={Kind}, ProjectId={ProjectId} Offset={Offset} PageSize={PageSize} ResultCount={ResultCount} UniqueCount={UniqueCount} HasMore={HasMore}",
            options.Kind,
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
            Kind = options.Kind,
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
            Kind = options.Kind,
            Query = SearchQuery,
            MinecraftVersion = selectedMinecraftVersions.Count == 1 ? selectedMinecraftVersions[0] : string.Empty,
            MinecraftVersions = selectedMinecraftVersions,
            Loader = !options.ShowsLoaderFilters ? LoaderKind.Vanilla : SelectedLoaderOption?.Id switch
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
            Category = ResolveSelectedCategory(),
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

    private ResourceProjectCategory? ResolveSelectedCategory()
    {
        var selectedId = SelectedTypeOption?.Id;
        if (string.IsNullOrWhiteSpace(selectedId) || string.Equals(selectedId, "all", StringComparison.OrdinalIgnoreCase))
            return null;

        return options.TypeOptions
            .FirstOrDefault(option => string.Equals(option.Id, selectedId, StringComparison.OrdinalIgnoreCase))
            ?.Category;
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

    private void UpdateProjectFooterItem()
    {
        RemoveProjectFooterItem();
        if (!CanShowLoadMoreState || string.IsNullOrWhiteSpace(LoadMoreMessage))
            return;

        ProjectListItems.Add(new ResourcesListFooterStatusItem(LoadMoreMessage));
    }

    private void RemoveProjectFooterItem()
    {
        for (var index = ProjectListItems.Count - 1; index >= 0; index--)
        {
            if (ProjectListItems[index] is ResourcesListFooterStatusItem)
                ProjectListItems.RemoveAt(index);
        }
    }

    private static string ResolveVersionFileNameForDisplay(ResourcesModVersionItemViewModel item)
    {
        return string.IsNullOrWhiteSpace(item.Version.FileName)
            ? item.Title
            : item.Version.FileName;
    }

    private async Task<RequiredDependencyInstallPlan> ResolveRequiredDependencyInstallPlanAsync(
        ResourcesModVersionItemViewModel item,
        GameInstance instance)
    {
        if (options.Kind is not ResourceProjectKind.Mod
            || item.Version.RequiredDependencies.Count == 0
            || modService is null)
        {
            return RequiredDependencyInstallPlan.Continue;
        }

        var installedDependencies = await LoadEnabledLocalModIdentifiersAsync(instance, CancellationToken.None)
            .ConfigureAwait(false);
        var candidates = new List<RequiredDependencyInstallCandidate>();
        var dialogItems = new List<ResourcesModDependencyRequirementItemViewModel>();
        foreach (var dependency in item.Version.RequiredDependencies)
        {
            var candidate = await TryResolveRequiredDependencyInstallCandidateAsync(dependency, instance, CancellationToken.None)
                .ConfigureAwait(false);
            var state = ResolveDependencyRequirementState(candidate, installedDependencies);
            candidates.Add(candidate);
            dialogItems.Add(new ResourcesModDependencyRequirementItemViewModel(
                dependency,
                candidate.MinimumVersion,
                candidate.InstallVersion,
                state,
                options.FallbackIconKey));
        }

        var missingDependencies = candidates
            .Where(candidate => ResolveDependencyRequirementState(candidate, installedDependencies) is not RequiredDependencyRequirementState.Installed)
            .ToList();

        if (missingDependencies.Count == 0)
        {
            logger?.LogInformation(
                "Resource project required dependencies are already installed. Kind={Kind}, ProjectId={ProjectId}, VersionId={VersionId}, RequiredCount={RequiredCount}, InstanceId={InstanceId}",
                options.Kind,
                SelectedProject?.Project.ProjectId,
                item.Version.VersionId,
                dialogItems.Count,
                instance.Id);
            return RequiredDependencyInstallPlan.Continue;
        }

        logger?.LogInformation(
            "Resource project required dependencies are missing. Kind={Kind}, ProjectId={ProjectId}, VersionId={VersionId}, MissingCount={MissingCount}, InstanceId={InstanceId}",
            options.Kind,
            SelectedProject?.Project.ProjectId,
            item.Version.VersionId,
            missingDependencies.Count,
            instance.Id);

        var choice = await RequestRequiredDependenciesDialogAsync(dialogItems).ConfigureAwait(false);
        return new RequiredDependencyInstallPlan(choice, missingDependencies);
    }

    private async Task<RequiredDependenciesDialogChoice> RequestRequiredDependenciesDialogAsync(
        IReadOnlyList<ResourcesModDependencyRequirementItemViewModel> items)
    {
        var previousSource = pendingRequiredDependenciesDialogChoiceSource;
        previousSource?.TrySetResult(RequiredDependenciesDialogChoice.Cancel);

        var completion = new TaskCompletionSource<RequiredDependenciesDialogChoice>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        pendingRequiredDependenciesDialogChoiceSource = completion;
        uiDispatcher.Invoke(() =>
        {
            RequiredDependencyDialogItems.Clear();
            foreach (var item in items)
                RequiredDependencyDialogItems.Add(item);

            IsRequiredDependenciesDialogOpen = true;
        });

        return await completion.Task.ConfigureAwait(false);
    }

    private void ResolveRequiredDependenciesDialog(RequiredDependenciesDialogChoice choice)
    {
        uiDispatcher.Invoke(() => IsRequiredDependenciesDialogOpen = false);
        pendingRequiredDependenciesDialogChoiceSource?.TrySetResult(choice);
        pendingRequiredDependenciesDialogChoiceSource = null;
    }

    private async Task InstallRequiredDependenciesAsync(
        IReadOnlyList<RequiredDependencyInstallCandidate> missingDependencies,
        GameInstance instance,
        DownloadTaskItem? downloadTask,
        CancellationToken cancellationToken)
    {
        if (missingDependencies.Count == 0)
            return;

        var installedDependencies = await LoadEnabledLocalModIdentifiersAsync(instance, cancellationToken)
            .ConfigureAwait(false);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dependency in missingDependencies)
        {
            await InstallRequiredDependencyAsync(
                    dependency,
                    instance,
                    installedDependencies,
                    visiting,
                    visited,
                    downloadTask,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task InstallRequiredDependencyAsync(
        RequiredDependencyInstallCandidate candidate,
        GameInstance instance,
        InstalledDependencyCatalog installedDependencies,
        ISet<string> visiting,
        ISet<string> visited,
        DownloadTaskItem? downloadTask,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dependency = candidate.Dependency;
        var project = dependency.Project;
        var dependencyKey = ResolveDependencyKey(project);
        if (string.IsNullOrWhiteSpace(dependencyKey)
            || visited.Contains(dependencyKey)
            || ResolveDependencyRequirementState(candidate, installedDependencies) is RequiredDependencyRequirementState.Installed)
        {
            return;
        }

        if (!visiting.Add(dependencyKey))
            return;

        try
        {
            var version = candidate.InstallVersion
                ?? throw new RequiredDependencyInstallException(project);
            foreach (var childDependency in version.RequiredDependencies)
            {
                var childCandidate = await ResolveRequiredDependencyInstallCandidateAsync(childDependency, instance, cancellationToken)
                    .ConfigureAwait(false);
                await InstallRequiredDependencyAsync(
                        childCandidate,
                        instance,
                        installedDependencies,
                        visiting,
                        visited,
                        downloadTask,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (ResolveDependencyRequirementState(candidate, installedDependencies) is RequiredDependencyRequirementState.Installed)
                return;

            var installingMessage = string.Format(
                Strings.Status_ModRequiredDependencyInstallingFormat,
                project.Title);
            ReportStatus(installingMessage);
            downloadTask?.Report(new LauncherProgress(ModProgressStages.DownloadingFile, installingMessage));
            await resourceCatalogService!.InstallProjectVersionAsync(version, instance, cancellationToken)
                .ConfigureAwait(false);
            AddDependencyIdentifiers(installedDependencies, dependency, version);
            logger?.LogInformation(
                "Resource project required dependency installed. ProjectId={ProjectId}, DependencyProjectId={DependencyProjectId}, VersionId={VersionId}, InstanceId={InstanceId}",
                SelectedProject?.Project.ProjectId,
                project.ProjectId,
                version.VersionId,
                instance.Id);
        }
        finally
        {
            visiting.Remove(dependencyKey);
            visited.Add(dependencyKey);
        }
    }

    private async Task<RequiredDependencyInstallCandidate> TryResolveRequiredDependencyInstallCandidateAsync(
        ResourceProjectDependency dependency,
        GameInstance instance,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ResolveRequiredDependencyInstallCandidateAsync(dependency, instance, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RequiredDependencyInstallException)
        {
            return new RequiredDependencyInstallCandidate(dependency, MinimumVersion: null, InstallVersion: null);
        }
    }

    private async Task<RequiredDependencyInstallCandidate> ResolveRequiredDependencyInstallCandidateAsync(
        ResourceProjectDependency dependency,
        GameInstance instance,
        CancellationToken cancellationToken)
    {
        var project = dependency.Project;
        if (resourceCatalogService is null
            || project.Source is not (ResourceProjectSource.Modrinth or ResourceProjectSource.CurseForge)
            || project.Kind is not ResourceProjectKind.Mod)
        {
            throw new RequiredDependencyInstallException(project);
        }

        var result = await resourceCatalogService.GetProjectVersionsAsync(
                new ResourceProjectVersionsRequest
                {
                    Kind = ResourceProjectKind.Mod,
                    Source = project.Source,
                    ProjectId = project.ProjectId,
                    Slug = project.Slug,
                    MinecraftVersion = instance.MinecraftVersion,
                    Loader = instance.Loader,
                    IncludeAllVersions = false,
                    Offset = 0,
                    PageSize = AvailableVersionPageSize
                },
                cancellationToken)
            .ConfigureAwait(false);
        var installVersion = SelectRequiredDependencyVersion(result.Versions)
            ?? throw new RequiredDependencyInstallException(project);
        var minimumVersion = ResolveRequiredDependencyMinimumVersion(dependency, result.Versions) ?? installVersion;
        return new RequiredDependencyInstallCandidate(dependency, minimumVersion, installVersion);
    }

    private static ResourceProjectVersion? SelectRequiredDependencyVersion(
        IReadOnlyList<ResourceProjectVersion> versions)
    {
        return versions.FirstOrDefault(version => string.Equals(
                   version.VersionType,
                   "release",
                   StringComparison.OrdinalIgnoreCase))
               ?? versions.FirstOrDefault();
    }

    private static ResourceProjectVersion? ResolveRequiredDependencyMinimumVersion(
        ResourceProjectDependency dependency,
        IReadOnlyList<ResourceProjectVersion> versions)
    {
        if (string.IsNullOrWhiteSpace(dependency.VersionId))
            return null;

        return versions.FirstOrDefault(version => string.Equals(
            version.VersionId,
            dependency.VersionId,
            StringComparison.OrdinalIgnoreCase));
    }

    private async Task<InstalledDependencyCatalog> LoadEnabledLocalModIdentifiersAsync(
        GameInstance instance,
        CancellationToken cancellationToken)
    {
        if (modService is null)
            return new InstalledDependencyCatalog([]);

        var mods = await modService.GetModsAsync(instance, cancellationToken).ConfigureAwait(false);
        var installedVersionsByModId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods
            .Where(mod => mod.IsEnabled
                && !string.IsNullOrWhiteSpace(mod.ModId)
                && !string.IsNullOrWhiteSpace(mod.Version)))
        {
            var modId = mod.ModId!.Trim();
            var version = mod.Version!.Trim();
            if (!installedVersionsByModId.TryGetValue(modId, out var versions))
            {
                versions = [];
                installedVersionsByModId[modId] = versions;
            }

            versions.Add(version);
        }

        return new InstalledDependencyCatalog(installedVersionsByModId);
    }

    private static RequiredDependencyRequirementState ResolveDependencyRequirementState(
        RequiredDependencyInstallCandidate candidate,
        InstalledDependencyCatalog installedDependencies)
    {
        var minimumVersion = candidate.MinimumVersion;
        if (minimumVersion is null || string.IsNullOrWhiteSpace(minimumVersion.VersionNumber))
            return RequiredDependencyRequirementState.Missing;

        var hasInstalledDependency = false;
        foreach (var identifier in EnumerateDependencyIdentifiers(candidate.Dependency.Project))
        {
            foreach (var installedVersion in installedDependencies.GetVersions(identifier))
            {
                hasInstalledDependency = true;
                if (ResourceDependencyVersionComparer.IsGreaterThanOrEqual(
                    installedVersion,
                    minimumVersion.VersionNumber))
                {
                    return RequiredDependencyRequirementState.Installed;
                }
            }
        }

        return hasInstalledDependency
            ? RequiredDependencyRequirementState.UpdateRequired
            : RequiredDependencyRequirementState.Missing;
    }

    private static void AddDependencyIdentifiers(
        InstalledDependencyCatalog installedDependencies,
        ResourceProjectDependency dependency,
        ResourceProjectVersion version)
    {
        if (string.IsNullOrWhiteSpace(version.VersionNumber))
            return;

        foreach (var identifier in EnumerateDependencyIdentifiers(dependency.Project))
            installedDependencies.Add(identifier, version.VersionNumber);
    }

    private static IEnumerable<string> EnumerateDependencyIdentifiers(ResourceProject dependency)
    {
        if (!string.IsNullOrWhiteSpace(dependency.Slug))
            yield return dependency.Slug;
        if (!string.IsNullOrWhiteSpace(dependency.ProjectId))
            yield return dependency.ProjectId;
    }

    private static string ResolveDependencyKey(ResourceProject dependency)
    {
        return string.IsNullOrWhiteSpace(dependency.ProjectId)
            ? dependency.Slug
            : dependency.ProjectId;
    }

    private static bool IsUnknownInstanceVersionTarget(ResourcesModInstallTargetItemViewModel? target)
    {
        return target?.IsLocalDownload is false
            && !target.IsNewInstanceInstall
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

    private IReadOnlyList<ResourcesFilterOptionItem> CreateAvailableLoaderFilterOptions(IReadOnlyList<ResourceProjectVersion> versions)
    {
        var options = CreateDefaultAvailableLoaderFilterOptions();
        if (!this.options.ShowsLoaderFilters)
            return options;

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

    private ResourcesFilterOptionItem CreateAllAvailableVersionFilterOption()
    {
        return new ResourcesFilterOptionItem { Id = "all", Title = options.AllVersionsText };
    }

    private List<ResourcesFilterOptionItem> CreateDefaultAvailableLoaderFilterOptions()
    {
        return
        [
            new ResourcesFilterOptionItem { Id = "all", Title = options.AllLoadersText }
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

    private bool MatchesAvailableLoaderFilter(ResourceProjectVersion version, string? selectedLoader)
    {
        if (string.IsNullOrWhiteSpace(selectedLoader)
            || string.Equals(selectedLoader, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!options.ShowsLoaderFilters)
            return true;

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
            AvailableVersionListItems.Add(new ResourcesModVersionItemViewModel(version, SelectedProject, options.FallbackIconKey));
            VisibleAvailableVersionCount++;
        }
    }

    private void AppendFlatAvailableVersionItems(IReadOnlyList<ResourceProjectVersion> versions)
    {
        if (!AvailableVersionListItems.OfType<ResourcesModVersionListHeaderItem>().Any())
            AvailableVersionListItems.Add(new ResourcesModVersionListHeaderItem(AvailableVersionsTitle));

        foreach (var version in versions)
        {
            AvailableVersionListItems.Add(new ResourcesModVersionItemViewModel(version, SelectedProject, options.FallbackIconKey));
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
                AvailableVersionListItems.Add(new ResourcesModVersionItemViewModel(version, SelectedProject, options.FallbackIconKey));
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
                AvailableVersionListItems.Insert(insertIndex, new ResourcesModVersionItemViewModel(version, SelectedProject, options.FallbackIconKey));
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
        var loaders = options.ShowsLoaderFilters
            ? ResolveCompatibilityLoaders(version)
            : [string.Empty];
        var selectedVersion = SelectedAvailableVersionFilterOption?.Id;
        var selectedLoader = SelectedAvailableLoaderFilterOption?.Id;

        if (!string.IsNullOrWhiteSpace(selectedVersion)
            && !string.Equals(selectedVersion, "all", StringComparison.OrdinalIgnoreCase))
        {
            gameVersions = gameVersions
                .Where(gameVersion => string.Equals(gameVersion, selectedVersion, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (options.ShowsLoaderFilters
            && !string.IsNullOrWhiteSpace(selectedLoader)
            && !string.Equals(selectedLoader, "all", StringComparison.OrdinalIgnoreCase))
        {
            loaders = loaders
                .Where(loader => string.Equals(loader, selectedLoader, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        foreach (var gameVersion in gameVersions)
        {
            foreach (var loader in loaders)
            {
                yield return string.IsNullOrWhiteSpace(loader)
                    ? gameVersion
                    : $"{gameVersion}-{loader}";
            }
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

    private string FormatAvailableVersionsTitle(ResourcesModInstallTargetItemViewModel? target)
    {
        if (target?.IsLocalDownload != false || IsUnknownInstanceVersionTarget(target))
            return options.VersionsAllTitleText;

        if (target.Instance is not { } instance)
            return options.VersionsAllTitleText;

        var minecraftVersion = instance.MinecraftVersion?.Trim();
        if (string.IsNullOrWhiteSpace(minecraftVersion))
            return options.VersionsAllTitleText;

        return options.ShowsLoaderFilters
            ? $"{minecraftVersion}-{GetLoaderId(instance.Loader)}"
            : minecraftVersion;
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
                options.FileExistsMessageFormat,
                ResolveVersionFileNameForDisplay(item));
            IsProjectVersionFileExistsDialogOpen = true;
        });
    }

    private void LogFilterSelection(string filterId, ResourcesFilterOptionItem? option)
    {
        if (option is null)
            return;

        logger?.LogInformation(
            "Resource project filter selected. Kind={Kind}, FilterId={FilterId}, OptionId={OptionId}",
            options.Kind,
            filterId,
            option.Id);
    }

    private DownloadTaskItem? BeginModDownloadTask(ResourcesModVersionItemViewModel item, string subtitle)
    {
        var task = downloadTasksPage?.BeginTask(item.Title, subtitle);
        task?.Report(new LauncherProgress(
            ModProgressStages.DownloadingFile,
            string.Format(options.DownloadingFormat, item.Title)));
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

    private IProgress<LauncherProgress>? CreateModpackImportProgressReporter(DownloadTaskItem? task)
    {
        if (task is null)
            return null;

        return new Progress<LauncherProgress>(progress =>
        {
            task.Report(progress with { Message = LauncherProgressTextFormatter.Format(progress) });
        });
    }

    private string MapModpackImportFailureMessage(ModpackImportFailureReason failureReason)
    {
        return failureReason switch
        {
            ModpackImportFailureReason.FileNotFound
                or ModpackImportFailureReason.UnsupportedArchive
                or ModpackImportFailureReason.InvalidManifest
                => Strings.Status_ModpackInvalidArchive,
            ModpackImportFailureReason.UnsupportedLoader
                => Strings.Status_ModpackUnsupportedLoader,
            ModpackImportFailureReason.MissingCurseForgeApiKey
                => Strings.Status_ModpackMissingCurseForgeApiKey,
            ModpackImportFailureReason.HashMismatch
                => Strings.Status_ModpackHashMismatch,
            _ => options.InstallFailedText
        };
    }

    private void SafeDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(exception, "Failed to delete temporary resource modpack directory. Directory={Directory}", directory);
        }
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

    private static ObservableCollection<ResourcesFilterOptionItem> CreateLoaderOptions(ResourcesOnlineProjectPageOptions options)
    {
        if (!options.ShowsLoaderFilters)
            return [new ResourcesFilterOptionItem { Id = "all", Title = options.AllLoadersText }];

        return
        [
            new ResourcesFilterOptionItem { Id = "all", Title = options.AllLoadersText },
            new ResourcesFilterOptionItem { Id = "fabric", Title = Strings.Download_FabricLoaderTitle },
            new ResourcesFilterOptionItem { Id = "forge", Title = Strings.Download_ForgeLoaderTitle },
            new ResourcesFilterOptionItem { Id = "neoforge", Title = Strings.Download_NeoForgeLoaderTitle },
            new ResourcesFilterOptionItem { Id = "quilt", Title = Strings.Download_QuiltLoaderTitle }
        ];
    }

    private static ObservableCollection<ResourcesFilterOptionItem> CreateSourceOptions(ResourcesOnlineProjectPageOptions options)
    {
        if (options.SourceOptions is { Count: > 0 } sourceOptions)
            return [.. sourceOptions];

        return
        [
            new ResourcesFilterOptionItem { Id = "all", Title = Strings.Resources_ModFilterAllSources },
            new ResourcesFilterOptionItem { Id = "modrinth", Title = Strings.Resources_ModSourceModrinth },
            new ResourcesFilterOptionItem { Id = "curseforge", Title = Strings.Resources_ModSourceCurseForge }
        ];
    }

    private static ObservableCollection<ResourcesFilterOptionItem> CreateTypeOptions(ResourcesOnlineProjectPageOptions options)
    {
        var typeOptions = new ObservableCollection<ResourcesFilterOptionItem>
        {
            new() { Id = "all", Title = Strings.Resources_ModFilterAllTypes }
        };

        foreach (var option in options.TypeOptions)
            typeOptions.Add(new ResourcesFilterOptionItem { Id = option.Id, Title = option.Title });

        return typeOptions;
    }

    protected static ResourcesOnlineProjectPageOptions CreateModOptions()
    {
        return new ResourcesOnlineProjectPageOptions(
            ResourceProjectKind.Mod,
            Strings.Resources_SectionMods,
            "instance_setting_page/mod",
            ShowsLoaderFilters: true,
            Strings.Resources_ModFilterAllVersions,
            Strings.Resources_ModFilterAllLoaders,
            Strings.Resources_ModProjectsLoading,
            Strings.Resources_ModProjectsEmpty,
            Strings.Resources_ModProjectsLoadError,
            Strings.Resources_ModProjectsLoadingMore,
            Strings.Resources_ModProjectsNoMore,
            Strings.Resources_ModProjectsLoadMoreError,
            Strings.Resources_ModCurseForgeMissingApiKey,
            Strings.Resources_ModDetailsInfoSection,
            Strings.Resources_ModInstallTargetSection,
            Strings.Resources_ModInstallTargetLocal,
            Strings.Resources_ModInstallTargetsLoading,
            Strings.Resources_ModInstallTargetsLoadError,
            Strings.Resources_ModVersionsLoading,
            Strings.Resources_ModVersionsEmpty,
            Strings.Resources_ModVersionsEmptyLocal,
            Strings.Resources_ModVersionsFilterEmpty,
            Strings.Resources_ModVersionsLoadError,
            Strings.Resources_ModVersionsLoadingMore,
            Strings.Resources_ModVersionsNoMore,
            Strings.Resources_ModVersionsLoadMoreError,
            Strings.Resources_ModVersionsAllTitle,
            Strings.FilePicker_ModDownloadDirectoryTitle,
            Strings.Status_ModDownloading,
            Strings.Status_ModDownloadingFormat,
            Strings.Status_ModDownloadedFormat,
            Strings.Status_ModDownloadFailed,
            Strings.Status_ModInstalledFormat,
            Strings.Status_ModInstallFailed,
            Strings.Resources_ModDownloadFileExistsMessageFormat,
            [
                new("optimization", Strings.Resources_ModFilterTypeOptimization, ResourceProjectCategory.Optimization),
                new("utility", Strings.Resources_ModFilterTypeUtility, ResourceProjectCategory.Utility),
                new("adventure", Strings.Resources_ModFilterTypeAdventure, ResourceProjectCategory.Adventure),
                new("decoration", Strings.Resources_ModFilterTypeDecoration, ResourceProjectCategory.Decoration),
                new("equipment", Strings.Resources_ModFilterTypeEquipment, ResourceProjectCategory.Equipment),
                new("technology", Strings.Resources_ModFilterTypeTechnology, ResourceProjectCategory.Technology),
                new("magic", Strings.Resources_ModFilterTypeMagic, ResourceProjectCategory.Magic),
                new("mobs", Strings.Resources_ModFilterTypeMobs, ResourceProjectCategory.Mobs),
                new("worldgen", Strings.Resources_ModFilterTypeWorldGeneration, ResourceProjectCategory.WorldGeneration),
                new("storage", Strings.Resources_ModFilterTypeStorage, ResourceProjectCategory.Storage),
                new("library", Strings.Resources_ModFilterTypeLibrary, ResourceProjectCategory.Library)
            ]);
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

    private sealed record RequiredDependencyInstallPlan(
        RequiredDependenciesDialogChoice Choice,
        IReadOnlyList<RequiredDependencyInstallCandidate> MissingDependencies)
    {
        public static RequiredDependencyInstallPlan Continue { get; } =
            new(RequiredDependenciesDialogChoice.ContinueWithoutDependencies, []);
    }

    private sealed record RequiredDependencyInstallCandidate(
        ResourceProjectDependency Dependency,
        ResourceProjectVersion? MinimumVersion,
        ResourceProjectVersion? InstallVersion);

    private sealed class InstalledDependencyCatalog
    {
        private readonly Dictionary<string, List<string>> versionsByModId;

        public InstalledDependencyCatalog(Dictionary<string, List<string>> versionsByModId)
        {
            this.versionsByModId = versionsByModId;
        }

        public IEnumerable<string> GetVersions(string modId)
        {
            if (string.IsNullOrWhiteSpace(modId))
                return [];

            return versionsByModId.TryGetValue(modId.Trim(), out var versions)
                ? versions
                : [];
        }

        public void Add(string modId, string version)
        {
            if (string.IsNullOrWhiteSpace(modId) || string.IsNullOrWhiteSpace(version))
                return;

            var key = modId.Trim();
            if (!versionsByModId.TryGetValue(key, out var versions))
            {
                versions = [];
                versionsByModId[key] = versions;
            }

            versions.Add(version.Trim());
        }
    }

    private static class ResourceDependencyVersionComparer
    {
        private static readonly string[] KnownContextTokens =
        [
            "mc",
            "minecraft",
            "fabric",
            "forge",
            "neoforge",
            "quilt"
        ];

        public static bool IsGreaterThanOrEqual(string installedVersion, string minimumVersion)
        {
            if (!TryParse(installedVersion, out var installed)
                || !TryParse(minimumVersion, out var minimum))
            {
                return false;
            }

            return installed.CompareTo(minimum) >= 0;
        }

        private static bool TryParse(string value, out ParsedDependencyVersion version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var tokens = value
                .Trim()
                .Split(['+', '-', '_', ' ', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(token => !IsContextToken(token))
                .ToList();
            var numericToken = tokens
                .LastOrDefault(token => char.IsDigit(token[0]) && token.Contains('.'))
                ?? tokens.LastOrDefault(token => char.IsDigit(token[0]));
            if (numericToken is null)
                return false;

            var numbers = numericToken
                .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => new string(part.TakeWhile(char.IsDigit).ToArray()))
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(int.Parse)
                .ToArray();
            if (numbers.Length == 0)
                return false;

            var qualifier = tokens.FirstOrDefault(token =>
                string.Equals(token, "alpha", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "beta", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "rc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "pre", StringComparison.OrdinalIgnoreCase)
                || token.StartsWith("alpha.", StringComparison.OrdinalIgnoreCase)
                || token.StartsWith("beta.", StringComparison.OrdinalIgnoreCase)
                || token.StartsWith("rc.", StringComparison.OrdinalIgnoreCase)
                || token.StartsWith("pre.", StringComparison.OrdinalIgnoreCase));
            version = new ParsedDependencyVersion(numbers, ResolveQualifierWeight(qualifier));
            return true;
        }

        private static bool IsContextToken(string token)
        {
            if (KnownContextTokens.Any(context => string.Equals(token, context, StringComparison.OrdinalIgnoreCase)))
                return true;

            if (token.StartsWith("mc", StringComparison.OrdinalIgnoreCase)
                && token.Skip(2).Any(char.IsDigit))
            {
                return true;
            }

            return token.Contains("minecraft", StringComparison.OrdinalIgnoreCase);
        }

        private static int ResolveQualifierWeight(string? qualifier)
        {
            if (string.IsNullOrWhiteSpace(qualifier))
                return 3;

            if (qualifier.StartsWith("alpha", StringComparison.OrdinalIgnoreCase))
                return 0;
            if (qualifier.StartsWith("beta", StringComparison.OrdinalIgnoreCase))
                return 1;
            if (qualifier.StartsWith("pre", StringComparison.OrdinalIgnoreCase)
                || qualifier.StartsWith("rc", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            return 3;
        }

        private readonly record struct ParsedDependencyVersion(
            IReadOnlyList<int> Numbers,
            int QualifierWeight) : IComparable<ParsedDependencyVersion>
        {
            public int CompareTo(ParsedDependencyVersion other)
            {
                var count = Math.Max(Numbers.Count, other.Numbers.Count);
                for (var index = 0; index < count; index++)
                {
                    var left = index < Numbers.Count ? Numbers[index] : 0;
                    var right = index < other.Numbers.Count ? other.Numbers[index] : 0;
                    var comparison = left.CompareTo(right);
                    if (comparison != 0)
                        return comparison;
                }

                return QualifierWeight.CompareTo(other.QualifierWeight);
            }
        }
    }

    private enum RequiredDependenciesDialogChoice
    {
        Cancel,
        ContinueWithoutDependencies,
        AutoInstallDependencies
    }

    private sealed class RequiredDependencyInstallException : Exception
    {
        public RequiredDependencyInstallException(ResourceProject dependency)
            : base($"Required dependency cannot be installed automatically: {dependency.ProjectId}")
        {
            DependencyProjectId = dependency.ProjectId;
            DependencyTitle = string.IsNullOrWhiteSpace(dependency.Title)
                ? dependency.Slug
                : dependency.Title;
        }

        public string DependencyProjectId { get; }

        public string DependencyTitle { get; }
    }

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
