using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.Utilities;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.Resources;

public sealed partial class ResourcesModPageViewModel : ResourcesSectionViewModelBase
{
    private const int SearchDebounceMilliseconds = 350;
    private const int InitialProjectBatchSize = 12;
    private const int AppendProjectBatchSize = 8;
    private readonly IResourceCatalogService? resourceCatalogService;
    private readonly IGameVersionService? gameVersionService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger? logger;
    private readonly object releaseVersionOrderGate = new();
    private CancellationTokenSource? refreshCancellationTokenSource;
    private bool hasRequestedInitialProjectLoad;
    private bool hasResolvedReleaseVersionOrder;
    private IReadOnlyList<string>? releaseVersionOrder;

    public ResourcesModPageViewModel(
        ResourcesPageViewModel parent,
        IResourceCatalogService? resourceCatalogService = null,
        ILogger? logger = null,
        IUiDispatcher? uiDispatcher = null,
        IGameVersionService? gameVersionService = null)
        : base(parent, Strings.Resources_SectionMods)
    {
        this.resourceCatalogService = resourceCatalogService;
        this.gameVersionService = gameVersionService;
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

        selectedVersionOption = VersionOptions[0];
        selectedLoaderOption = LoaderOptions[0];
        selectedSourceOption = SourceOptions[0];
        selectedTypeOption = TypeOptions[0];
    }

    public ObservableCollection<ResourcesFilterOptionItem> VersionOptions { get; }

    public ObservableCollection<ResourcesFilterOptionItem> LoaderOptions { get; }

    public ObservableCollection<ResourcesFilterOptionItem> SourceOptions { get; }

    public ObservableCollection<ResourcesFilterOptionItem> TypeOptions { get; }

    public ObservableCollection<ResourcesModProjectItemViewModel> VisibleProjects { get; } = [];

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private bool isLoadingProjects;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadErrorMessage))]
    private string loadErrorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPartialWarningMessage))]
    private string partialWarningMessage = string.Empty;

    [ObservableProperty]
    private int listEntranceAnimationToken;

    [ObservableProperty]
    private ResourcesFilterOptionItem? selectedVersionOption;

    [ObservableProperty]
    private ResourcesFilterOptionItem? selectedLoaderOption;

    [ObservableProperty]
    private ResourcesFilterOptionItem? selectedSourceOption;

    [ObservableProperty]
    private ResourcesFilterOptionItem? selectedTypeOption;

    public bool HasVisibleProjects => VisibleProjects.Count > 0;

    public bool HasLoadErrorMessage => !string.IsNullOrWhiteSpace(LoadErrorMessage);

    public bool HasPartialWarningMessage => !string.IsNullOrWhiteSpace(PartialWarningMessage);

    public string EmptyMessage => Strings.Resources_ModProjectsEmpty;

    public bool CanShowLoadingState => IsLoadingProjects && !HasVisibleProjects;

    public bool CanShowEmptyState => !IsLoadingProjects && !HasVisibleProjects && !HasLoadErrorMessage;

    public bool CanShowLoadErrorState => !IsLoadingProjects && !HasVisibleProjects && HasLoadErrorMessage;

    [RelayCommand]
    public async Task RefreshProjectsAsync()
    {
        await RefreshProjectsCoreAsync();
    }

    public void BeginEnsureProjectsLoaded()
    {
        if (hasRequestedInitialProjectLoad || resourceCatalogService is null)
            return;

        _ = RefreshProjectsCoreAsync();
    }

    partial void OnSearchQueryChanged(string value)
    {
        ScheduleProjectsRefresh();
    }

    partial void OnSelectedVersionOptionChanged(ResourcesFilterOptionItem? value)
    {
        LogFilterSelection("version", value);
        ScheduleProjectsRefresh();
    }

    partial void OnSelectedLoaderOptionChanged(ResourcesFilterOptionItem? value)
    {
        LogFilterSelection("loader", value);
        ScheduleProjectsRefresh();
    }

    partial void OnSelectedSourceOptionChanged(ResourcesFilterOptionItem? value)
    {
        LogFilterSelection("source", value);
        ScheduleProjectsRefresh();
    }

    partial void OnSelectedTypeOptionChanged(ResourcesFilterOptionItem? value)
    {
        LogFilterSelection("type", value);
        ScheduleProjectsRefresh();
    }

    partial void OnIsLoadingProjectsChanged(bool value)
    {
        RaiseProjectStatePropertiesChanged();
    }

    partial void OnLoadErrorMessageChanged(string value)
    {
        RaiseProjectStatePropertiesChanged();
    }

    private async Task RunProjectLoadAsync(
        ResourceCatalogSearchRequest request,
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
                        return new ProjectLoadResult(catalogResult, items);
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetResult();
                return;
            }

            uiDispatcher.Post(() => CompleteProjectLoad(result, cancellationToken, completion));
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

            uiDispatcher.Post(() => FailProjectLoad(exception, cancellationToken, completion));
        }
    }

    private async Task<IReadOnlyList<string>?> GetReleaseVersionOrderAsync(CancellationToken cancellationToken)
    {
        if (gameVersionService is null)
            return null;

        lock (releaseVersionOrderGate)
        {
            if (hasResolvedReleaseVersionOrder)
                return releaseVersionOrder;
        }

        try
        {
            var versions = await gameVersionService.GetVersionsAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var resolvedOrder = versions
                .Where(version => string.Equals(version.Type, "release", StringComparison.OrdinalIgnoreCase))
                .Select(version => version.Name)
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .ToList();

            lock (releaseVersionOrderGate)
            {
                releaseVersionOrder = resolvedOrder;
                hasResolvedReleaseVersionOrder = true;
                return releaseVersionOrder;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Failed to load Minecraft release version order for resource mod subtitles.");
            lock (releaseVersionOrderGate)
            {
                releaseVersionOrder = [];
                hasResolvedReleaseVersionOrder = true;
                return releaseVersionOrder;
            }
        }
    }

    private async void ScheduleProjectsRefresh()
    {
        if (resourceCatalogService is null)
            return;

        var cancellationToken = BeginProjectLoad();

        try
        {
            await Task.Delay(SearchDebounceMilliseconds, cancellationToken).ConfigureAwait(true);
            await QueueProjectLoadAsync(CreateSearchRequest(), cancellationToken).ConfigureAwait(true);
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
        return QueueProjectLoadAsync(CreateSearchRequest(), cancellationToken);
    }

    private CancellationToken BeginProjectLoad()
    {
        hasRequestedInitialProjectLoad = true;
        refreshCancellationTokenSource?.Cancel();
        refreshCancellationTokenSource?.Dispose();
        refreshCancellationTokenSource = new CancellationTokenSource();

        IsLoadingProjects = true;
        LoadErrorMessage = string.Empty;
        PartialWarningMessage = string.Empty;

        return refreshCancellationTokenSource.Token;
    }

    private Task QueueProjectLoadAsync(ResourceCatalogSearchRequest request, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        uiDispatcher.Post(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetResult();
                return;
            }

            _ = RunProjectLoadAsync(request, cancellationToken, completion);
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

            VisibleProjects.Clear();
            PartialWarningMessage = string.Empty;
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
        LoadErrorMessage = Strings.Resources_ModProjectsLoadError;
        PartialWarningMessage = string.Empty;
        IsLoadingProjects = false;
        logger?.LogError(exception, "Failed to load resources mod projects.");
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

    private sealed record ProjectLoadResult(
        ResourceCatalogSearchResult CatalogResult,
        IReadOnlyList<ResourcesModProjectItemViewModel> Items);

    private ResourceCatalogSearchRequest CreateSearchRequest()
    {
        return new ResourceCatalogSearchRequest
        {
            Query = SearchQuery,
            MinecraftVersion = SelectedVersionOption?.Id is { } versionId && versionId != "all" ? versionId : string.Empty,
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
            }
        };
    }

    private void RaiseProjectStatePropertiesChanged()
    {
        OnPropertyChanged(nameof(HasVisibleProjects));
        OnPropertyChanged(nameof(CanShowLoadingState));
        OnPropertyChanged(nameof(CanShowEmptyState));
        OnPropertyChanged(nameof(CanShowLoadErrorState));
        OnPropertyChanged(nameof(HasLoadErrorMessage));
        OnPropertyChanged(nameof(HasPartialWarningMessage));
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
}
