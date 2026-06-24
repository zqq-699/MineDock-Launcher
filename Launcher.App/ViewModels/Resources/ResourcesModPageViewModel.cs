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
    private const int CatalogPageSize = 20;
    private const int InitialProjectBatchSize = 12;
    private const int AppendProjectBatchSize = 8;
    private readonly IResourceCatalogService? resourceCatalogService;
    private readonly IGameVersionService? gameVersionService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger? logger;
    private readonly object releaseVersionOrderGate = new();
    private CancellationTokenSource? refreshCancellationTokenSource;
    private bool hasRequestedInitialProjectLoad;
    private bool isApplyingVersionFilterOptions;
    private Task<ReleaseVersionData?>? releaseVersionDataTask;

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
    private ResourcesFilterOptionItem? selectedVersionOption;

    [ObservableProperty]
    private ResourcesFilterOptionItem? selectedLoaderOption;

    [ObservableProperty]
    private ResourcesFilterOptionItem? selectedSourceOption;

    [ObservableProperty]
    private ResourcesFilterOptionItem? selectedTypeOption;

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

    public void BeginEnsureProjectsLoaded()
    {
        BeginEnsureVersionOptionsLoaded();

        if (hasRequestedInitialProjectLoad || resourceCatalogService is null)
            return;

        _ = RefreshProjectsCoreAsync();
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
        ScheduleProjectsRefresh();
    }

    partial void OnSelectedVersionOptionChanged(ResourcesFilterOptionItem? value)
    {
        if (isApplyingVersionFilterOptions)
            return;

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

    private void LogFilterSelection(string filterId, ResourcesFilterOptionItem? option)
    {
        if (option is null)
            return;

        logger?.LogInformation(
            "Resources mod filter selected. FilterId={FilterId}, OptionId={OptionId}",
            filterId,
            option.Id);
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
}
