/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.Resources;

public sealed partial class ResourcesProjectVersionsViewModel : ObservableObject, IDisposable
{
    private const int PageSize = 10000;

    private readonly HashSet<string> loadedVersionIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ResourcesOnlineProjectPageOptions options;
    private readonly IResourceCatalogService? resourceCatalogService;
    private readonly IGameInstanceService? gameInstanceService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger? logger;
    private CancellationTokenSource? targetsCancellation;
    private CancellationTokenSource? versionsCancellation;
    private bool isApplyingFilters;
    private ResourcesModProjectItemViewModel? currentProject;

    internal ResourcesProjectVersionsViewModel(
        ResourcesOnlineProjectPageOptions options,
        IResourceCatalogService? resourceCatalogService,
        IGameInstanceService? gameInstanceService,
        IUiDispatcher uiDispatcher,
        ILogger? logger)
    {
        this.options = options;
        this.resourceCatalogService = resourceCatalogService;
        this.gameInstanceService = gameInstanceService;
        this.uiDispatcher = uiDispatcher;
        this.logger = logger;
        Builder = new ResourcesAvailableVersionListBuilder(options);
        ResetFilterOptions();
    }

    public event Action<ResourcesModInstallTargetItemViewModel>? TargetSelected;

    public event Action<ResourcesModVersionItemViewModel>? InstallRequested;

    internal ResourcesAvailableVersionListBuilder Builder { get; }

    public ObservableCollection<ResourcesModInstallTargetItemViewModel> InstallTargets { get; } = [];

    public ObservableCollection<object> ListItems { get; } = [];

    public ObservableCollection<ResourcesFilterOptionItem> VersionFilterOptions { get; } = [];

    public ObservableCollection<ResourcesFilterOptionItem> LoaderFilterOptions { get; } = [];

    public IReadOnlyList<ResourceProjectVersion> SourceVersions { get; private set; } = [];

    public int NextPageOffset { get; private set; }

    public string InstallTargetSectionText => options.InstallTargetSectionText;

    public string InstallTargetsLoadingMessage => options.InstallTargetsLoadingText;

    public string LoadingMessage => options.VersionsLoadingText;

    public string Title => Builder.FormatTitle(SelectedTarget);

    [ObservableProperty]
    private ResourcesModInstallTargetItemViewModel? selectedTarget;

    [ObservableProperty]
    private bool isLoadingTargets;

    [ObservableProperty]
    private string targetsLoadErrorMessage = string.Empty;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string loadErrorMessage = string.Empty;

    [ObservableProperty]
    private bool isLoadingMore;

    [ObservableProperty]
    private string loadMoreMessage = string.Empty;

    [ObservableProperty]
    private bool hasMore;

    [ObservableProperty]
    private int visibleVersionCount;

    [ObservableProperty]
    private int listEntranceAnimationToken;

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private ResourcesFilterOptionItem? selectedVersionFilter;

    [ObservableProperty]
    private ResourcesFilterOptionItem? selectedLoaderFilter;

    [ObservableProperty]
    private bool isUnknownInstanceVersionDialogOpen;

    public bool HasTargetsLoadErrorMessage => !string.IsNullOrWhiteSpace(TargetsLoadErrorMessage);

    public bool CanShowTargetsLoadingState => IsLoadingTargets && InstallTargets.Count == 0;

    public bool CanShowTargetsLoadErrorState => !IsLoadingTargets && HasTargetsLoadErrorMessage;

    public bool HasLoadErrorMessage => !string.IsNullOrWhiteSpace(LoadErrorMessage);

    public bool HasFilters => !string.IsNullOrWhiteSpace(SearchQuery)
        || SelectedVersionFilter?.Id is { } versionId && !string.Equals(versionId, "all", StringComparison.OrdinalIgnoreCase)
        || options.ShowsLoaderFilters && SelectedLoaderFilter?.Id is { } loaderId && !string.Equals(loaderId, "all", StringComparison.OrdinalIgnoreCase);

    public string EmptyMessage => HasFilters
        ? options.VersionsFilterEmptyText
        : SelectedTarget?.IsLocalDownload == true || ResourcesAvailableVersionListBuilder.IsUnknownInstanceVersionTarget(SelectedTarget)
            ? options.VersionsEmptyLocalText
            : options.VersionsEmptyText;

    public bool CanShowLoadingState => IsLoading && VisibleVersionCount == 0;

    public bool CanShowEmptyState => !IsLoading && VisibleVersionCount == 0 && !HasMore && !HasLoadErrorMessage;

    public bool CanShowLoadErrorState => !IsLoading && HasLoadErrorMessage;

    public bool CanShowLoadMoreState => VisibleVersionCount > 0
        && (IsLoadingMore || !string.IsNullOrWhiteSpace(LoadMoreMessage));

    public void SetProject(ResourcesModProjectItemViewModel project)
    {
        currentProject = project;
        SelectedTarget = null;
        CancelVersionsLoad();
        ResetVersions(resetFilters: true);
        _ = LoadTargetsAsync();
    }

    public void Reset()
    {
        currentProject = null;
        SelectedTarget = null;
        CancelTargetsLoad();
        CancelVersionsLoad();
        InstallTargets.Clear();
        TargetsLoadErrorMessage = string.Empty;
        IsLoadingTargets = false;
        IsUnknownInstanceVersionDialogOpen = false;
        ResetVersions(resetFilters: true);
        RaiseTargetStateChanged();
    }

    [RelayCommand]
    private void SelectTarget(ResourcesModInstallTargetItemViewModel? target)
    {
        if (target is null || currentProject is null || resourceCatalogService is null)
            return;

        SelectedTarget = target;
        IsUnknownInstanceVersionDialogOpen = ResourcesAvailableVersionListBuilder.IsUnknownInstanceVersionTarget(target);
        TargetSelected?.Invoke(target);
        _ = LoadVersionsAsync(target);
    }

    [RelayCommand]
    private void InstallVersion(ResourcesModVersionItemViewModel? item)
    {
        if (item is not null)
            InstallRequested?.Invoke(item);
    }

    [RelayCommand]
    private void CloseUnknownInstanceVersionDialog()
    {
        IsUnknownInstanceVersionDialogOpen = false;
    }

    public void BeginLoadMore()
    {
        if (resourceCatalogService is null || currentProject is null || SelectedTarget is null
            || !HasMore || IsLoading || IsLoadingMore)
        {
            return;
        }

        _ = LoadMoreAsync();
    }

    [RelayCommand]
    public async Task LoadMoreAsync()
    {
        var project = currentProject;
        if (resourceCatalogService is null || project is null || SelectedTarget is null
            || !HasMore || IsLoading || IsLoadingMore)
        {
            return;
        }

        var cancellationToken = versionsCancellation?.Token ?? CancellationToken.None;
        IsLoadingMore = true;
        LoadMoreMessage = options.VersionsLoadingMoreText;
        UpdateFooter();
        RaiseStateChanged();
        try
        {
            var page = await LoadNextPageAsync(project.Project, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            uiDispatcher.Invoke(() => ApplyMore(page, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested)
                return;
            uiDispatcher.Invoke(() =>
            {
                IsLoadingMore = false;
                LoadMoreMessage = options.VersionsLoadMoreErrorText;
                UpdateFooter();
                RaiseStateChanged();
            });
            logger?.LogError(exception, "Failed to load more resource versions. Kind={Kind}", options.Kind);
        }
    }

    public void Dispose()
    {
        CancelTargetsLoad();
        CancelVersionsLoad();
    }

    partial void OnSearchQueryChanged(string value)
    {
        if (!isApplyingFilters)
            RebuildList();
    }

    partial void OnSelectedVersionFilterChanged(ResourcesFilterOptionItem? value)
    {
        if (!isApplyingFilters)
            RebuildList();
    }

    partial void OnSelectedLoaderFilterChanged(ResourcesFilterOptionItem? value)
    {
        if (!isApplyingFilters)
            RebuildList();
    }

    partial void OnSelectedTargetChanged(ResourcesModInstallTargetItemViewModel? value)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private async Task LoadTargetsAsync()
    {
        var replacement = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref targetsCancellation, replacement);
        previous?.Cancel();
        previous?.Dispose();
        var cancellationToken = replacement.Token;

        uiDispatcher.Invoke(() =>
        {
            InstallTargets.Clear();
            TargetsLoadErrorMessage = string.Empty;
            IsLoadingTargets = true;
            RaiseTargetStateChanged();
        });

        try
        {
            IReadOnlyList<GameInstance> instances = [];
            if (gameInstanceService is not null
                && options.InstallTargetMode is ResourcesOnlineProjectInstallTargetMode.ExistingInstance)
            {
                instances = await gameInstanceService.GetInstancesAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            uiDispatcher.Invoke(() => ApplyTargets(instances, string.Empty, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested)
                return;
            uiDispatcher.Invoke(() => ApplyTargets([], options.InstallTargetsLoadErrorText, cancellationToken));
            logger?.LogError(exception, "Failed to load resource install targets. Kind={Kind}", options.Kind);
        }
    }

    private void ApplyTargets(IReadOnlyList<GameInstance> instances, string error, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

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

        InstallTargets.Clear();
        foreach (var target in targets)
            InstallTargets.Add(target);
        TargetsLoadErrorMessage = error;
        IsLoadingTargets = false;
        RaiseTargetStateChanged();
    }

    private async Task LoadVersionsAsync(ResourcesModInstallTargetItemViewModel target)
    {
        var replacement = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref versionsCancellation, replacement);
        previous?.Cancel();
        previous?.Dispose();
        var cancellationToken = replacement.Token;

        uiDispatcher.Invoke(() =>
        {
            ResetVersions(resetFilters: true);
            ListItems.Add(new ResourcesModVersionListHeaderItem(Title));
            IsLoading = true;
            RaiseStateChanged();
        });

        try
        {
            var project = currentProject;
            if (resourceCatalogService is null || project is null
                || !ReferenceEquals(target, SelectedTarget)
                || !target.IsLocalDownload && !target.IsNewInstanceInstall && target.Instance is null)
            {
                uiDispatcher.Invoke(() => ApplyInitial(new AvailableVersionPage(new ResourceProjectVersionsResult(), []), options.VersionsLoadErrorText, cancellationToken));
                return;
            }

            var page = await LoadNextPageAsync(project.Project, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var error = page.Result.IsCurseForgeUnavailable ? options.VersionsLoadErrorText : string.Empty;
            uiDispatcher.Invoke(() => ApplyInitial(page, error, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested)
                return;
            uiDispatcher.Invoke(() => ApplyInitial(new AvailableVersionPage(new ResourceProjectVersionsResult(), []), options.VersionsLoadErrorText, cancellationToken));
            logger?.LogError(exception, "Failed to load resource project versions. Kind={Kind}", options.Kind);
        }
    }

    private async Task<AvailableVersionPage> LoadNextPageAsync(ResourceProject project, CancellationToken cancellationToken)
    {
        var request = new ResourceProjectVersionsRequest
        {
            Kind = options.Kind,
            Source = project.Source,
            ProjectId = project.ProjectId,
            Slug = project.Slug,
            MinecraftVersion = string.Empty,
            Loader = LoaderKind.Vanilla,
            IncludeAllVersions = true,
            Offset = NextPageOffset,
            PageSize = PageSize
        };
        var result = await resourceCatalogService!.GetProjectVersionsAsync(request, cancellationToken).ConfigureAwait(false);
        var accepted = AcceptPage(result.Versions, request.Offset, request.PageSize);
        return new AvailableVersionPage(result, accepted);
    }

    private IReadOnlyList<ResourceProjectVersion> AcceptPage(
        IReadOnlyList<ResourceProjectVersion> versions,
        int requestOffset,
        int pageSize)
    {
        var accepted = new List<ResourceProjectVersion>(versions.Count);
        foreach (var version in versions)
        {
            var key = string.IsNullOrWhiteSpace(version.VersionId)
                ? $"{version.FileName}|{version.VersionNumber}|{version.PublishedAt:O}"
                : version.VersionId;
            if (!string.IsNullOrWhiteSpace(key) && loadedVersionIds.Add(key))
                accepted.Add(version);
        }
        NextPageOffset = requestOffset + pageSize;
        return accepted;
    }

    private void ApplyInitial(AvailableVersionPage page, string error, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        SourceVersions = page.Versions.ToList();
        ApplyDefaultFilters(SourceVersions, SelectedTarget);
        RebuildList();
        LoadErrorMessage = error;
        HasMore = !page.Result.IsCurseForgeUnavailable && page.Result.HasMore;
        LoadMoreMessage = HasMore ? string.Empty : options.VersionsNoMoreText;
        IsLoading = false;
        IsLoadingMore = false;
        UpdateFooter();
        RaiseStateChanged();
    }

    private void ApplyMore(AvailableVersionPage page, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        SourceVersions = SourceVersions.Concat(page.Versions).ToList();
        UpdateFiltersPreservingSelection(SourceVersions);
        RebuildList(playEntranceAnimation: false);
        HasMore = !page.Result.IsCurseForgeUnavailable && page.Result.HasMore && page.Versions.Count > 0;
        LoadMoreMessage = HasMore ? string.Empty : options.VersionsNoMoreText;
        IsLoadingMore = false;
        UpdateFooter();
        RaiseStateChanged();
    }

    private void ResetVersions(bool resetFilters)
    {
        SourceVersions = [];
        NextPageOffset = 0;
        loadedVersionIds.Clear();
        ListItems.Clear();
        VisibleVersionCount = 0;
        HasMore = false;
        IsLoading = false;
        IsLoadingMore = false;
        LoadErrorMessage = string.Empty;
        LoadMoreMessage = string.Empty;
        if (resetFilters)
        {
            try
            {
                isApplyingFilters = true;
                SearchQuery = string.Empty;
                ResetFilterOptions();
            }
            finally
            {
                isApplyingFilters = false;
            }
        }
        RaiseStateChanged();
    }

    private void ResetFilterOptions()
    {
        VersionFilterOptions.Clear();
        VersionFilterOptions.Add(Builder.CreateAllVersionFilterOption());
        LoaderFilterOptions.Clear();
        foreach (var option in Builder.CreateDefaultLoaderFilterOptions())
            LoaderFilterOptions.Add(option);
        SelectedVersionFilter = VersionFilterOptions[0];
        SelectedLoaderFilter = LoaderFilterOptions[0];
    }

    private void ApplyDefaultFilters(
        IReadOnlyList<ResourceProjectVersion> versions,
        ResourcesModInstallTargetItemViewModel? target)
    {
        ApplyFilters(versions, Builder.ResolveDefaultVersionFilterId(target), Builder.ResolveDefaultLoaderFilterId(target));
    }

    private void UpdateFiltersPreservingSelection(IReadOnlyList<ResourceProjectVersion> versions)
    {
        ApplyFilters(versions, SelectedVersionFilter?.Id ?? "all", SelectedLoaderFilter?.Id ?? "all");
    }

    private void ApplyFilters(IReadOnlyList<ResourceProjectVersion> versions, string versionId, string loaderId)
    {
        try
        {
            isApplyingFilters = true;
            VersionFilterOptions.Clear();
            VersionFilterOptions.Add(Builder.CreateAllVersionFilterOption());
            foreach (var option in Builder.CreateVersionFilterOptions(versions))
                VersionFilterOptions.Add(option);
            LoaderFilterOptions.Clear();
            foreach (var option in Builder.CreateLoaderFilterOptions(versions))
                LoaderFilterOptions.Add(option);
            EnsureFilter(VersionFilterOptions, versionId, id => id);
            EnsureFilter(LoaderFilterOptions, loaderId, Builder.GetLoaderTitle);
            SelectedVersionFilter = VersionFilterOptions.First(option => string.Equals(option.Id, versionId, StringComparison.OrdinalIgnoreCase));
            SelectedLoaderFilter = LoaderFilterOptions.First(option => string.Equals(option.Id, loaderId, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            isApplyingFilters = false;
        }
    }

    private static void EnsureFilter(
        ICollection<ResourcesFilterOptionItem> values,
        string id,
        Func<string, string> titleFactory)
    {
        if (string.IsNullOrWhiteSpace(id) || values.Any(value => string.Equals(value.Id, id, StringComparison.OrdinalIgnoreCase)))
            return;
        values.Add(new ResourcesFilterOptionItem { Id = id, Title = titleFactory(id) });
    }

    private void RebuildList(bool playEntranceAnimation = true)
    {
        ListItems.Clear();
        var result = Builder.Build(
            SourceVersions,
            Title,
            currentProject,
            options.FallbackIconKey,
            SelectedVersionFilter?.Id,
            SelectedLoaderFilter?.Id,
            SearchQuery);
        foreach (var item in result.Items)
            ListItems.Add(item);
        VisibleVersionCount = result.VisibleVersionCount;
        if (playEntranceAnimation)
            ListEntranceAnimationToken++;
        UpdateFooter();
        RaiseStateChanged();
    }

    private void UpdateFooter()
    {
        for (var index = ListItems.Count - 1; index >= 0; index--)
        {
            if (ListItems[index] is ResourcesListFooterStatusItem)
                ListItems.RemoveAt(index);
        }
        if (CanShowLoadMoreState && !string.IsNullOrWhiteSpace(LoadMoreMessage))
            ListItems.Add(new ResourcesListFooterStatusItem(LoadMoreMessage));
    }

    private void RaiseTargetStateChanged()
    {
        OnPropertyChanged(nameof(HasTargetsLoadErrorMessage));
        OnPropertyChanged(nameof(CanShowTargetsLoadingState));
        OnPropertyChanged(nameof(CanShowTargetsLoadErrorState));
    }

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(HasLoadErrorMessage));
        OnPropertyChanged(nameof(HasFilters));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(CanShowLoadingState));
        OnPropertyChanged(nameof(CanShowEmptyState));
        OnPropertyChanged(nameof(CanShowLoadErrorState));
        OnPropertyChanged(nameof(CanShowLoadMoreState));
    }

    private void CancelTargetsLoad()
    {
        var cancellation = Interlocked.Exchange(ref targetsCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void CancelVersionsLoad()
    {
        var cancellation = Interlocked.Exchange(ref versionsCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private sealed record AvailableVersionPage(
        ResourceProjectVersionsResult Result,
        IReadOnlyList<ResourceProjectVersion> Versions);
}
