using Launcher.Application.Services;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Domain.Models;
using Launcher.Tests.Fakes;

namespace Launcher.Tests.Resources;

public sealed class ResourcesPageViewModelTests
{

    [Fact]
    public void ModFiltersUseExpectedDefaults()
    {
        var viewModel = new ResourcesPageViewModel();

        Assert.Equal([Strings.Resources_ModFilterAllVersions], viewModel.ModPage.VersionOptions.Select(option => option.Title));
        Assert.Equal(
            [
                Strings.Resources_ModFilterAllLoaders,
                Strings.Download_FabricLoaderTitle,
                Strings.Download_ForgeLoaderTitle,
                Strings.Download_NeoForgeLoaderTitle,
                Strings.Download_QuiltLoaderTitle
            ],
            viewModel.ModPage.LoaderOptions.Select(option => option.Title));
        Assert.Equal(
            [
                Strings.Resources_ModFilterAllSources,
                Strings.Resources_ModSourceModrinth,
                Strings.Resources_ModSourceCurseForge
            ],
            viewModel.ModPage.SourceOptions.Select(option => option.Title));
        Assert.Equal(
            [
                Strings.Resources_ModFilterAllTypes,
                Strings.Resources_ModFilterTypeOptimization,
                Strings.Resources_ModFilterTypeUtility,
                Strings.Resources_ModFilterTypeAdventure,
                Strings.Resources_ModFilterTypeDecoration,
                Strings.Resources_ModFilterTypeEquipment,
                Strings.Resources_ModFilterTypeTechnology,
                Strings.Resources_ModFilterTypeMagic,
                Strings.Resources_ModFilterTypeMobs,
                Strings.Resources_ModFilterTypeWorldGeneration,
                Strings.Resources_ModFilterTypeStorage,
                Strings.Resources_ModFilterTypeLibrary
            ],
            viewModel.ModPage.TypeOptions.Select(option => option.Title));
        Assert.Same(viewModel.ModPage.VersionOptions[0], viewModel.ModPage.SelectedVersionOption);
        Assert.Same(viewModel.ModPage.LoaderOptions[0], viewModel.ModPage.SelectedLoaderOption);
        Assert.Same(viewModel.ModPage.SourceOptions[0], viewModel.ModPage.SelectedSourceOption);
        Assert.Same(viewModel.ModPage.TypeOptions[0], viewModel.ModPage.SelectedTypeOption);
    }

    [Fact]
    public async Task ResourcePackPageRefreshUsesResourcePackKindAndHidesLoaderFilters()
    {
        var service = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        var viewModel = new ResourcesPageViewModel(service);
        var resourcePacksSection = viewModel.Sections.Single(section => section.Id == "resource_packs");

        viewModel.SelectSectionCommand.Execute(resourcePacksSection);
        await viewModel.ResourcePacksPage.RefreshProjectsCommand.ExecuteAsync(null);

        Assert.Same(viewModel.ResourcePacksPage, viewModel.CurrentSectionViewModel);
        Assert.Same(viewModel.ResourcePacksPage, viewModel.CurrentOnlineProjectPage);
        Assert.True(viewModel.IsModSearchVisible);
        Assert.False(viewModel.ResourcePacksPage.ShowsLoaderFilters);
        Assert.Equal([Strings.Resources_ResourcePackFilterAllLoaders], viewModel.ResourcePacksPage.LoaderOptions.Select(option => option.Title));
        Assert.NotNull(service.LastRequest);
        Assert.Equal(ResourceProjectKind.ResourcePack, service.LastRequest.Kind);
        Assert.Equal(LoaderKind.Vanilla, service.LastRequest.Loader);
    }

    [Fact]
    public async Task ShaderPackPageRefreshUsesShaderPackKindAndHidesLoaderFilters()
    {
        var service = new FakeResourceCatalogService(new ResourceCatalogSearchResult
        {
            Projects =
            [
                new ResourceProject
                {
                    Kind = ResourceProjectKind.ShaderPack,
                    Source = ResourceProjectSource.Modrinth,
                    ProjectId = "complementary",
                    Slug = "complementary",
                    Title = "Complementary",
                    Description = "Nice shaders",
                    SupportedMinecraftVersions = ["1.20.1"],
                    Downloads = 2000,
                    ProjectUrl = "https://modrinth.com/shader/complementary"
                }
            ]
        });
        var viewModel = new ResourcesPageViewModel(service);
        var shaderPacksSection = viewModel.Sections.Single(section => section.Id == "shader_packs");

        viewModel.SelectSectionCommand.Execute(shaderPacksSection);
        await viewModel.ShaderPacksPage.RefreshProjectsCommand.ExecuteAsync(null);

        Assert.Same(viewModel.ShaderPacksPage, viewModel.CurrentSectionViewModel);
        Assert.Same(viewModel.ShaderPacksPage, viewModel.CurrentOnlineProjectPage);
        Assert.True(viewModel.IsModSearchVisible);
        Assert.False(viewModel.ShaderPacksPage.ShowsLoaderFilters);
        Assert.Equal([Strings.Resources_ShaderPackFilterAllLoaders], viewModel.ShaderPacksPage.LoaderOptions.Select(option => option.Title));
        Assert.Equal(Strings.Resources_ShaderPackProjectsLoading, viewModel.ShaderPacksPage.ProjectsLoadingMessage);
        Assert.Equal(Strings.Resources_ShaderPackDetailsInfoSection, viewModel.ShaderPacksPage.DetailsInfoSectionText);
        Assert.Equal("instance_setting_page/shader", viewModel.ShaderPacksPage.VisibleProjects[0].IconKey);
        Assert.NotNull(service.LastRequest);
        Assert.Equal(ResourceProjectKind.ShaderPack, service.LastRequest.Kind);
        Assert.Equal(LoaderKind.Vanilla, service.LastRequest.Loader);
    }

    [Fact]
    public async Task WorldPageRefreshUsesWorldKindAndCurseForgeOnly()
    {
        var service = new FakeResourceCatalogService(new ResourceCatalogSearchResult
        {
            Projects =
            [
                new ResourceProject
                {
                    Kind = ResourceProjectKind.World,
                    Source = ResourceProjectSource.CurseForge,
                    ProjectId = "1234",
                    Slug = "skyblock",
                    Title = "SkyBlock",
                    Description = "A world",
                    SupportedMinecraftVersions = ["1.20.1"],
                    Downloads = 2000,
                    ProjectUrl = "https://www.curseforge.com/minecraft/worlds/skyblock"
                }
            ]
        });
        var viewModel = new ResourcesPageViewModel(service);
        var worldsSection = viewModel.Sections.Single(section => section.Id == "worlds");

        viewModel.SelectSectionCommand.Execute(worldsSection);
        await viewModel.WorldsPage.RefreshProjectsCommand.ExecuteAsync(null);

        Assert.Same(viewModel.WorldsPage, viewModel.CurrentSectionViewModel);
        Assert.Same(viewModel.WorldsPage, viewModel.CurrentOnlineProjectPage);
        Assert.True(viewModel.IsModSearchVisible);
        Assert.False(viewModel.WorldsPage.ShowsLoaderFilters);
        Assert.False(viewModel.WorldsPage.ShowsSourceFilters);
        Assert.Equal([Strings.Resources_WorldFilterAllLoaders], viewModel.WorldsPage.LoaderOptions.Select(option => option.Title));
        Assert.Equal([Strings.Resources_ModSourceCurseForge], viewModel.WorldsPage.SourceOptions.Select(option => option.Title));
        Assert.Equal(Strings.Resources_WorldProjectsLoading, viewModel.WorldsPage.ProjectsLoadingMessage);
        Assert.Equal(Strings.Resources_WorldDetailsInfoSection, viewModel.WorldsPage.DetailsInfoSectionText);
        Assert.Equal("instance_setting_page/saves", viewModel.WorldsPage.VisibleProjects[0].IconKey);
        Assert.NotNull(service.LastRequest);
        Assert.Equal(ResourceProjectKind.World, service.LastRequest.Kind);
        Assert.Equal(ResourceProjectSource.CurseForge, service.LastRequest.Source);
        Assert.Equal(LoaderKind.Vanilla, service.LastRequest.Loader);
    }

    [Fact]
    public async Task ModpackPageRefreshUsesModpackKindAndShowsLoaderFilters()
    {
        var service = new FakeResourceCatalogService(new ResourceCatalogSearchResult
        {
            Projects =
            [
                new ResourceProject
                {
                    Kind = ResourceProjectKind.Modpack,
                    Source = ResourceProjectSource.Modrinth,
                    ProjectId = "pack",
                    Slug = "pack",
                    Title = "Pack",
                    Description = "A modpack",
                    SupportedMinecraftVersions = ["1.20.1"],
                    SupportedLoaders = ["fabric"],
                    Downloads = 2000,
                    ProjectUrl = "https://modrinth.com/modpack/pack"
                }
            ]
        });
        var viewModel = new ResourcesPageViewModel(service);
        var modpacksSection = viewModel.Sections.Single(section => section.Id == "modpacks");

        viewModel.SelectSectionCommand.Execute(modpacksSection);
        viewModel.ModpacksPage.SelectedLoaderOption = viewModel.ModpacksPage.LoaderOptions.Single(option => option.Id == "fabric");
        await viewModel.ModpacksPage.RefreshProjectsCommand.ExecuteAsync(null);

        Assert.Same(viewModel.ModpacksPage, viewModel.CurrentSectionViewModel);
        Assert.Same(viewModel.ModpacksPage, viewModel.CurrentOnlineProjectPage);
        Assert.True(viewModel.IsModSearchVisible);
        Assert.True(viewModel.ModpacksPage.ShowsLoaderFilters);
        Assert.True(viewModel.ModpacksPage.ShowsSourceFilters);
        Assert.Equal(
            [
                Strings.Resources_ModFilterAllSources,
                Strings.Resources_ModSourceModrinth,
                Strings.Resources_ModSourceCurseForge
            ],
            viewModel.ModpacksPage.SourceOptions.Select(option => option.Title));
        Assert.Equal(Strings.Resources_ModpackProjectsLoading, viewModel.ModpacksPage.ProjectsLoadingMessage);
        Assert.Equal(Strings.Resources_ModpackDetailsInfoSection, viewModel.ModpacksPage.DetailsInfoSectionText);
        Assert.Equal("general/general_extention", viewModel.ModpacksPage.VisibleProjects[0].IconKey);
        Assert.NotNull(service.LastRequest);
        Assert.Equal(ResourceProjectKind.Modpack, service.LastRequest.Kind);
        Assert.Equal(LoaderKind.Fabric, service.LastRequest.Loader);
    }

    [Fact]
    public async Task ModpackNewInstanceTargetDoesNotShowUnknownInstanceVersionDialog()
    {
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult())
        {
            VersionsResult = new ResourceProjectVersionsResult()
        };
        var viewModel = new ResourcesPageViewModel(catalogService);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Kind = ResourceProjectKind.Modpack,
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "pack",
            Title = "Pack"
        });

        viewModel.ModpacksPage.SelectProjectCommand.Execute(project);
        await TestAsync.WaitForAsync(() => viewModel.ModpacksPage.InstallTargets.Count == 2);
        viewModel.ModpacksPage.SelectInstallTargetCommand.Execute(viewModel.ModpacksPage.InstallTargets.Single(item => item.IsNewInstanceInstall));
        await TestAsync.WaitForAsync(() => !viewModel.ModpacksPage.IsLoadingAvailableVersions);

        Assert.Equal(ResourcesModPageStep.ProjectVersions, viewModel.ModpacksPage.CurrentStep);
        Assert.False(viewModel.ModpacksPage.IsUnknownInstanceVersionDialogOpen);
        Assert.NotNull(catalogService.LastVersionsRequest);
        Assert.True(catalogService.LastVersionsRequest.IncludeAllVersions);
        Assert.Equal(ResourceProjectKind.Modpack, catalogService.LastVersionsRequest.Kind);
        Assert.Equal(Strings.Resources_ModpackVersionsAllTitle, viewModel.ModpacksPage.AvailableVersionsTitle);
    }

    [Fact]
    public async Task ModpackVersionSelectionImportsAsNewInstance()
    {
        var importedInstance = CreateInstance("Imported Pack", "1.20.1", LoaderKind.Fabric);
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        var importService = new FakeLocalModpackImportService
        {
            ResultToReturn = ModpackImportResult.Success(importedInstance)
        };
        var statusService = new FakeStatusService();
        var downloadTasksPage = new DownloadTasksPageViewModel();
        var viewModel = new ResourcesPageViewModel(
            catalogService,
            statusService: statusService,
            downloadTasksPage: downloadTasksPage,
            localModpackImportService: importService);
        GameInstance? importedEventInstance = null;
        viewModel.ModpackImported += (_, instance) => importedEventInstance = instance;
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Kind = ResourceProjectKind.Modpack,
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "pack",
            Title = "Pack"
        });
        var version = new ResourceProjectVersion
        {
            Kind = ResourceProjectKind.Modpack,
            VersionId = "version-1",
            Name = "Pack 1.0",
            VersionNumber = "1.0.0",
            FileName = "pack.mrpack",
            PrimaryDownloadUrl = "https://example.test/pack.mrpack"
        };

        viewModel.ModpacksPage.SelectProjectCommand.Execute(project);
        await TestAsync.WaitForAsync(() => viewModel.ModpacksPage.InstallTargets.Count == 2);
        var target = viewModel.ModpacksPage.InstallTargets.Single(item => item.IsNewInstanceInstall);
        viewModel.ModpacksPage.SelectInstallTargetCommand.Execute(target);
        await viewModel.ModpacksPage.InstallAvailableVersionCommand.ExecuteAsync(new ResourcesModVersionItemViewModel(version, project));

        Assert.Same(version, catalogService.LastDownloadedVersion);
        Assert.NotNull(catalogService.LastDownloadDirectory);
        Assert.Contains("launcher-modpack-install-", catalogService.LastDownloadDirectory);
        Assert.Equal(1, importService.ImportCallCount);
        Assert.Equal(Path.Combine(catalogService.LastDownloadDirectory!, "pack.mrpack"), importService.LastArchivePath);
        Assert.False(Directory.Exists(catalogService.LastDownloadDirectory));
        Assert.Same(importedInstance, importedEventInstance);
        Assert.Null(catalogService.LastInstalledVersion);
        var task = Assert.Single(downloadTasksPage.Tasks);
        Assert.Equal(DownloadTaskState.Completed, task.State);
        Assert.Equal(string.Format(Strings.Status_ModpackImportedFormat, importedInstance.Name), task.StatusMessage);
        Assert.Contains(string.Format(Strings.Status_ModpackDownloadingFormat, "Pack 1.0"), statusService.Messages);
        Assert.Contains(string.Format(Strings.Status_ModpackImportedFormat, importedInstance.Name), statusService.Messages);
    }

    [Fact]
    public async Task ModpackVersionSelectionRequestsManualDownloadsDialogWhenImportIsPartial()
    {
        var importedInstance = CreateInstance("Partial Pack", "1.20.1", LoaderKind.Fabric);
        var manualDownload = new ManualModpackDownload
        {
            FileName = "missing.jar",
            DisplayName = "Missing Mod",
            FailureSummary = "Need manual download"
        };
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        var importService = new FakeLocalModpackImportService
        {
            ResultToReturn = ModpackImportResult.PartialSuccess(importedInstance, [manualDownload])
        };
        var downloadTasksPage = new DownloadTasksPageViewModel();
        var viewModel = new ResourcesPageViewModel(
            catalogService,
            downloadTasksPage: downloadTasksPage,
            localModpackImportService: importService);
        ResourcesModpackManualDownloadsRequestedEventArgs? request = null;
        viewModel.ModpackManualDownloadsRequested += (_, args) => request = args;
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Kind = ResourceProjectKind.Modpack,
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "pack",
            Title = "Pack"
        });
        var version = new ResourceProjectVersion
        {
            Kind = ResourceProjectKind.Modpack,
            VersionId = "version-1",
            Name = "Pack 1.0",
            VersionNumber = "1.0.0",
            FileName = "pack.mrpack",
            PrimaryDownloadUrl = "https://example.test/pack.mrpack"
        };

        viewModel.ModpacksPage.SelectProjectCommand.Execute(project);
        await TestAsync.WaitForAsync(() => viewModel.ModpacksPage.InstallTargets.Count == 2);
        viewModel.ModpacksPage.SelectInstallTargetCommand.Execute(viewModel.ModpacksPage.InstallTargets.Single(item => item.IsNewInstanceInstall));
        await viewModel.ModpacksPage.InstallAvailableVersionCommand.ExecuteAsync(new ResourcesModVersionItemViewModel(version, project));

        Assert.NotNull(request);
        Assert.Same(importedInstance, request.Instance);
        Assert.Equal([manualDownload], request.ManualDownloads);
        Assert.Equal(
            string.Format(Strings.Status_ModpackImportedWithManualDownloadsFormat, importedInstance.Name),
            Assert.Single(downloadTasksPage.Tasks).StatusMessage);
    }

    [Fact]
    public async Task ModpackVersionSelectionReportsFriendlyImportFailure()
    {
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        var importService = new FakeLocalModpackImportService
        {
            ResultToReturn = ModpackImportResult.Failure(ModpackImportFailureReason.InvalidManifest)
        };
        var statusService = new FakeStatusService();
        var floatingMessageService = new FakeFloatingMessageService();
        var downloadTasksPage = new DownloadTasksPageViewModel();
        var viewModel = new ResourcesPageViewModel(
            catalogService,
            statusService: statusService,
            floatingMessageService: floatingMessageService,
            downloadTasksPage: downloadTasksPage,
            localModpackImportService: importService);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Kind = ResourceProjectKind.Modpack,
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "pack",
            Title = "Pack"
        });
        var version = new ResourceProjectVersion
        {
            Kind = ResourceProjectKind.Modpack,
            VersionId = "version-1",
            Name = "Pack 1.0",
            VersionNumber = "1.0.0",
            FileName = "pack.mrpack",
            PrimaryDownloadUrl = "https://example.test/pack.mrpack"
        };

        viewModel.ModpacksPage.SelectProjectCommand.Execute(project);
        await TestAsync.WaitForAsync(() => viewModel.ModpacksPage.InstallTargets.Count == 2);
        viewModel.ModpacksPage.SelectInstallTargetCommand.Execute(viewModel.ModpacksPage.InstallTargets.Single(item => item.IsNewInstanceInstall));
        await viewModel.ModpacksPage.InstallAvailableVersionCommand.ExecuteAsync(new ResourcesModVersionItemViewModel(version, project));

        Assert.Equal(1, importService.ImportCallCount);
        Assert.Contains(Strings.Status_ModpackInvalidArchive, statusService.Messages);
        Assert.Contains(Strings.Status_ModpackInvalidArchive, floatingMessageService.Messages);
        var task = Assert.Single(downloadTasksPage.Tasks);
        Assert.Equal(DownloadTaskState.Failed, task.State);
        Assert.Equal(Strings.Status_ModpackInvalidArchive, task.StatusMessage);
        Assert.NotNull(catalogService.LastDownloadDirectory);
        Assert.False(Directory.Exists(catalogService.LastDownloadDirectory));
    }

    [Theory]
    [InlineData(LoaderKind.Fabric, "fabric")]
    [InlineData(LoaderKind.Forge, "forge")]
    [InlineData(LoaderKind.NeoForge, "neoforge")]
    [InlineData(LoaderKind.Quilt, "quilt")]
    public async Task OpenModsForInstanceAppliesKnownVersionAndLoaderFilters(
        LoaderKind loader,
        string expectedLoaderId)
    {
        var service = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        var versionService = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.18.2", "release", false)
        ]);
        var viewModel = new ResourcesPageViewModel(service, gameVersionService: versionService);
        viewModel.SelectSectionCommand.Execute(viewModel.Sections.Single(section => section.Id == "worlds"));
        viewModel.ModPage.CurrentStep = ResourcesModPageStep.ProjectDetails;
        var instance = CreateInstance("Fabric Pack", "1.18.2", loader);

        await viewModel.OpenModsForInstanceAsync(instance);

        Assert.True(viewModel.IsModsSection);
        Assert.Same(viewModel.ModPage, viewModel.CurrentSectionViewModel);
        Assert.Equal(ResourcesModPageStep.ProjectList, viewModel.ModPage.CurrentStep);
        Assert.Equal("1.18", viewModel.ModPage.SelectedVersionOption?.Id);
        Assert.Equal(expectedLoaderId, viewModel.ModPage.SelectedLoaderOption?.Id);
        Assert.True(service.CallCount >= 1);
        await TestAsync.WaitForAsync(() => service.Requests.Any(request => request.Kind == ResourceProjectKind.Mod));
        var modRequest = service.Requests.LastOrDefault(request => request.Kind == ResourceProjectKind.Mod);
        Assert.NotNull(modRequest);
        Assert.Equal(["1.18.2"], modRequest.MinecraftVersions);
        Assert.Equal("1.18.2", modRequest.MinecraftVersion);
        Assert.Equal(loader, modRequest.Loader);
    }

    [Fact]
    public async Task OpenModsForInstanceLeavesVersionFilterAllForUnknownMinecraftVersion()
    {
        var service = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        var versionService = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "release", false)
        ]);
        var viewModel = new ResourcesPageViewModel(service, gameVersionService: versionService);
        var instance = CreateInstance("Unknown Pack", "custom-1.18.2", LoaderKind.Fabric);

        await viewModel.OpenModsForInstanceAsync(instance);

        Assert.Equal("all", viewModel.ModPage.SelectedVersionOption?.Id);
        Assert.Equal("fabric", viewModel.ModPage.SelectedLoaderOption?.Id);
        Assert.Equal(1, service.CallCount);
        Assert.NotNull(service.LastRequest);
        Assert.Empty(service.LastRequest.MinecraftVersions);
        Assert.Equal(string.Empty, service.LastRequest.MinecraftVersion);
        Assert.Equal(LoaderKind.Fabric, service.LastRequest.Loader);
    }

    [Fact]
    public async Task ModPageRefreshProjectsLoadsVisibleProjects()
    {
        var service = new FakeResourceCatalogService(new ResourceCatalogSearchResult
        {
            Projects =
            [
                new ResourceProject
                {
                    Source = ResourceProjectSource.Modrinth,
                    ProjectId = "sodium",
                    Slug = "sodium",
                    Title = "Sodium",
                    Description = "Rendering optimization",
                    SupportedMinecraftVersions = ["1.20.1"],
                    SupportedLoaders = ["fabric"],
                    Downloads = 2000,
                    ProjectUrl = "https://modrinth.com/mod/sodium"
                }
            ]
        });
        var viewModel = new ResourcesPageViewModel(service);

        await viewModel.ModPage.RefreshProjectsCommand.ExecuteAsync(null);

        Assert.True(viewModel.ModPage.HasVisibleProjects);
        Assert.False(viewModel.ModPage.CanShowEmptyState);
        Assert.Equal("Sodium", viewModel.ModPage.VisibleProjects[0].Title);
        Assert.Equal($"1.20  fabric  {Strings.Resources_ModSourceModrinth}", viewModel.ModPage.VisibleProjects[0].Subtitle);
        Assert.Equal(string.Format(Strings.Resources_ModDownloadsFormat, "2,000"), viewModel.ModPage.VisibleProjects[0].TrailingText);
    }

    [Fact]
    public async Task ModPageLoadMoreAppendsNextPageUsingNextOffset()
    {
        var service = new QueueResourceCatalogService(
            CreateProjectResult(2, "first", hasMore: true),
            CreateProjectResult(1, "second", hasMore: false));
        var viewModel = new ResourcesPageViewModel(service);

        await viewModel.ModPage.RefreshProjectsCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.ModPage.VisibleProjects.Count);
        Assert.True(viewModel.ModPage.HasMoreProjects);
        Assert.Equal(0, service.Requests[0].Offset);
        Assert.Equal(20, service.Requests[0].PageSize);
        var entranceAnimationToken = viewModel.ModPage.ListEntranceAnimationToken;

        await viewModel.ModPage.LoadMoreProjectsCommand.ExecuteAsync(null);

        Assert.Equal(3, viewModel.ModPage.VisibleProjects.Count);
        Assert.Equal(["First 0", "First 1", "Second 0"], viewModel.ModPage.VisibleProjects.Select(project => project.Title));
        Assert.Equal(
            ["P:First 0", "P:First 1", "P:Second 0"],
            FormatProjectListItems(viewModel.ModPage.ProjectListItems));
        Assert.Equal(
            ["P:First 0", "P:First 1", "P:Second 0", $"F:{Strings.Resources_ModProjectsNoMore}"],
            FormatProjectListItemsWithFooter(viewModel.ModPage.ProjectListItems));
        Assert.False(viewModel.ModPage.HasMoreProjects);
        Assert.Equal(Strings.Resources_ModProjectsNoMore, viewModel.ModPage.LoadMoreMessage);
        Assert.Equal(entranceAnimationToken, viewModel.ModPage.ListEntranceAnimationToken);
        Assert.Equal(20, service.Requests[1].Offset);
        Assert.Equal(20, service.Requests[1].PageSize);
    }

    [Fact]
    public async Task ModPageLoadMoreFailureShowsFooterItemAndKeepsProjects()
    {
        var dispatcher = new QueueingUiDispatcher();
        var service = new ControlledResourceCatalogService();
        var viewModel = new ResourcesPageViewModel(service, uiDispatcher: dispatcher);

        var firstLoad = viewModel.ModPage.RefreshProjectsAsync();
        dispatcher.RunNext();
        var firstCall = await service.WaitForCallAsync(0);
        firstCall.SetResult(CreateProjectResult(1, "first", hasMore: true));
        await TestAsync.WaitForAsync(() => dispatcher.PendingCount == 1);
        dispatcher.RunNext();
        await firstLoad;

        var loadMore = viewModel.ModPage.LoadMoreProjectsAsync();
        dispatcher.RunNext();
        var secondCall = await service.WaitForCallAsync(1);
        secondCall.SetException(new InvalidOperationException("load more failed"));
        await TestAsync.WaitForAsync(() => dispatcher.PendingCount == 1);
        dispatcher.RunNext();
        await loadMore;

        Assert.Single(viewModel.ModPage.VisibleProjects);
        Assert.False(viewModel.ModPage.IsLoadingMoreProjects);
        Assert.Equal(Strings.Resources_ModProjectsLoadMoreError, viewModel.ModPage.LoadMoreMessage);
        Assert.Equal(
            ["P:First 0", $"F:{Strings.Resources_ModProjectsLoadMoreError}"],
            FormatProjectListItemsWithFooter(viewModel.ModPage.ProjectListItems));
    }

    [Fact]
    public async Task ModPageBackgroundLoadFailureShowsErrorAfterCompletion()
    {
        var dispatcher = new QueueingUiDispatcher();
        var pendingResult = new TaskCompletionSource<ResourceCatalogSearchResult>();
        var service = new FakeResourceCatalogService(pendingResult.Task);
        var viewModel = new ResourcesPageViewModel(service, uiDispatcher: dispatcher);

        viewModel.BeginEnsureCurrentSectionLoaded();

        Assert.True(viewModel.ModPage.IsLoadingProjects);
        dispatcher.RunNext();
        await TestAsync.WaitForAsync(() => service.CallCount == 1);
        pendingResult.SetException(new InvalidOperationException("network failed"));

        await TestAsync.WaitForAsync(() => dispatcher.PendingCount == 1);
        dispatcher.RunNext();
        await TestAsync.WaitForAsync(() =>
            !viewModel.ModPage.IsLoadingProjects
            && viewModel.ModPage.HasLoadErrorMessage);
        Assert.Equal(Strings.Resources_ModProjectsLoadError, viewModel.ModPage.LoadErrorMessage);
    }

    [Fact]
    public async Task ModPageRefreshDoesNotLetCanceledOlderResultOverwriteLatestResult()
    {
        var dispatcher = new QueueingUiDispatcher();
        var service = new ControlledResourceCatalogService();
        var viewModel = new ResourcesPageViewModel(service, uiDispatcher: dispatcher);

        var firstLoad = viewModel.ModPage.RefreshProjectsAsync();
        dispatcher.RunNext();
        var oldCall = await service.WaitForCallAsync(0);

        var secondLoad = viewModel.ModPage.RefreshProjectsAsync();
        dispatcher.RunNext();

        var newCall = await service.WaitForCallAsync(1);
        newCall.SetResult(CreateSingleProjectResult("new", "New Mod", 2000));
        await TestAsync.WaitForAsync(() => dispatcher.PendingCount >= 1);
        while (dispatcher.PendingCount > 0)
            dispatcher.RunNext();
        await TestAsync.WaitForAsync(() =>
            viewModel.ModPage.VisibleProjects.Count == 1
            && viewModel.ModPage.VisibleProjects[0].Title == "New Mod");

        oldCall.SetResult(CreateSingleProjectResult("old", "Old Mod", 100));
        await firstLoad;
        await secondLoad;

        Assert.Equal("New Mod", viewModel.ModPage.VisibleProjects.Single().Title);
    }

    [Fact]
    public async Task ModProjectDetailsLoadsInstallTargetsAndAppendsLocalDownload()
    {
        var instanceService = new FakeGameInstanceService(
        [
            new GameInstance
            {
                Id = "fabric-instance",
                Name = "Fabric Test",
                MinecraftVersion = "1.21.1",
                Loader = LoaderKind.Fabric,
                LoaderVersion = "0.16.9",
                VersionType = "release"
            },
            new GameInstance
            {
                Id = "vanilla-instance",
                Name = "Vanilla Test",
                MinecraftVersion = "1.20.1",
                Loader = LoaderKind.Vanilla,
                VersionType = "release"
            }
        ]);
        var viewModel = new ResourcesPageViewModel(gameInstanceService: instanceService);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "project",
            Title = "Project"
        });

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        await TestAsync.WaitForAsync(() => viewModel.ModPage.InstallTargets.Count == 2);

        Assert.Equal(1, instanceService.GetInstancesCallCount);
        Assert.Equal(["Fabric Test", Strings.Resources_ModInstallTargetLocal], viewModel.ModPage.InstallTargets.Select(target => target.Title));
        Assert.False(viewModel.ModPage.InstallTargets[0].IsLocalDownload);
        Assert.True(viewModel.ModPage.InstallTargets[1].IsLocalDownload);
    }

    [Fact]
    public async Task ModProjectDetailsLoadsRequiredDependenciesAndOpensDependencyDetails()
    {
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        catalogService.DependenciesResultsByProjectId["iris"] = new ResourceProjectDependenciesResult
        {
            RequiredProjects =
            [
                new ResourceProject
                {
                    Kind = ResourceProjectKind.Mod,
                    Source = ResourceProjectSource.Modrinth,
                    ProjectId = "fabric-api",
                    Slug = "fabric-api",
                    Title = "Fabric API",
                    Description = "Library",
                    SupportedMinecraftVersions = ["1.20.1"],
                    SupportedLoaders = ["fabric"],
                    Downloads = 100
                }
            ]
        };
        catalogService.DependenciesResultsByProjectId["fabric-api"] = new ResourceProjectDependenciesResult();
        var viewModel = new ResourcesPageViewModel(catalogService);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Kind = ResourceProjectKind.Mod,
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "iris",
            Slug = "iris",
            Title = "Iris Shaders"
        });

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        await TestAsync.WaitForAsync(() => viewModel.ModPage.RequiredDependencies.Count == 1);

        var dependency = Assert.Single(viewModel.ModPage.RequiredDependencies);
        Assert.Equal("Fabric API", dependency.Title);
        Assert.True(viewModel.ModPage.CanShowRequiredDependencies);
        Assert.NotNull(catalogService.LastDependenciesRequest);
        Assert.Equal("iris", catalogService.LastDependenciesRequest.ProjectId);

        viewModel.ModPage.OpenDependencyProjectCommand.Execute(dependency);
        await TestAsync.WaitForAsync(() => catalogService.LastDependenciesRequest?.ProjectId == "fabric-api");

        Assert.Equal(ResourcesModPageStep.ProjectDetails, viewModel.ModPage.CurrentStep);
        Assert.Equal("Fabric API", viewModel.ModPage.SelectedProject?.Title);
        Assert.Empty(viewModel.ModPage.RequiredDependencies);

        viewModel.ModPage.BackToProjectListCommand.Execute(null);

        Assert.Equal(ResourcesModPageStep.ProjectDetails, viewModel.ModPage.CurrentStep);
        Assert.Equal("Iris Shaders", viewModel.ModPage.SelectedProject?.Title);

        viewModel.ModPage.BackToProjectListCommand.Execute(null);

        Assert.Equal(ResourcesModPageStep.ProjectList, viewModel.ModPage.CurrentStep);
    }

    [Fact]
    public async Task ModInstallTargetSelectionLoadsAvailableVersionsForInstance()
    {
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult())
        {
            VersionsResult = new ResourceProjectVersionsResult
            {
                Versions =
                [
                    new ResourceProjectVersion
                    {
                        VersionId = "version-1",
                        Name = "Fabric 1.18.2",
                        VersionNumber = "1.0.0",
                        VersionType = "release",
                        FileName = "version-1.jar",
                        GameVersions = ["1.18.2"],
                        Loaders = ["fabric"]
                    },
                    new ResourceProjectVersion
                    {
                        VersionId = "version-2",
                        Name = "Forge 1.18.2",
                        VersionNumber = "1.0.1",
                        VersionType = "release",
                        FileName = "version-2.jar",
                        GameVersions = ["1.18.2"],
                        Loaders = ["forge"]
                    },
                    new ResourceProjectVersion
                    {
                        VersionId = "version-3",
                        Name = "Fabric 1.18.1",
                        VersionNumber = "1.0.2",
                        VersionType = "release",
                        FileName = "version-3.jar",
                        GameVersions = ["1.18.1"],
                        Loaders = ["fabric"]
                    }
                ]
            }
        };
        var instance = new GameInstance
        {
            Id = "fabric-instance",
            Name = "Fabric Test",
            MinecraftVersion = "1.18.2",
            Loader = LoaderKind.Fabric
        };
        var viewModel = new ResourcesPageViewModel(catalogService);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "modrinth-project",
            Slug = "modrinth-project",
            Title = "Project"
        });
        var target = ResourcesModInstallTargetItemViewModel.FromInstance(instance);

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        Assert.Equal("Project", viewModel.PageTitle);
        viewModel.ModPage.SelectInstallTargetCommand.Execute(target);
        await TestAsync.WaitForAsync(() => viewModel.ModPage.AvailableVersions.Count == 3);

        Assert.Equal(ResourcesModPageStep.ProjectVersions, viewModel.ModPage.CurrentStep);
        Assert.Equal("Fabric Test", viewModel.PageTitle);
        Assert.NotNull(catalogService.LastVersionsRequest);
        Assert.Equal(ResourceProjectSource.Modrinth, catalogService.LastVersionsRequest.Source);
        Assert.Equal("modrinth-project", catalogService.LastVersionsRequest.ProjectId);
        Assert.True(catalogService.LastVersionsRequest.IncludeAllVersions);
        Assert.Equal(string.Empty, catalogService.LastVersionsRequest.MinecraftVersion);
        Assert.Equal(LoaderKind.Vanilla, catalogService.LastVersionsRequest.Loader);
        Assert.Equal("1.18.2-fabric", viewModel.ModPage.AvailableVersionsTitle);
        Assert.Equal("1.18.2", viewModel.ModPage.SelectedAvailableVersionFilterOption?.Id);
        Assert.Equal("fabric", viewModel.ModPage.SelectedAvailableLoaderFilterOption?.Id);
        Assert.Equal(["H:1.18.2-fabric", "V:Fabric 1.18.2"], FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems));
        Assert.Equal("Fabric 1.18.2", viewModel.ModPage.AvailableVersions[0].Title);
        Assert.Equal(1, viewModel.ModPage.VisibleAvailableVersionCount);

        viewModel.ModPage.SelectedAvailableLoaderFilterOption = viewModel.ModPage.AvailableLoaderFilterOptions.Single(option => option.Id == "forge");

        Assert.Equal(["H:1.18.2-forge", "V:Forge 1.18.2"], FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems));
    }

    [Fact]
    public async Task ModAvailableVersionsLoadMoreAppendsNextPageUsingNextOffset()
    {
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        catalogService.VersionsResultsByOffset[0] = new ResourceProjectVersionsResult
        {
            HasMore = true,
            Versions =
            [
                new ResourceProjectVersion
                {
                    VersionId = "version-1",
                    Name = "Forge 1.18.2",
                    VersionNumber = "1.0.0",
                    VersionType = "release",
                    FileName = "forge-1.18.2.jar",
                    GameVersions = ["1.18.2"],
                    Loaders = ["forge"]
                }
            ]
        };
        catalogService.VersionsResultsByOffset[10000] = new ResourceProjectVersionsResult
        {
            Versions =
            [
                new ResourceProjectVersion
                {
                    VersionId = "version-2",
                    Name = "Forge 1.18.1",
                    VersionNumber = "1.0.1",
                    VersionType = "release",
                    FileName = "forge-1.18.1.jar",
                    GameVersions = ["1.18.1"],
                    Loaders = ["forge"]
                }
            ]
        };
        var viewModel = new ResourcesPageViewModel(catalogService);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.CurseForge,
            ProjectId = "1234",
            Slug = "project",
            Title = "Project"
        });

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectInstallTargetCommand.Execute(ResourcesModInstallTargetItemViewModel.CreateLocalDownload());
        await TestAsync.WaitForAsync(() => viewModel.ModPage.AvailableVersions.Count == 1);

        Assert.True(viewModel.ModPage.HasMoreAvailableVersions);
        Assert.Equal(0, Assert.Single(catalogService.VersionRequests).Offset);
        var entranceAnimationToken = viewModel.ModPage.AvailableVersionListEntranceAnimationToken;
        var visibleItemsBeforeLoadMore = FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems);

        await viewModel.ModPage.LoadMoreAvailableVersionsAsync();

        Assert.Equal(2, viewModel.ModPage.AvailableVersions.Count);
        Assert.Equal([0, 10000], catalogService.VersionRequests.Select(request => request.Offset).ToArray());
        Assert.All(catalogService.VersionRequests, request => Assert.Equal(10000, request.PageSize));
        Assert.All(catalogService.VersionRequests, request => Assert.True(request.IncludeAllVersions));
        Assert.False(viewModel.ModPage.HasMoreAvailableVersions);
        Assert.Equal(entranceAnimationToken, viewModel.ModPage.AvailableVersionListEntranceAnimationToken);
        Assert.Equal(Strings.Resources_ModVersionsNoMore, viewModel.ModPage.AvailableVersionsLoadMoreMessage);
        Assert.Equal(
            $"F:{Strings.Resources_ModVersionsNoMore}",
            FormatAvailableVersionListItemsWithFooter(viewModel.ModPage.AvailableVersionListItems).Last());
        Assert.Equal(
            visibleItemsBeforeLoadMore,
            FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems).Take(visibleItemsBeforeLoadMore.Count).ToArray());
        Assert.Equal(
            [
                "H:1.18.2-forge",
                "V:Forge 1.18.2",
                "H:1.18.1-forge",
                "V:Forge 1.18.1"
            ],
            FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems));
    }

    [Fact]
    public async Task CurseForgeAvailableVersionsDeduplicateAcrossLargePages()
    {
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        catalogService.VersionsResultsByOffset[0] = new ResourceProjectVersionsResult
        {
            HasMore = true,
            Versions =
            [
                new ResourceProjectVersion
                {
                    VersionId = "duplicate-version",
                    Name = "Forge 1.18.2",
                    VersionNumber = "1.0.0",
                    VersionType = "release",
                    FileName = "forge-1.18.2.jar",
                    GameVersions = ["1.18.2"],
                    Loaders = ["forge"]
                }
            ]
        };
        catalogService.VersionsResultsByOffset[10000] = new ResourceProjectVersionsResult
        {
            Versions =
            [
                new ResourceProjectVersion
                {
                    VersionId = "duplicate-version",
                    Name = "Forge Duplicate",
                    VersionNumber = "1.0.0",
                    VersionType = "release",
                    FileName = "forge-duplicate.jar",
                    GameVersions = ["1.18.1"],
                    Loaders = ["forge"]
                },
                new ResourceProjectVersion
                {
                    VersionId = "new-version",
                    Name = "Forge 1.18.0",
                    VersionNumber = "1.0.1",
                    VersionType = "release",
                    FileName = "forge-1.18.0.jar",
                    GameVersions = ["1.18.0"],
                    Loaders = ["forge"]
                }
            ]
        };
        var viewModel = new ResourcesPageViewModel(catalogService);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.CurseForge,
            ProjectId = "1234",
            Slug = "project",
            Title = "Project"
        });

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectInstallTargetCommand.Execute(ResourcesModInstallTargetItemViewModel.CreateLocalDownload());
        await TestAsync.WaitForAsync(() => viewModel.ModPage.AvailableVersions.Count == 1);

        await viewModel.ModPage.LoadMoreAvailableVersionsAsync();

        Assert.Equal(2, viewModel.ModPage.AvailableVersions.Count);
        Assert.Equal(["Forge 1.18.2", "Forge 1.18.0"], viewModel.ModPage.AvailableVersions.Select(version => version.Title).ToArray());
        Assert.False(viewModel.ModPage.HasMoreAvailableVersions);
    }

    [Fact]
    public async Task ModInstallTargetSelectionShowsDialogAndLoadsAllVersionsForUnknownInstanceVersion()
    {
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        var viewModel = new ResourcesPageViewModel(catalogService);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "project",
            Slug = "project",
            Title = "Project"
        });
        var instance = new GameInstance
        {
            Id = "unknown-version-instance",
            Name = "Unknown Version",
            MinecraftVersion = string.Empty,
            Loader = LoaderKind.Fabric
        };

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectInstallTargetCommand.Execute(ResourcesModInstallTargetItemViewModel.FromInstance(instance));
        await TestAsync.WaitForAsync(() => !viewModel.ModPage.IsLoadingAvailableVersions);

        Assert.Equal(ResourcesModPageStep.ProjectVersions, viewModel.ModPage.CurrentStep);
        Assert.True(viewModel.ModPage.IsUnknownInstanceVersionDialogOpen);
        Assert.NotNull(catalogService.LastVersionsRequest);
        Assert.True(catalogService.LastVersionsRequest.IncludeAllVersions);
        Assert.Equal(string.Empty, catalogService.LastVersionsRequest.MinecraftVersion);
        Assert.Equal(LoaderKind.Vanilla, catalogService.LastVersionsRequest.Loader);
        Assert.Equal(Strings.Resources_ModVersionsAllTitle, viewModel.ModPage.AvailableVersionsTitle);
        Assert.Equal([$"H:{Strings.Resources_ModVersionsAllTitle}"], FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems));
        Assert.Equal(Strings.Resources_ModVersionsEmptyLocal, viewModel.ModPage.AvailableVersionsEmptyMessage);
        Assert.True(viewModel.ModPage.CanShowAvailableVersionsEmptyState);

        viewModel.ModPage.CloseUnknownInstanceVersionDialogCommand.Execute(null);

        Assert.False(viewModel.ModPage.IsUnknownInstanceVersionDialogOpen);
    }

    [Fact]
    public async Task UnknownInstanceVersionStillInstallsSelectedVersionToInstance()
    {
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        var viewModel = new ResourcesPageViewModel(catalogService);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "project",
            Title = "Project"
        });
        var instance = new GameInstance
        {
            Id = "unknown-version-instance",
            Name = "Unknown Version",
            MinecraftVersion = string.Empty,
            Loader = LoaderKind.Fabric
        };
        var version = new ResourceProjectVersion
        {
            VersionId = "version-1",
            Name = "Version 1",
            VersionNumber = "1.0.0",
            VersionType = "release",
            FileName = "version-1.jar",
            PrimaryDownloadUrl = "https://example.test/version-1.jar"
        };

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectInstallTargetCommand.Execute(ResourcesModInstallTargetItemViewModel.FromInstance(instance));
        await TestAsync.WaitForAsync(() => catalogService.LastVersionsRequest is not null);
        await viewModel.ModPage.InstallAvailableVersionCommand.ExecuteAsync(new ResourcesModVersionItemViewModel(version, project));

        Assert.Same(version, catalogService.LastInstalledVersion);
        Assert.Same(instance, catalogService.LastInstallInstance);
        Assert.Null(catalogService.LastDownloadedVersion);
    }

    [Fact]
    public async Task ModVersionSelectionInstallsVersionToSelectedInstance()
    {
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        var statusService = new FakeStatusService();
        var downloadTasksPage = new DownloadTasksPageViewModel();
        var instance = new GameInstance
        {
            Id = "fabric-instance",
            Name = "Fabric Test",
            MinecraftVersion = "1.18.2",
            Loader = LoaderKind.Fabric,
            InstanceDirectory = "C:\\Instances\\Fabric Test"
        };
        var version = new ResourceProjectVersion
        {
            VersionId = "version-1",
            Name = "Version 1",
            VersionNumber = "1.0.0",
            FileName = "version-1.jar",
            PrimaryDownloadUrl = "https://example.test/version-1.jar"
        };
        var viewModel = new ResourcesPageViewModel(
            catalogService,
            statusService: statusService,
            downloadTasksPage: downloadTasksPage);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "project",
            Title = "Project"
        });
        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectInstallTargetCommand.Execute(ResourcesModInstallTargetItemViewModel.FromInstance(instance));

        await viewModel.ModPage.InstallAvailableVersionCommand.ExecuteAsync(new ResourcesModVersionItemViewModel(version, project));

        Assert.Same(version, catalogService.LastInstalledVersion);
        Assert.Same(instance, catalogService.LastInstallInstance);
        var task = Assert.Single(downloadTasksPage.Tasks);
        Assert.Equal("Version 1", task.Title);
        Assert.Equal("Fabric Test", task.Subtitle);
        Assert.Equal(DownloadTaskState.Completed, task.State);
        Assert.Equal(100, task.ProgressPercent);
        Assert.Contains(string.Format(Strings.Status_ModDownloadingFormat, "Version 1"), statusService.Messages);
        Assert.Contains(string.Format(Strings.Status_ModInstalledFormat, "Project"), statusService.Messages);
    }

    [Fact]
    public async Task ModVersionSelectionShowsDialogWhenInstalledRequiredDependencyVersionIsLowerThanMinimum()
    {
        var dependency = CreateDependency("fabric-api", "Fabric API", "api-version");
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        catalogService.VersionsResultsByProjectId["fabric-api"] = new ResourceProjectVersionsResult
        {
            Versions =
            [
                new ResourceProjectVersion
                {
                    Kind = ResourceProjectKind.Mod,
                    VersionId = "api-version",
                    Name = "Fabric API 1.0",
                    VersionNumber = "1.0.0",
                    VersionType = "release",
                    FileName = "fabric-api.jar"
                }
            ]
        };
        var modService = new FakeModService();
        var instance = CreateInstance("Fabric Test", "1.20.1", LoaderKind.Fabric);
        modService.ModsByInstanceId[instance.Id] =
        [
            new LocalMod
            {
                Name = "Fabric API",
                ModId = "fabric-api",
                Version = "0.9.0",
                FileName = "fabric-api.jar",
                FullPath = "C:\\Instances\\Fabric Test\\mods\\fabric-api.jar",
                IsEnabled = true
            }
        ];
        var viewModel = new ResourcesPageViewModel(catalogService, modService: modService);
        var project = CreateModProjectItem("iris", "Iris");
        var version = new ResourcesModVersionItemViewModel(new ResourceProjectVersion
        {
            Kind = ResourceProjectKind.Mod,
            VersionId = "iris-version",
            Name = "Iris 1.0",
            FileName = "iris.jar",
            RequiredDependencies = [dependency]
        }, project);

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectedInstallTarget = ResourcesModInstallTargetItemViewModel.FromInstance(instance);
        var installTask = viewModel.ModPage.InstallAvailableVersionCommand.ExecuteAsync(version);
        await TestAsync.WaitForAsync(() => viewModel.ModPage.IsRequiredDependenciesDialogOpen);

        var dialogItem = Assert.Single(viewModel.ModPage.RequiredDependencyDialogItems);
        Assert.Equal("Fabric API", dialogItem.Title);
        Assert.False(dialogItem.IsInstalled);
        Assert.Equal(Strings.Resources_ModRequiredDependencyUpdateRequired, dialogItem.StateText);

        viewModel.ModPage.CancelRequiredDependenciesDialogCommand.Execute(null);
        await installTask;

        Assert.Null(catalogService.LastInstalledVersion);
    }

    [Fact]
    public async Task ModVersionSelectionMissingRequiredDependencyDialogCanCancelInstall()
    {
        var dependency = CreateDependency("fabric-api", "Fabric API", "api-version");
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        catalogService.VersionsResultsByProjectId["fabric-api"] = new ResourceProjectVersionsResult
        {
            Versions =
            [
                new ResourceProjectVersion
                {
                    Kind = ResourceProjectKind.Mod,
                    VersionId = "api-version",
                    Name = "Fabric API 1.0",
                    VersionNumber = "1.0.0",
                    VersionType = "release",
                    FileName = "fabric-api.jar"
                }
            ]
        };
        var modService = new FakeModService();
        var instance = CreateInstance("Fabric Test", "1.20.1", LoaderKind.Fabric);
        modService.ModsByInstanceId[instance.Id] =
        [
            new LocalMod
            {
                Name = "Fabric API",
                ModId = "fabric-api",
                FileName = "fabric-api.jar.disabled",
                FullPath = "C:\\Instances\\Fabric Test\\mods\\fabric-api.jar.disabled",
                IsEnabled = false
            }
        ];
        var viewModel = new ResourcesPageViewModel(catalogService, modService: modService);
        var project = CreateModProjectItem("iris", "Iris");
        var version = new ResourcesModVersionItemViewModel(new ResourceProjectVersion
        {
            Kind = ResourceProjectKind.Mod,
            VersionId = "iris-version",
            Name = "Iris 1.0",
            FileName = "iris.jar",
            RequiredDependencies = [dependency]
        }, project);

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectedInstallTarget = ResourcesModInstallTargetItemViewModel.FromInstance(instance);
        var installTask = viewModel.ModPage.InstallAvailableVersionCommand.ExecuteAsync(version);
        await TestAsync.WaitForAsync(() => viewModel.ModPage.IsRequiredDependenciesDialogOpen);

        var dialogItem = Assert.Single(viewModel.ModPage.RequiredDependencyDialogItems);
        Assert.Equal("Fabric API", dialogItem.Title);
        Assert.Equal(string.Format(Strings.Resources_ModRequiredDependencyVersionFormat, "Fabric API 1.0 1.0.0"), dialogItem.VersionText);
        Assert.False(dialogItem.IsInstalled);
        Assert.Equal(Strings.Resources_ModRequiredDependencyMissing, dialogItem.StateText);

        viewModel.ModPage.CancelRequiredDependenciesDialogCommand.Execute(null);
        await installTask;

        Assert.Null(catalogService.LastInstalledVersion);
        Assert.False(viewModel.ModPage.IsRequiredDependenciesDialogOpen);
    }

    [Fact]
    public async Task CurseForgeModVersionSelectionAutoInstallsMissingRequiredDependency()
    {
        var dependency = CreateDependency(
            "222",
            "Library Mod",
            source: ResourceProjectSource.CurseForge);
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        catalogService.VersionsResultsByProjectId["222"] = new ResourceProjectVersionsResult
        {
            Versions =
            [
                new ResourceProjectVersion
                {
                    Kind = ResourceProjectKind.Mod,
                    VersionId = "library-beta",
                    Name = "Library 1.1 Beta",
                    VersionNumber = "1.1.0-beta.1",
                    VersionType = "beta",
                    FileName = "library-beta.jar"
                },
                new ResourceProjectVersion
                {
                    Kind = ResourceProjectKind.Mod,
                    VersionId = "library-release",
                    Name = "Library 1.0",
                    VersionNumber = "1.0.0",
                    VersionType = "release",
                    FileName = "library.jar"
                }
            ]
        };
        var viewModel = new ResourcesPageViewModel(
            catalogService,
            downloadTasksPage: new DownloadTasksPageViewModel(),
            modService: new FakeModService());
        var instance = CreateInstance("Fabric Test", "1.20.1", LoaderKind.Fabric);
        var project = CreateModProjectItem("1234", "Main Mod", ResourceProjectSource.CurseForge);
        var version = new ResourcesModVersionItemViewModel(new ResourceProjectVersion
        {
            Kind = ResourceProjectKind.Mod,
            VersionId = "main-version",
            Name = "Main Mod 1.0",
            FileName = "main.jar",
            RequiredDependencies = [dependency]
        }, project);

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectedInstallTarget = ResourcesModInstallTargetItemViewModel.FromInstance(instance);
        var installTask = viewModel.ModPage.InstallAvailableVersionCommand.ExecuteAsync(version);
        await TestAsync.WaitForAsync(() => viewModel.ModPage.IsRequiredDependenciesDialogOpen);

        var dialogItem = Assert.Single(viewModel.ModPage.RequiredDependencyDialogItems);
        Assert.Equal("Library Mod", dialogItem.Title);
        Assert.Equal(Strings.Resources_ModRequiredDependencyMissing, dialogItem.StateText);
        Assert.Contains("Library 1.0", dialogItem.InstallVersionText);
        Assert.NotNull(catalogService.LastVersionsRequest);
        Assert.Equal(ResourceProjectSource.CurseForge, catalogService.LastVersionsRequest.Source);

        viewModel.ModPage.AutoInstallRequiredDependenciesCommand.Execute(null);
        await installTask;

        Assert.Equal(["library-release", "main-version"], catalogService.InstalledVersions.Select(version => version.VersionId));
    }

    [Fact]
    public async Task ModVersionSelectionAutoInstallsMissingRequiredDependencyThroughDispatcher()
    {
        var dependency = CreateDependency("fabric-api", "Fabric API", "api-version");
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        catalogService.VersionsResultsByProjectId["fabric-api"] = new ResourceProjectVersionsResult
        {
            Versions =
            [
                new ResourceProjectVersion
                {
                    Kind = ResourceProjectKind.Mod,
                    VersionId = "api-version",
                    Name = "Fabric API 1.0",
                    VersionNumber = "1.0.0",
                    VersionType = "release",
                    FileName = "fabric-api.jar"
                }
            ]
        };
        var dispatcher = new RecordingUiDispatcher(hasAccess: false);
        var statusService = new FakeStatusService();
        var downloadTasksPage = new DownloadTasksPageViewModel(dispatcher);
        var taskStatusMessages = new List<string>();
        downloadTasksPage.TaskStarted += (_, task) =>
        {
            task.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(DownloadTaskItem.StatusMessage))
                    taskStatusMessages.Add(task.StatusMessage);
            };
        };
        var viewModel = new ResourcesPageViewModel(
            catalogService,
            uiDispatcher: dispatcher,
            statusService: statusService,
            downloadTasksPage: downloadTasksPage,
            modService: new FakeModService());
        var instance = CreateInstance("Fabric Test", "1.20.1", LoaderKind.Fabric);
        var project = CreateModProjectItem("iris", "Iris");
        var version = new ResourcesModVersionItemViewModel(new ResourceProjectVersion
        {
            Kind = ResourceProjectKind.Mod,
            VersionId = "iris-version",
            Name = "Iris 1.0",
            FileName = "iris.jar",
            RequiredDependencies = [dependency]
        }, project);

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectedInstallTarget = ResourcesModInstallTargetItemViewModel.FromInstance(instance);
        var installTask = viewModel.ModPage.InstallAvailableVersionCommand.ExecuteAsync(version);
        await TestAsync.WaitForAsync(() => viewModel.ModPage.IsRequiredDependenciesDialogOpen);

        viewModel.ModPage.AutoInstallRequiredDependenciesCommand.Execute(null);
        await installTask;

        Assert.Equal(["api-version", "iris-version"], catalogService.InstalledVersions.Select(version => version.VersionId));
        var task = Assert.Single(downloadTasksPage.Tasks);
        Assert.Equal(DownloadTaskState.Completed, task.State);
        Assert.Equal(100, task.ProgressPercent);
        Assert.Contains(string.Format(Strings.Status_ModRequiredDependencyInstallingFormat, "Fabric API"), statusService.Messages);
        Assert.Contains(string.Format(Strings.Status_ModRequiredDependencyInstallingFormat, "Fabric API"), taskStatusMessages);
        Assert.Contains(string.Format(Strings.Status_ModInstalledFormat, "Iris"), taskStatusMessages);
        Assert.True(dispatcher.InvokeCount > 0);
    }

    [Fact]
    public async Task ModVersionSelectionAutoInstallStopsWhenDependencyHasNoCompatibleVersion()
    {
        var dependency = CreateDependency("fabric-api", "Fabric API", "api-version");
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        catalogService.VersionsResultsByProjectId["fabric-api"] = new ResourceProjectVersionsResult();
        var statusService = new FakeStatusService();
        var viewModel = new ResourcesPageViewModel(
            catalogService,
            statusService: statusService,
            downloadTasksPage: new DownloadTasksPageViewModel(),
            modService: new FakeModService());
        var instance = CreateInstance("Fabric Test", "1.20.1", LoaderKind.Fabric);
        var project = CreateModProjectItem("iris", "Iris");
        var version = new ResourcesModVersionItemViewModel(new ResourceProjectVersion
        {
            Kind = ResourceProjectKind.Mod,
            VersionId = "iris-version",
            Name = "Iris 1.0",
            FileName = "iris.jar",
            RequiredDependencies = [dependency]
        }, project);

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectedInstallTarget = ResourcesModInstallTargetItemViewModel.FromInstance(instance);
        var installTask = viewModel.ModPage.InstallAvailableVersionCommand.ExecuteAsync(version);
        await TestAsync.WaitForAsync(() => viewModel.ModPage.IsRequiredDependenciesDialogOpen);

        Assert.Equal(
            string.Format(Strings.Resources_ModRequiredDependencyVersionFormat, Strings.Resources_ModRequiredDependencyVersionUnresolved),
            Assert.Single(viewModel.ModPage.RequiredDependencyDialogItems).VersionText);

        viewModel.ModPage.AutoInstallRequiredDependenciesCommand.Execute(null);
        await installTask;

        Assert.Empty(catalogService.InstalledVersions);
        Assert.Contains(
            string.Format(Strings.Status_ModRequiredDependenciesAutoInstallFailedFormat, "Fabric API"),
            statusService.Messages);
    }

    [Fact]
    public async Task ModVersionSelectionDownloadsVersionToPickedLocalFolder()
    {
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        var statusService = new FakeStatusService();
        var filePickerService = new FakeFilePickerService { FolderPath = "C:\\Downloads" };
        var floatingMessageService = new FakeFloatingMessageService();
        var downloadTasksPage = new DownloadTasksPageViewModel();
        var viewModel = new ResourcesPageViewModel(
            catalogService,
            statusService: statusService,
            filePickerService: filePickerService,
            floatingMessageService: floatingMessageService,
            downloadTasksPage: downloadTasksPage);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "project",
            Title = "Project"
        });
        var version = new ResourceProjectVersion
        {
            VersionId = "version-1",
            Name = "Version 1",
            VersionNumber = "1.0.0",
            FileName = "version-1.jar",
            PrimaryDownloadUrl = "https://example.test/version-1.jar"
        };

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectInstallTargetCommand.Execute(ResourcesModInstallTargetItemViewModel.CreateLocalDownload());
        await viewModel.ModPage.InstallAvailableVersionCommand.ExecuteAsync(new ResourcesModVersionItemViewModel(version, project));

        Assert.Same(version, catalogService.LastDownloadedVersion);
        Assert.Equal("C:\\Downloads", catalogService.LastDownloadDirectory);
        var task = Assert.Single(downloadTasksPage.Tasks);
        Assert.Equal("Version 1", task.Title);
        Assert.Equal("C:\\Downloads", task.Subtitle);
        Assert.Equal(DownloadTaskState.Completed, task.State);
        Assert.Equal(100, task.ProgressPercent);
        Assert.Equal(Strings.FilePicker_ModDownloadDirectoryTitle, filePickerService.LastFolderPickerTitle);
        Assert.Contains(string.Format(Strings.Status_ModDownloadingFormat, "Version 1"), statusService.Messages);
        Assert.Contains(string.Format(Strings.Status_ModDownloadedFormat, "version-1.jar"), statusService.Messages);
        Assert.Contains(Strings.Status_ModDownloading, floatingMessageService.Messages);
    }

    [Fact]
    public async Task ModVersionSelectionShowsDialogWhenLocalFileAlreadyExists()
    {
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult())
        {
            DownloadExists = true
        };
        var statusService = new FakeStatusService();
        var floatingMessageService = new FakeFloatingMessageService();
        var downloadTasksPage = new DownloadTasksPageViewModel();
        var viewModel = new ResourcesPageViewModel(
            catalogService,
            statusService: statusService,
            filePickerService: new FakeFilePickerService { FolderPath = "C:\\Downloads" },
            floatingMessageService: floatingMessageService,
            downloadTasksPage: downloadTasksPage);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "project",
            Title = "Project"
        });
        var version = new ResourceProjectVersion
        {
            VersionId = "version-1",
            Name = "Version 1",
            VersionNumber = "1.0.0",
            FileName = "version-1.jar",
            PrimaryDownloadUrl = "https://example.test/version-1.jar"
        };

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectInstallTargetCommand.Execute(ResourcesModInstallTargetItemViewModel.CreateLocalDownload());
        await viewModel.ModPage.InstallAvailableVersionCommand.ExecuteAsync(new ResourcesModVersionItemViewModel(version, project));

        Assert.True(viewModel.ModPage.IsProjectVersionFileExistsDialogOpen);
        Assert.Contains("version-1.jar", viewModel.ModPage.ProjectVersionFileExistsDialogMessage);
        Assert.Null(catalogService.LastDownloadedVersion);
        Assert.Empty(downloadTasksPage.Tasks);
        Assert.Empty(floatingMessageService.Messages);
    }

    [Fact]
    public async Task ModVersionSelectionShowsDialogWhenInstanceInstallFileAlreadyExists()
    {
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult())
        {
            InstallExists = true
        };
        var floatingMessageService = new FakeFloatingMessageService();
        var downloadTasksPage = new DownloadTasksPageViewModel();
        var instance = new GameInstance
        {
            Id = "fabric-instance",
            MinecraftVersion = "1.18.2",
            Loader = LoaderKind.Fabric,
            InstanceDirectory = "C:\\Instances\\Fabric Test"
        };
        var viewModel = new ResourcesPageViewModel(
            catalogService,
            floatingMessageService: floatingMessageService,
            downloadTasksPage: downloadTasksPage);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "project",
            Title = "Project"
        });
        var version = new ResourceProjectVersion
        {
            VersionId = "version-1",
            Name = "Version 1",
            VersionNumber = "1.0.0",
            FileName = "version-1.jar",
            PrimaryDownloadUrl = "https://example.test/version-1.jar"
        };

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectInstallTargetCommand.Execute(ResourcesModInstallTargetItemViewModel.FromInstance(instance));
        await viewModel.ModPage.InstallAvailableVersionCommand.ExecuteAsync(new ResourcesModVersionItemViewModel(version, project));

        Assert.True(viewModel.ModPage.IsProjectVersionFileExistsDialogOpen);
        Assert.Contains("version-1.jar", viewModel.ModPage.ProjectVersionFileExistsDialogMessage);
        Assert.Null(catalogService.LastInstalledVersion);
        Assert.Empty(downloadTasksPage.Tasks);
        Assert.Empty(floatingMessageService.Messages);
    }

    [Fact]
    public async Task ModVersionSelectionReportsFailureWhenLocalDownloadFails()
    {
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult())
        {
            ThrowOnDownload = true
        };
        var statusService = new FakeStatusService();
        var floatingMessageService = new FakeFloatingMessageService();
        var downloadTasksPage = new DownloadTasksPageViewModel();
        var viewModel = new ResourcesPageViewModel(
            catalogService,
            statusService: statusService,
            filePickerService: new FakeFilePickerService { FolderPath = "C:\\Downloads" },
            floatingMessageService: floatingMessageService,
            downloadTasksPage: downloadTasksPage);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "project",
            Title = "Project"
        });
        var version = new ResourceProjectVersion
        {
            VersionId = "version-1",
            Name = "Version 1",
            VersionNumber = "1.0.0",
            FileName = "version-1.jar",
            PrimaryDownloadUrl = "https://example.test/version-1.jar"
        };

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectInstallTargetCommand.Execute(ResourcesModInstallTargetItemViewModel.CreateLocalDownload());
        await viewModel.ModPage.InstallAvailableVersionCommand.ExecuteAsync(new ResourcesModVersionItemViewModel(version, project));

        Assert.Equal(ResourcesModPageStep.ProjectVersions, viewModel.ModPage.CurrentStep);
        var task = Assert.Single(downloadTasksPage.Tasks);
        Assert.Equal(DownloadTaskState.Failed, task.State);
        Assert.Equal(Strings.Status_ModDownloadFailed, task.StatusMessage);
        Assert.Contains(Strings.Status_ModDownloadFailed, statusService.Messages);
        Assert.Contains(Strings.Status_ModDownloadFailed, floatingMessageService.Messages);
    }

    private static GameInstance CreateInstance(string name, string minecraftVersion, LoaderKind loader)
    {
        return new GameInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            MinecraftVersion = minecraftVersion,
            VersionName = minecraftVersion,
            Loader = loader,
            LoaderVersion = loader is LoaderKind.Vanilla ? null : "latest",
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            InstanceDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"))
        };
    }

    private static ResourcesModProjectItemViewModel CreateModProjectItem(
        string slug,
        string title,
        ResourceProjectSource source = ResourceProjectSource.Modrinth)
    {
        return new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Kind = ResourceProjectKind.Mod,
            Source = source,
            ProjectId = slug,
            Slug = slug,
            Title = title
        });
    }

    private static ResourceProject CreateDependencyProject(
        string slug,
        string title,
        ResourceProjectSource source = ResourceProjectSource.Modrinth)
    {
        return new ResourceProject
        {
            Kind = ResourceProjectKind.Mod,
            Source = source,
            ProjectId = slug,
            Slug = slug,
            Title = title
        };
    }

    private static ResourceProjectDependency CreateDependency(
        string slug,
        string title,
        string versionId = "",
        ResourceProjectSource source = ResourceProjectSource.Modrinth)
    {
        return new ResourceProjectDependency
        {
            Project = CreateDependencyProject(slug, title, source),
            VersionId = versionId
        };
    }

    private static IReadOnlyList<string> FormatAvailableVersionListItems(IEnumerable<object> items)
    {
        return items
            .Where(item => item is not ResourcesListFooterStatusItem)
            .Select(item => item switch
            {
                ResourcesModVersionListHeaderItem header => $"H:{header.Title}",
                ResourcesModVersionItemViewModel version => $"V:{version.Title}",
                _ => item.GetType().Name
            })
            .ToList();
    }

    private static IReadOnlyList<string> FormatProjectListItems(IEnumerable<object> items)
    {
        return items
            .Where(item => item is not ResourcesListFooterStatusItem)
            .Select(item => item switch
            {
                ResourcesModProjectItemViewModel project => $"P:{project.Title}",
                _ => item.GetType().Name
            })
            .ToList();
    }

    private static IReadOnlyList<string> FormatProjectListItemsWithFooter(IEnumerable<object> items)
    {
        return items
            .Select(item => item switch
            {
                ResourcesModProjectItemViewModel project => $"P:{project.Title}",
                ResourcesListFooterStatusItem footer => $"F:{footer.Message}",
                _ => item.GetType().Name
            })
            .ToList();
    }

    private static IReadOnlyList<string> FormatAvailableVersionListItemsWithFooter(IEnumerable<object> items)
    {
        return items
            .Select(item => item switch
            {
                ResourcesModVersionListHeaderItem header => $"H:{header.Title}",
                ResourcesModVersionItemViewModel version => $"V:{version.Title}",
                ResourcesListFooterStatusItem footer => $"F:{footer.Message}",
                _ => item.GetType().Name
            })
            .ToList();
    }

    private sealed class FakeResourceCatalogService : IResourceCatalogService
    {
        private readonly Task<ResourceCatalogSearchResult> resultTask;

        public FakeResourceCatalogService(ResourceCatalogSearchResult result)
            : this(Task.FromResult(result))
        {
        }

        public FakeResourceCatalogService(Task<ResourceCatalogSearchResult> resultTask)
        {
            this.resultTask = resultTask;
        }

        public int CallCount { get; private set; }

        public ResourceCatalogSearchRequest? LastRequest { get; private set; }

        public List<ResourceCatalogSearchRequest> Requests { get; } = [];

        public ResourceProjectVersionsRequest? LastVersionsRequest { get; private set; }

        public List<ResourceProjectVersionsRequest> VersionRequests { get; } = [];

        public ResourceProjectDependenciesRequest? LastDependenciesRequest { get; private set; }

        public ResourceProjectVersionsResult VersionsResult { get; init; } = new();

        public Dictionary<int, ResourceProjectVersionsResult> VersionsResultsByOffset { get; } = [];

        public Dictionary<string, ResourceProjectVersionsResult> VersionsResultsByRequestKey { get; } = [];

        public Dictionary<string, ResourceProjectVersionsResult> VersionsResultsByProjectId { get; } = [];

        public Dictionary<string, TaskCompletionSource<ResourceProjectVersionsResult>> PendingVersionsResultsByRequestKey { get; } = [];

        public ResourceProjectDependenciesResult DependenciesResult { get; init; } = new();

        public Dictionary<string, ResourceProjectDependenciesResult> DependenciesResultsByProjectId { get; } = [];

        public Dictionary<string, TaskCompletionSource<ResourceProjectDependenciesResult>> PendingDependenciesResultsByProjectId { get; } = [];

        public HashSet<int> VersionOffsetsToThrow { get; } = [];

        public ResourceProjectVersion? LastInstalledVersion { get; private set; }

        public List<ResourceProjectVersion> InstalledVersions { get; } = [];

        public GameInstance? LastInstallInstance { get; private set; }

        public CancellationToken LastInstallCancellationToken { get; private set; }

        public ResourceProjectVersion? LastDownloadedVersion { get; private set; }

        public string? LastDownloadDirectory { get; private set; }

        public CancellationToken LastDownloadCancellationToken { get; private set; }

        public bool ThrowOnDownload { get; init; }

        public bool ThrowOnInstall { get; init; }

        public bool DownloadExists { get; init; }

        public bool InstallExists { get; init; }

        public TaskCompletionSource<string>? PendingInstall { get; init; }

        public TaskCompletionSource<string>? PendingDownload { get; init; }

        public Task<ResourceCatalogSearchResult> SearchModsAsync(
            ResourceCatalogSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            Requests.Add(request);
            return resultTask;
        }

        public Task<ResourceProjectVersionsResult> GetProjectVersionsAsync(
            ResourceProjectVersionsRequest request,
            CancellationToken cancellationToken = default)
        {
            LastVersionsRequest = request;
            VersionRequests.Add(request);
            if (VersionOffsetsToThrow.Contains(request.Offset))
                return Task.FromException<ResourceProjectVersionsResult>(new InvalidOperationException("versions failed"));

            if (VersionsResultsByProjectId.TryGetValue(request.ProjectId, out var projectResult))
                return Task.FromResult(projectResult);

            if (VersionsResultsByRequestKey.TryGetValue(CreateVersionsRequestKey(request), out var keyedResult))
                return Task.FromResult(keyedResult);

            if (PendingVersionsResultsByRequestKey.TryGetValue(CreateVersionsRequestKey(request), out var pendingResult))
                return pendingResult.Task;

            if (VersionsResultsByOffset.TryGetValue(request.Offset, out var result))
                return Task.FromResult(result);

            return Task.FromResult(VersionsResult);
        }

        public Task<ResourceProjectDependenciesResult> GetProjectDependenciesAsync(
            ResourceProjectDependenciesRequest request,
            CancellationToken cancellationToken = default)
        {
            LastDependenciesRequest = request;
            if (PendingDependenciesResultsByProjectId.TryGetValue(request.ProjectId, out var pendingResult))
                return pendingResult.Task.WaitAsync(cancellationToken);

            if (DependenciesResultsByProjectId.TryGetValue(request.ProjectId, out var result))
                return Task.FromResult(result);

            return Task.FromResult(DependenciesResult);
        }

        public static string CreateVersionsRequestKey(
            string minecraftVersion,
            LoaderKind loader,
            int offset)
        {
            return $"{minecraftVersion}|{loader}|{offset}";
        }

        private static string CreateVersionsRequestKey(ResourceProjectVersionsRequest request)
        {
            return CreateVersionsRequestKey(request.MinecraftVersion, request.Loader, request.Offset);
        }

        public Task<string> InstallProjectVersionAsync(
            ResourceProjectVersion version,
            GameInstance instance,
            CancellationToken cancellationToken = default)
        {
            LastInstallCancellationToken = cancellationToken;
            if (PendingInstall is not null)
                return PendingInstall.Task.WaitAsync(cancellationToken);

            if (ThrowOnInstall)
                return Task.FromException<string>(new InvalidOperationException("install failed"));

            LastInstalledVersion = version;
            InstalledVersions.Add(version);
            LastInstallInstance = instance;
            return Task.FromResult("installed.jar");
        }

        public Task<string> DownloadProjectVersionAsync(
            ResourceProjectVersion version,
            string targetDirectory,
            CancellationToken cancellationToken = default)
        {
            LastDownloadCancellationToken = cancellationToken;
            if (PendingDownload is not null)
                return PendingDownload.Task.WaitAsync(cancellationToken);

            if (ThrowOnDownload)
                return Task.FromException<string>(new InvalidOperationException("download failed"));

            LastDownloadedVersion = version;
            LastDownloadDirectory = targetDirectory;
            return Task.FromResult(Path.Combine(targetDirectory, version.FileName));
        }

        public Task<bool> ProjectVersionDownloadExistsAsync(
            ResourceProjectVersion version,
            string targetDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(DownloadExists);
        }

        public Task<bool> ProjectVersionInstallExistsAsync(
            ResourceProjectVersion version,
            GameInstance instance,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(InstallExists);
        }
    }

    private sealed class FakeModService : IModService
    {
        public Dictionary<string, List<LocalMod>> ModsByInstanceId { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int GetModsCallCount { get; private set; }

        public Task<IReadOnlyList<LocalMod>> GetModsAsync(
            GameInstance instance,
            CancellationToken cancellationToken = default)
        {
            GetModsCallCount++;
            var mods = ModsByInstanceId.TryGetValue(instance.Id, out var result)
                ? result.Select(CloneLocalMod).ToArray()
                : [];
            return Task.FromResult<IReadOnlyList<LocalMod>>(mods);
        }

        public Task<LocalMod> ImportAsync(
            GameInstance instance,
            string sourceJarPath,
            bool overwriteExisting = false,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SetEnabledAsync(
            LocalMod mod,
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAsync(LocalMod mod, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        private static LocalMod CloneLocalMod(LocalMod mod)
        {
            return new LocalMod
            {
                Name = mod.Name,
                Loader = mod.Loader,
                ModId = mod.ModId,
                Version = mod.Version,
                FileName = mod.FileName,
                FullPath = mod.FullPath,
                IconSource = mod.IconSource,
                IsEnabled = mod.IsEnabled,
                SizeBytes = mod.SizeBytes,
                Source = mod.Source
            };
        }
    }

    private sealed class FakeLocalModpackImportService : ILocalModpackImportService
    {
        public ModpackImportResult ResultToReturn { get; init; } =
            ModpackImportResult.Failure(ModpackImportFailureReason.UnsupportedArchive);

        public int ImportCallCount { get; private set; }

        public string? LastArchivePath { get; private set; }

        public CancellationToken LastImportCancellationToken { get; private set; }

        public IProgress<LauncherProgress>? LastProgress { get; private set; }

        public Task<ModpackRecognitionResult> RecognizeArchiveAsync(
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ModpackRecognitionResult.Success());
        }

        public Task<ModpackImportResult> ImportFromArchiveAsync(
            string archivePath,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            ImportCallCount++;
            LastArchivePath = archivePath;
            LastProgress = progress;
            LastImportCancellationToken = cancellationToken;
            progress?.Report(new LauncherProgress(
                ImportProgressStages.CopyingOverrides,
                "importing",
                50));
            return Task.FromResult(ResultToReturn);
        }
    }

    private sealed class ControlledResourceCatalogService : IResourceCatalogService
    {
        private readonly object gate = new();
        private readonly List<TaskCompletionSource<ResourceCatalogSearchResult>> calls = [];
        private readonly List<ResourceCatalogSearchRequest> requests = [];

        public int CallCount
        {
            get
            {
                lock (gate)
                    return calls.Count;
            }
        }

        public Task<ResourceCatalogSearchResult> SearchModsAsync(
            ResourceCatalogSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                var result = new TaskCompletionSource<ResourceCatalogSearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                calls.Add(result);
                requests.Add(request);
                return result.Task;
            }
        }

        public Task<ResourceProjectVersionsResult> GetProjectVersionsAsync(
            ResourceProjectVersionsRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ResourceProjectVersionsResult());
        }

        public Task<string> InstallProjectVersionAsync(
            ResourceProjectVersion version,
            GameInstance instance,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult("installed.jar");
        }

        public Task<string> DownloadProjectVersionAsync(
            ResourceProjectVersion version,
            string targetDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult("downloaded.jar");
        }

        public Task<bool> ProjectVersionDownloadExistsAsync(
            ResourceProjectVersion version,
            string targetDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<bool> ProjectVersionInstallExistsAsync(
            ResourceProjectVersion version,
            GameInstance instance,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public async Task<ResourceCatalogSearchRequest> WaitForRequestAsync(int index)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
            while (DateTimeOffset.UtcNow < deadline)
            {
                lock (gate)
                {
                    if (requests.Count > index)
                        return requests[index];
                }

                await Task.Delay(10);
            }

            throw new TimeoutException($"Resource catalog request {index} was not observed.");
        }

        public async Task<TaskCompletionSource<ResourceCatalogSearchResult>> WaitForCallAsync(int index)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
            while (DateTimeOffset.UtcNow < deadline)
            {
                lock (gate)
                {
                    if (calls.Count > index)
                        return calls[index];
                }

                await Task.Delay(10);
            }

            throw new TimeoutException($"Resource catalog call {index} was not observed.");
        }
    }

    private sealed class QueueResourceCatalogService : IResourceCatalogService
    {
        private readonly Queue<ResourceCatalogSearchResult> results;

        public QueueResourceCatalogService(params ResourceCatalogSearchResult[] results)
        {
            this.results = new Queue<ResourceCatalogSearchResult>(results);
        }

        public List<ResourceCatalogSearchRequest> Requests { get; } = [];

        public Task<ResourceCatalogSearchResult> SearchModsAsync(
            ResourceCatalogSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(results.Count == 0
                ? new ResourceCatalogSearchResult()
                : results.Dequeue());
        }

        public Task<ResourceProjectVersionsResult> GetProjectVersionsAsync(
            ResourceProjectVersionsRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ResourceProjectVersionsResult());
        }

        public Task<string> InstallProjectVersionAsync(
            ResourceProjectVersion version,
            GameInstance instance,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult("installed.jar");
        }

        public Task<string> DownloadProjectVersionAsync(
            ResourceProjectVersion version,
            string targetDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult("downloaded.jar");
        }

        public Task<bool> ProjectVersionDownloadExistsAsync(
            ResourceProjectVersion version,
            string targetDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<bool> ProjectVersionInstallExistsAsync(
            ResourceProjectVersion version,
            GameInstance instance,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }
    }

    private sealed class FakeStatusService : IStatusService
    {
        public event Action<string>? MessageReported;

        public List<string> Messages { get; } = [];

        public void Report(string message)
        {
            Messages.Add(message);
            MessageReported?.Invoke(message);
        }
    }

    private sealed class FakeFilePickerService : IFilePickerService
    {
        public string? FolderPath { get; init; }

        public string? LastFolderPickerTitle { get; private set; }

        public string? PickMinecraftSkin() => null;

        public string? PickJavaExecutable() => null;

        public string? PickLocalImportFile() => null;

        public string? PickModFile() => null;

        public string? PickSaveArchive() => null;

        public string? PickResourcePackArchive() => null;

        public string? PickShaderPackArchive() => null;

        public string? PickModpackExportArchive(string defaultFileName, ModpackExportKind kind) => null;

        public string? PickFolder(string title, string? initialDirectory = null)
        {
            LastFolderPickerTitle = title;
            return FolderPath;
        }
    }

    private sealed class FakeFloatingMessageService : IFloatingMessageService
    {
        public event Action<string>? MessageRequested;

        public List<string> Messages { get; } = [];

        public void Show(string message)
        {
            Messages.Add(message);
            MessageRequested?.Invoke(message);
        }
    }

    private sealed class ThrowingGameVersionService : IGameVersionService
    {
        public Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            CancellationToken cancellationToken = default,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            return Task.FromException<IReadOnlyList<MinecraftVersionInfo>>(
                new InvalidOperationException("version order failed"));
        }
    }

    private sealed class FakeGameInstanceService(IReadOnlyList<GameInstance> instances) : IGameInstanceService
    {
        public int GetInstancesCallCount { get; private set; }

        public Task<IReadOnlyList<GameInstance>> GetStoredInstancesAsync(
            LauncherSettings settings,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(instances);
        }

        public Task<IReadOnlyList<GameInstance>> GetInstancesAsync(CancellationToken cancellationToken = default)
        {
            GetInstancesCallCount++;
            return Task.FromResult(instances);
        }

        public Task<GameInstance?> GetDefaultInstanceAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(instances.FirstOrDefault());
        }

        public Task<GameInstance> CreateInstanceAsync(
            string minecraftVersion,
            LoaderKind loader,
            string? loaderVersion,
            string? name,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            int downloadSpeedLimitMbPerSecond = 0,
            bool installFabricApi = true,
            string? fabricApiVersionId = null,
            string? quiltStandardLibraryVersionId = null)
        {
            throw new NotSupportedException();
        }

        public Task SaveInstanceAsync(GameInstance instance, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<GameInstance> RenameInstanceAsync(
            string instanceId,
            string? newName,
            string? newIconSource,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> SetDefaultInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ThrowingGameInstanceService : IGameInstanceService
    {
        public Task<IReadOnlyList<GameInstance>> GetStoredInstancesAsync(
            LauncherSettings settings,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<IReadOnlyList<GameInstance>>(new InvalidOperationException("instances failed"));
        }

        public Task<IReadOnlyList<GameInstance>> GetInstancesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromException<IReadOnlyList<GameInstance>>(new InvalidOperationException("instances failed"));
        }

        public Task<GameInstance?> GetDefaultInstanceAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<GameInstance> CreateInstanceAsync(
            string minecraftVersion,
            LoaderKind loader,
            string? loaderVersion,
            string? name,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            int downloadSpeedLimitMbPerSecond = 0,
            bool installFabricApi = true,
            string? fabricApiVersionId = null,
            string? quiltStandardLibraryVersionId = null)
        {
            throw new NotSupportedException();
        }

        public Task SaveInstanceAsync(GameInstance instance, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<GameInstance> RenameInstanceAsync(
            string instanceId,
            string? newName,
            string? newIconSource,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> SetDefaultInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingUiDispatcher(bool hasAccess) : IUiDispatcher
    {
        private bool hasAccess = hasAccess;

        public bool HasAccess => hasAccess;

        public int InvokeCount { get; private set; }

        public int PostCount { get; private set; }

        public void Post(Action action)
        {
            PostCount++;
            ExecuteWithAccess(action);
        }

        public void Invoke(Action action)
        {
            InvokeCount++;
            ExecuteWithAccess(action);
        }

        private void ExecuteWithAccess(Action action)
        {
            var previousHasAccess = hasAccess;
            hasAccess = true;
            try
            {
                action();
            }
            finally
            {
                hasAccess = previousHasAccess;
            }
        }
    }

    private sealed class QueueingUiDispatcher : IUiDispatcher
    {
        private readonly object gate = new();
        private readonly Queue<Action> actions = new();

        public bool HasAccess => true;

        public int PendingCount
        {
            get
            {
                lock (gate)
                    return actions.Count;
            }
        }

        public void Post(Action action)
        {
            lock (gate)
                actions.Enqueue(action);
        }

        public void Invoke(Action action)
        {
            action();
        }

        public void RunNext()
        {
            Action action;
            lock (gate)
                action = actions.Dequeue();
            action.Invoke();
        }
    }

    private static ResourceCatalogSearchResult CreateSingleProjectResult(string slug, string title, long downloads)
    {
        return new ResourceCatalogSearchResult
        {
            Projects =
            [
                new ResourceProject
                {
                    Source = ResourceProjectSource.Modrinth,
                    ProjectId = slug,
                    Slug = slug,
                    Title = title,
                    Description = title,
                    Downloads = downloads,
                    ProjectUrl = $"https://modrinth.com/mod/{slug}"
                }
            ]
        };
    }

    private static ResourceCatalogSearchResult CreateProjectResult(
        int count,
        string prefix = "project",
        bool hasMore = false)
    {
        return new ResourceCatalogSearchResult
        {
            Projects = Enumerable.Range(0, count)
                .Select(index => new ResourceProject
                {
                    Source = ResourceProjectSource.Modrinth,
                    ProjectId = $"{prefix}-{index}",
                    Slug = $"{prefix}-{index}",
                    Title = $"{char.ToUpperInvariant(prefix[0])}{prefix[1..]} {index}",
                    Description = $"{prefix} {index}",
                    Downloads = count - index,
                    ProjectUrl = $"https://modrinth.com/mod/{prefix}-{index}"
                })
                .ToList(),
            HasMore = hasMore
        };
    }
}
