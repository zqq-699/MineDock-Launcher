/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Services;
using Launcher.App.ViewModels.Resources;
using Launcher.Application.Services;

namespace Launcher.Tests.ViewModels;

public sealed class ResourcesProjectListViewModelTests
{
    [Fact]
    public void FilterChangeRefreshesImmediately()
    {
        var service = new RecordingCatalogService();
        using var viewModel = CreateViewModel(service);

        viewModel.SelectedLoaderOption = viewModel.LoaderOptions[1];

        var request = Assert.Single(service.Requests);
        Assert.Equal(LoaderKind.Fabric, request.Loader);
    }

    [Fact]
    public async Task SearchDebounceOnlyQueriesLatestText()
    {
        var service = new RecordingCatalogService();
        using var viewModel = CreateViewModel(service);

        viewModel.SearchQuery = "ma";
        viewModel.SearchQuery = "map";

        Assert.Empty(service.Requests);
        await WaitUntilAsync(() => service.Requests.Count == 1);
        Assert.Equal("map", Assert.Single(service.Requests).Query);
    }

    [Fact]
    public void ConfirmingSeveralPendingFiltersIssuesOneRefresh()
    {
        var service = new RecordingCatalogService();
        using var viewModel = CreateViewModel(service);
        viewModel.OpenFilterDialogCommand.Execute(null);
        viewModel.PendingLoaderOption = viewModel.LoaderOptions[1];
        viewModel.PendingSourceOption = viewModel.SourceOptions[1];

        viewModel.ConfirmFilterDialogCommand.Execute(null);

        var request = Assert.Single(service.Requests);
        Assert.Equal(LoaderKind.Fabric, request.Loader);
        Assert.Equal(ResourceProjectSource.Modrinth, request.Source);
    }

    [Fact]
    public async Task CanceledOlderRequestCannotReplaceLatestResults()
    {
        var service = new ControlledCatalogService();
        using var viewModel = CreateViewModel(service);

        viewModel.SelectedLoaderOption = viewModel.LoaderOptions[1];
        viewModel.SelectedLoaderOption = viewModel.LoaderOptions[2];
        Assert.Equal(2, service.Searches.Count);

        service.Searches[1].Completion.SetResult(Result("new"));
        await WaitUntilAsync(() => viewModel.VisibleProjects.Count == 1);
        service.Searches[0].Completion.SetResult(Result("old"));
        await Task.Delay(50);

        Assert.Equal("new", Assert.Single(viewModel.VisibleProjects).Title);
    }

    private static ResourcesProjectListViewModel CreateViewModel(IResourceCatalogService service) =>
        new(CreateOptions(), service, null, ImmediateUiDispatcher.Instance, null);

    private static ResourcesOnlineProjectPageOptions CreateOptions() => new(
        Kind: ResourceProjectKind.Mod,
        Title: "Mods",
        FallbackIconKey: "mod",
        ShowsLoaderFilters: true,
        AllVersionsText: "All versions",
        AllLoadersText: "All loaders",
        ProjectsLoadingText: "Loading",
        ProjectsEmptyText: "Empty",
        ProjectsLoadErrorText: "Error",
        ProjectsLoadingMoreText: "Loading more",
        ProjectsNoMoreText: "No more",
        ProjectsLoadMoreErrorText: "Load more error",
        CurseForgeMissingApiKeyText: "Missing key",
        DetailsInfoSectionText: "Details",
        InstallTargetSectionText: "Target",
        InstallTargetLocalText: "Local",
        InstallTargetsLoadingText: "Loading targets",
        InstallTargetsLoadErrorText: "Target error",
        VersionsLoadingText: "Loading versions",
        VersionsEmptyText: "No versions",
        VersionsEmptyLocalText: "No local versions",
        VersionsFilterEmptyText: "No filtered versions",
        VersionsLoadErrorText: "Version error",
        VersionsLoadingMoreText: "Loading more versions",
        VersionsNoMoreText: "No more versions",
        VersionsLoadMoreErrorText: "Version load more error",
        VersionsAllTitleText: "All versions",
        DownloadDirectoryPickerTitle: "Download",
        DownloadingText: "Downloading",
        DownloadingFormat: "Downloading {0}",
        DownloadedFormat: "Downloaded {0}",
        DownloadFailedText: "Download failed",
        InstalledFormat: "Installed {0}",
        InstallFailedText: "Install failed",
        FileExistsMessageFormat: "Exists {0}",
        TypeOptions: []);

    private static ResourceCatalogSearchResult Result(string title) => new()
    {
        Projects =
        [
            new ResourceProject
            {
                ProjectId = title,
                Title = title,
                Kind = ResourceProjectKind.Mod,
                Source = ResourceProjectSource.Modrinth
            }
        ]
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private class RecordingCatalogService : IResourceCatalogService
    {
        public List<ResourceCatalogSearchRequest> Requests { get; } = [];

        public virtual Task<ResourceCatalogSearchResult> SearchProjectsAsync(
            ResourceCatalogSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ResourceCatalogSearchResult());
        }

        public Task<ResourceCatalogSearchResult> SearchModsAsync(
            ResourceCatalogSearchRequest request,
            CancellationToken cancellationToken = default) => SearchProjectsAsync(request, cancellationToken);

        public Task<ResourceProjectVersionsResult> GetProjectVersionsAsync(ResourceProjectVersionsRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResourceProjectVersionsResult());

        public Task<ResourceProjectDependenciesResult> GetProjectDependenciesAsync(ResourceProjectDependenciesRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResourceProjectDependenciesResult());

        public Task<string> InstallProjectVersionAsync(ResourceProjectVersion version, GameInstance instance, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public Task<string> DownloadProjectVersionAsync(ResourceProjectVersion version, string targetDirectory, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public Task<bool> ProjectVersionDownloadExistsAsync(ResourceProjectVersion version, string targetDirectory, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> ProjectVersionInstallExistsAsync(ResourceProjectVersion version, GameInstance instance, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class ControlledCatalogService : RecordingCatalogService
    {
        public List<ControlledSearch> Searches { get; } = [];

        public override Task<ResourceCatalogSearchResult> SearchProjectsAsync(
            ResourceCatalogSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var search = new ControlledSearch();
            Searches.Add(search);
            return search.Completion.Task;
        }
    }

    private sealed class ControlledSearch
    {
        public TaskCompletionSource<ResourceCatalogSearchResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
