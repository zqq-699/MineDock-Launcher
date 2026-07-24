/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Services;
using Launcher.App.ViewModels.Resources;
using Launcher.Application.Services;

namespace Launcher.Tests.ViewModels;

public sealed class ResourcesProjectDetailsViewModelTests
{
    [Fact]
    public async Task RelatedWebsiteLookupUsesProjectIdInsteadOfSlugAndPublishesResult()
    {
        var service = new ControlledCatalogService();
        using var viewModel = CreateViewModel(service, ResourceProjectKind.ResourcePack);
        var project = Item("stable-id", "display-slug", ResourceProjectKind.ResourcePack);

        viewModel.SelectRoot(project);

        var request = Assert.Single(service.RelatedWebsiteRequests);
        Assert.Equal("stable-id", request.Reference.ProjectId);
        request.Completion.SetResult(new ResourceProjectRelatedWebsite(
            ResourceProjectSource.Modrinth,
            "stable-id",
            "MCRES",
            "https://www.mcresource.cn/resourcepack/1"));
        await WaitUntilAsync(() => viewModel.HasRelatedWebsite);

        Assert.Equal("MCRES", viewModel.RelatedWebsite?.Name);
    }

    [Fact]
    public async Task NewSelectionCancelsAndCannotBeOverwrittenByOlderLookup()
    {
        var service = new ControlledCatalogService();
        using var viewModel = CreateViewModel(service, ResourceProjectKind.ResourcePack);
        var oldProject = Item("old", "old-slug", ResourceProjectKind.ResourcePack);
        var newProject = Item("new", "new-slug", ResourceProjectKind.ResourcePack);

        viewModel.SelectRoot(oldProject);
        var oldRequest = Assert.Single(service.RelatedWebsiteRequests);
        viewModel.SelectRoot(newProject);
        var newRequest = service.RelatedWebsiteRequests[1];

        Assert.True(oldRequest.CancellationToken.IsCancellationRequested);
        newRequest.Completion.SetResult(Website("new"));
        await WaitUntilAsync(() => viewModel.RelatedWebsite?.ProjectId == "new");
        oldRequest.Completion.SetResult(Website("old"));
        await Task.Delay(20);

        Assert.Equal("new", viewModel.RelatedWebsite?.ProjectId);
    }

    [Fact]
    public async Task NoMatchLeavesRelatedWebsiteHidden()
    {
        var service = new ControlledCatalogService();
        using var viewModel = CreateViewModel(service, ResourceProjectKind.ResourcePack);

        viewModel.SelectRoot(Item("missing", "missing", ResourceProjectKind.ResourcePack));
        Assert.False(viewModel.HasRelatedWebsite);
        service.RelatedWebsiteRequests[0].Completion.SetResult(null);
        await service.RelatedWebsiteRequests[0].Completion.Task;

        Assert.False(viewModel.HasRelatedWebsite);
        Assert.Null(viewModel.RelatedWebsite);
    }

    [Theory]
    [InlineData(ResourceProjectKind.Mod)]
    [InlineData(ResourceProjectKind.Modpack)]
    public void UnsupportedKindsDoNotRequestRelatedWebsite(ResourceProjectKind kind)
    {
        var service = new ControlledCatalogService();
        using var viewModel = CreateViewModel(service, kind);

        viewModel.SelectRoot(Item("project", "slug", kind));

        Assert.Empty(service.RelatedWebsiteRequests);
    }

    private static ResourcesProjectDetailsViewModel CreateViewModel(
        IResourceCatalogService service,
        ResourceProjectKind kind) =>
        new(CreateOptions(kind), service, ImmediateUiDispatcher.Instance, null);

    private static ResourcesModProjectItemViewModel Item(
        string projectId,
        string slug,
        ResourceProjectKind kind) =>
        new(
            new ResourceProject
            {
                ProjectId = projectId,
                Slug = slug,
                Title = projectId,
                Kind = kind,
                Source = ResourceProjectSource.Modrinth
            },
            fallbackIconKey: "resource",
            typeOptions: []);

    private static ResourceProjectRelatedWebsite Website(string projectId) =>
        new(
            ResourceProjectSource.Modrinth,
            projectId,
            "MCRES",
            $"https://www.mcresource.cn/resourcepack/{projectId}");

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
            await Task.Delay(10);
        Assert.True(condition());
    }

    private static ResourcesOnlineProjectPageOptions CreateOptions(ResourceProjectKind kind) => new(
        Kind: kind,
        Title: "Resources",
        FallbackIconKey: "resource",
        ShowsLoaderFilters: false,
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

    private sealed class ControlledCatalogService : IResourceCatalogService
    {
        public List<ControlledRelatedWebsiteRequest> RelatedWebsiteRequests { get; } = [];

        public Task<ResourceProjectRelatedWebsite?> GetRelatedWebsiteAsync(
            ResourceProjectReference reference,
            CancellationToken cancellationToken = default)
        {
            var request = new ControlledRelatedWebsiteRequest(reference, cancellationToken);
            RelatedWebsiteRequests.Add(request);
            return request.Completion.Task;
        }

        public Task<ResourceCatalogSearchResult> SearchModsAsync(
            ResourceCatalogSearchRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResourceCatalogSearchResult());

        public Task<ResourceProjectVersionsResult> GetProjectVersionsAsync(
            ResourceProjectVersionsRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResourceProjectVersionsResult());

        public Task<ResourceProjectDependenciesResult> GetProjectDependenciesAsync(
            ResourceProjectDependenciesRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResourceProjectDependenciesResult());

        public Task<string> InstallProjectVersionAsync(
            ResourceProjectVersion version,
            GameInstance instance,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public Task<string> DownloadProjectVersionAsync(
            ResourceProjectVersion version,
            string targetDirectory,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public Task<bool> ProjectVersionDownloadExistsAsync(
            ResourceProjectVersion version,
            string targetDirectory,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> ProjectVersionInstallExistsAsync(
            ResourceProjectVersion version,
            GameInstance instance,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed record ControlledRelatedWebsiteRequest(
        ResourceProjectReference Reference,
        CancellationToken CancellationToken)
    {
        public TaskCompletionSource<ResourceProjectRelatedWebsite?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
