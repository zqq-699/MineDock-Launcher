using Launcher.Application.Services;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Domain.Models;
using Launcher.Tests.Fakes;

namespace Launcher.Tests.Resources;

public sealed class ResourcesPageViewModelTests
{
    [Fact]
    public void ResourcesPageShowsExpectedSectionsAndSelectsModByDefault()
    {
        var viewModel = new ResourcesPageViewModel();

        Assert.Equal(
            [
                Strings.Resources_SectionMods,
                Strings.Resources_SectionResourcePacks,
                Strings.Resources_SectionShaderPacks,
                Strings.Resources_SectionWorlds,
                Strings.Resources_SectionModpacks
            ],
            viewModel.Sections.Select(section => section.Title));
        Assert.Same(viewModel.Sections[0], viewModel.SelectedSection);
        Assert.True(viewModel.Sections[0].IsSelected);
        Assert.All(viewModel.Sections.Skip(1), section => Assert.False(section.IsSelected));
        Assert.True(viewModel.IsModsSection);
        Assert.Same(viewModel.ModPage, viewModel.CurrentSectionViewModel);
        Assert.Equal(string.Empty, viewModel.ModPage.SearchQuery);
    }

    [Fact]
    public void SelectSectionCommandUpdatesSelectionState()
    {
        var viewModel = new ResourcesPageViewModel();
        var targetSection = viewModel.Sections.Single(section => section.Id == "worlds");

        viewModel.SelectSectionCommand.Execute(targetSection);

        Assert.Same(targetSection, viewModel.SelectedSection);
        Assert.False(viewModel.Sections[0].IsSelected);
        Assert.True(targetSection.IsSelected);
        Assert.Equal(targetSection.Title, viewModel.PageTitle);
        Assert.False(viewModel.IsModsSection);
        Assert.Same(viewModel.WorldsPage, viewModel.CurrentSectionViewModel);
    }

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
    public void ModFilterSelectionCanBeChanged()
    {
        var viewModel = new ResourcesPageViewModel();
        var versionOption = new ResourcesFilterOptionItem { Id = "1.21", Title = "1.21" };
        var loaderOption = new ResourcesFilterOptionItem { Id = "fabric", Title = "Fabric" };
        var sourceOption = new ResourcesFilterOptionItem { Id = "modrinth", Title = "Modrinth" };
        var typeOption = new ResourcesFilterOptionItem { Id = "library", Title = "Library" };

        viewModel.ModPage.VersionOptions.Add(versionOption);
        viewModel.ModPage.LoaderOptions.Add(loaderOption);
        viewModel.ModPage.SourceOptions.Add(sourceOption);
        viewModel.ModPage.TypeOptions.Add(typeOption);

        viewModel.ModPage.SelectedVersionOption = versionOption;
        viewModel.ModPage.SelectedLoaderOption = loaderOption;
        viewModel.ModPage.SelectedSourceOption = sourceOption;
        viewModel.ModPage.SelectedTypeOption = typeOption;

        Assert.Same(versionOption, viewModel.ModPage.SelectedVersionOption);
        Assert.Same(loaderOption, viewModel.ModPage.SelectedLoaderOption);
        Assert.Same(sourceOption, viewModel.ModPage.SelectedSourceOption);
        Assert.Same(typeOption, viewModel.ModPage.SelectedTypeOption);
    }

    [Fact]
    public async Task ModTypeFilterAddsCategoryToSearchRequest()
    {
        var service = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        var viewModel = new ResourcesPageViewModel(service);

        viewModel.ModPage.SelectedTypeOption = viewModel.ModPage.TypeOptions.Single(option => option.Id == "magic");
        await viewModel.ModPage.RefreshProjectsCommand.ExecuteAsync(null);

        Assert.NotNull(service.LastRequest);
        Assert.Equal(ResourceProjectCategory.Magic, service.LastRequest.Category);
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
    public async Task ResourcePackTypeFilterAddsResourcePackCategoryToSearchRequest()
    {
        var service = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        var viewModel = new ResourcesPageViewModel(service);
        viewModel.SelectSectionCommand.Execute(viewModel.Sections.Single(section => section.Id == "resource_packs"));

        viewModel.ResourcePacksPage.SelectedTypeOption = viewModel.ResourcePacksPage.TypeOptions.Single(option => option.Id == "vanilla-like");
        await viewModel.ResourcePacksPage.RefreshProjectsCommand.ExecuteAsync(null);

        Assert.NotNull(service.LastRequest);
        Assert.Equal(ResourceProjectKind.ResourcePack, service.LastRequest.Kind);
        Assert.Equal(ResourceProjectCategory.VanillaLike, service.LastRequest.Category);
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
    public async Task ShaderPackTypeFilterAddsShaderPackCategoryToSearchRequest()
    {
        var service = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        var viewModel = new ResourcesPageViewModel(service);
        viewModel.SelectSectionCommand.Execute(viewModel.Sections.Single(section => section.Id == "shader_packs"));

        viewModel.ShaderPacksPage.SelectedTypeOption = viewModel.ShaderPacksPage.TypeOptions.Single(option => option.Id == "semi-realistic");
        await viewModel.ShaderPacksPage.RefreshProjectsCommand.ExecuteAsync(null);

        Assert.NotNull(service.LastRequest);
        Assert.Equal(ResourceProjectKind.ShaderPack, service.LastRequest.Kind);
        Assert.Equal(ResourceProjectCategory.SemiRealistic, service.LastRequest.Category);
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
    public async Task WorldTypeFilterAddsWorldCategoryToSearchRequest()
    {
        var service = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        var viewModel = new ResourcesPageViewModel(service);
        viewModel.SelectSectionCommand.Execute(viewModel.Sections.Single(section => section.Id == "modpacks"));

        viewModel.WorldsPage.SelectedTypeOption = viewModel.WorldsPage.TypeOptions.Single(option => option.Id == "parkour");
        await viewModel.WorldsPage.RefreshProjectsCommand.ExecuteAsync(null);

        Assert.NotNull(service.LastRequest);
        Assert.Equal(ResourceProjectKind.World, service.LastRequest.Kind);
        Assert.Equal(ResourceProjectCategory.Parkour, service.LastRequest.Category);
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
    public async Task ModpackTypeFilterAddsModpackCategoryToSearchRequest()
    {
        var service = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        var viewModel = new ResourcesPageViewModel(service);
        viewModel.SelectSectionCommand.Execute(viewModel.Sections.Single(section => section.Id == "modpacks"));

        viewModel.ModpacksPage.SelectedTypeOption = viewModel.ModpacksPage.TypeOptions.Single(option => option.Id == "quests");
        await viewModel.ModpacksPage.RefreshProjectsCommand.ExecuteAsync(null);

        Assert.NotNull(service.LastRequest);
        Assert.Equal(ResourceProjectKind.Modpack, service.LastRequest.Kind);
        Assert.Equal(ResourceProjectCategory.Quests, service.LastRequest.Category);
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

    [Fact]
    public async Task ModTypeFilterKeepsCategoryWhenLoadingMore()
    {
        var service = new QueueResourceCatalogService(
            CreateProjectResult(1, "first", hasMore: true),
            CreateProjectResult(1, "second", hasMore: false));
        var viewModel = new ResourcesPageViewModel(service);

        viewModel.ModPage.SelectedTypeOption = viewModel.ModPage.TypeOptions.Single(option => option.Id == "technology");
        await viewModel.ModPage.RefreshProjectsCommand.ExecuteAsync(null);
        await viewModel.ModPage.LoadMoreProjectsCommand.ExecuteAsync(null);

        Assert.Equal(ResourceProjectCategory.Technology, service.Requests[0].Category);
        Assert.Equal(ResourceProjectCategory.Technology, service.Requests[1].Category);
        Assert.Equal(20, service.Requests[1].Offset);
    }

    [Fact]
    public async Task ModFilterDialogConfirmAppliesPendingTypeFilter()
    {
        var service = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        var viewModel = new ResourcesPageViewModel(service);
        var typeOption = viewModel.ModPage.TypeOptions.Single(option => option.Id == "storage");

        viewModel.ModPage.OpenFilterDialogCommand.Execute(null);
        viewModel.ModPage.PendingTypeOption = typeOption;
        viewModel.ModPage.ConfirmFilterDialogCommand.Execute(null);
        await viewModel.ModPage.RefreshProjectsCommand.ExecuteAsync(null);

        Assert.Same(typeOption, viewModel.ModPage.SelectedTypeOption);
        Assert.NotNull(service.LastRequest);
        Assert.Equal(ResourceProjectCategory.Storage, service.LastRequest.Category);
    }

    [Fact]
    public async Task ModVersionFilterOptionsUseMajorReleaseVersions()
    {
        var service = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        var versionService = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("26.2", "release", false),
            new MinecraftVersionInfo("26.1", "release", false),
            new MinecraftVersionInfo("26", "release", false),
            new MinecraftVersionInfo("1.21.8", "release", false),
            new MinecraftVersionInfo("1.21.4", "release", false),
            new MinecraftVersionInfo("1.20.6", "release", false),
            new MinecraftVersionInfo("1.20.1", "release", false),
            new MinecraftVersionInfo("1.20", "release", false),
            new MinecraftVersionInfo("24w45a", "snapshot", false),
            new MinecraftVersionInfo("1.19.4", "release", false)
        ]);
        var viewModel = new ResourcesPageViewModel(service, gameVersionService: versionService);

        await viewModel.ModPage.RefreshProjectsCommand.ExecuteAsync(null);

        Assert.Equal(
            [Strings.Resources_ModFilterAllVersions, "26", "1.21", "1.20", "1.19"],
            viewModel.ModPage.VersionOptions.Select(option => option.Title));
        var modernOption = viewModel.ModPage.VersionOptions.Single(option => option.Id == "26");
        Assert.Equal(["26.2", "26.1", "26"], modernOption.MinecraftVersions);
        var option = viewModel.ModPage.VersionOptions.Single(option => option.Id == "1.20");
        Assert.Equal(["1.20.6", "1.20.1", "1.20"], option.MinecraftVersions);
        Assert.Equal(1, versionService.CallCount);
    }

    [Fact]
    public async Task ModVersionFilterExpandsMajorVersionInSearchRequest()
    {
        var service = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        var versionService = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.6", "release", false),
            new MinecraftVersionInfo("1.20.1", "release", false),
            new MinecraftVersionInfo("1.20", "release", false)
        ]);
        var viewModel = new ResourcesPageViewModel(service, gameVersionService: versionService);

        await viewModel.ModPage.RefreshProjectsCommand.ExecuteAsync(null);
        viewModel.ModPage.SelectedVersionOption = viewModel.ModPage.VersionOptions.Single(option => option.Id == "1.20");
        await viewModel.ModPage.RefreshProjectsCommand.ExecuteAsync(null);

        Assert.NotNull(service.LastRequest);
        Assert.Equal(string.Empty, service.LastRequest.MinecraftVersion);
        Assert.Equal(["1.20.6", "1.20.1", "1.20"], service.LastRequest.MinecraftVersions);
        Assert.Equal(0, service.LastRequest.Offset);
        Assert.Equal(20, service.LastRequest.PageSize);
    }

    [Fact]
    public async Task ModVersionFilterKeepsExpandedVersionsWhenLoadingMore()
    {
        var service = new QueueResourceCatalogService(
            CreateProjectResult(1, "initial", hasMore: false),
            CreateProjectResult(1, "first", hasMore: true),
            CreateProjectResult(1, "second", hasMore: false));
        var versionService = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("26", "release", false)
        ]);
        var viewModel = new ResourcesPageViewModel(service, gameVersionService: versionService);

        await viewModel.ModPage.RefreshProjectsCommand.ExecuteAsync(null);
        viewModel.ModPage.SelectedVersionOption = viewModel.ModPage.VersionOptions.Single(option => option.Id == "26");
        await viewModel.ModPage.RefreshProjectsCommand.ExecuteAsync(null);
        await viewModel.ModPage.LoadMoreProjectsCommand.ExecuteAsync(null);

        Assert.Equal(["26"], service.Requests[^2].MinecraftVersions);
        Assert.Equal(["26"], service.Requests[^1].MinecraftVersions);
        Assert.Equal(20, service.Requests[^1].Offset);
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
        Assert.NotNull(service.LastRequest);
        Assert.Equal(["1.18.2"], service.LastRequest.MinecraftVersions);
        Assert.Equal("1.18.2", service.LastRequest.MinecraftVersion);
        Assert.Equal(loader, service.LastRequest.Loader);
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
    public void SelectSectionCommandMapsAllSectionsToChildViewModels()
    {
        var viewModel = new ResourcesPageViewModel();

        var expectedMappings = new Dictionary<string, ResourcesSectionViewModelBase>
        {
            ["mods"] = viewModel.ModPage,
            ["resource_packs"] = viewModel.ResourcePacksPage,
            ["shader_packs"] = viewModel.ShaderPacksPage,
            ["worlds"] = viewModel.WorldsPage,
            ["modpacks"] = viewModel.ModpacksPage
        };

        foreach (var section in viewModel.Sections)
        {
            viewModel.SelectSectionCommand.Execute(section);

            Assert.Same(expectedMappings[section.Id], viewModel.CurrentSectionViewModel);
            Assert.Equal(section.Id == "mods", viewModel.IsModsSection);
        }
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
    public async Task ModPageRefreshProjectsUsesMinecraftReleaseOrderForVersionSummary()
    {
        var service = new FakeResourceCatalogService(new ResourceCatalogSearchResult
        {
            Projects =
            [
                new ResourceProject
                {
                    Source = ResourceProjectSource.Modrinth,
                    ProjectId = "future-mod",
                    Slug = "future-mod",
                    Title = "Future Mod",
                    SupportedMinecraftVersions = ["26", "1.21.4", "1.20.1", "1.12.2"],
                    SupportedLoaders = ["fabric"],
                    Downloads = 2000
                }
            ]
        });
        var versionService = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("26", "release", false),
            new MinecraftVersionInfo("1.21.8", "release", false),
            new MinecraftVersionInfo("1.20.6", "release", false),
            new MinecraftVersionInfo("24w45a", "snapshot", false),
            new MinecraftVersionInfo("1.19.4", "release", false),
            new MinecraftVersionInfo("1.18.2", "release", false),
            new MinecraftVersionInfo("1.17.1", "release", false),
            new MinecraftVersionInfo("1.16.5", "release", false),
            new MinecraftVersionInfo("1.15.2", "release", false),
            new MinecraftVersionInfo("1.14.4", "release", false),
            new MinecraftVersionInfo("1.13.2", "release", false),
            new MinecraftVersionInfo("1.12.2", "release", false)
        ]);
        var viewModel = new ResourcesPageViewModel(service, gameVersionService: versionService);

        await viewModel.ModPage.RefreshProjectsCommand.ExecuteAsync(null);

        Assert.Equal($"1.20+, 1.12  fabric  {Strings.Resources_ModSourceModrinth}", viewModel.ModPage.VisibleProjects[0].Subtitle);
        Assert.Equal(1, versionService.CallCount);
    }

    [Fact]
    public async Task ModPageRefreshProjectsFallsBackWhenMinecraftReleaseOrderFails()
    {
        var service = new FakeResourceCatalogService(new ResourceCatalogSearchResult
        {
            Projects =
            [
                new ResourceProject
                {
                    Source = ResourceProjectSource.Modrinth,
                    ProjectId = "fallback-mod",
                    Slug = "fallback-mod",
                    Title = "Fallback Mod",
                    SupportedMinecraftVersions = ["26", "1.21.4", "1.20.1", "1.12.2"],
                    SupportedLoaders = ["fabric"],
                    Downloads = 2000
                }
            ]
        });
        var viewModel = new ResourcesPageViewModel(service, gameVersionService: new ThrowingGameVersionService());

        await viewModel.ModPage.RefreshProjectsCommand.ExecuteAsync(null);

        Assert.True(viewModel.ModPage.HasVisibleProjects);
        Assert.Equal($"26, 1.21, 1.20, 1.12  fabric  {Strings.Resources_ModSourceModrinth}", viewModel.ModPage.VisibleProjects[0].Subtitle);
    }

    [Fact]
    public void ModProjectItemSubtitleShowsVersionsLoadersAndSource()
    {
        var item = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.Modrinth,
            Title = "Test Mod",
            Description = "This should not be shown",
            SupportedMinecraftVersions = ["1.20.1", "1.19.4", "1.18.2", "1.17.1", "1.16.5", "1.12.2"],
            SupportedLoaders = ["forge", "fabric"],
            Downloads = 1
        }, ["1.20", "1.19", "1.18", "1.17", "1.16", "1.15", "1.14", "1.13", "1.12"]);

        Assert.Equal($"1.16+, 1.12  fabric/forge  {Strings.Resources_ModSourceModrinth}", item.Subtitle);
        Assert.DoesNotContain("This should not be shown", item.Subtitle);
    }

    [Fact]
    public void MinecraftVersionSupportFormatterUsesOfficialReleaseOrderForContinuity()
    {
        var summary = ResourceMinecraftVersionSupportFormatter.Format(
            ["26", "1.21.4", "1.20.1", "1.12.2"],
            ["26", "1.21.8", "1.20.6", "1.19.4", "1.18.2", "1.17.1", "1.16.5", "1.15.2", "1.14.4", "1.13.2", "1.12.2"]);

        Assert.Equal("1.20+, 1.12", summary);
    }

    [Fact]
    public void MinecraftVersionSupportFormatterSupportsSingleNumberVersion()
    {
        var summary = ResourceMinecraftVersionSupportFormatter.Format(["26"], ["26", "1.21.8"]);

        Assert.Equal("26", summary);
    }

    [Fact]
    public void MinecraftVersionSupportFormatterDoesNotHideVersionsWithoutOfficialOrder()
    {
        var summary = ResourceMinecraftVersionSupportFormatter.Format(["26", "1.21.4", "1.20.1", "1.12.2"]);

        Assert.Equal("26, 1.21, 1.20, 1.12", summary);
    }

    [Fact]
    public void MinecraftVersionSupportFormatterDeduplicatesPatchVersions()
    {
        var summary = ResourceMinecraftVersionSupportFormatter.Format(
            ["1.20.6", "1.20.1"],
            ["1.20.6", "1.20.1"]);

        Assert.Equal("1.20", summary);
    }

    [Fact]
    public void ModProjectItemSubtitleUsesFriendlyFallbacksWhenMetadataIsMissing()
    {
        var item = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.CurseForge,
            Title = "Unknown Mod",
            Downloads = 1
        });

        Assert.Equal(
            $"{Strings.Resources_ModVersionsUnknown}  {Strings.Resources_ModLoadersUnknown}  {Strings.Resources_ModSourceCurseForge}",
            item.Subtitle);
    }

    [Theory]
    [InlineData(9999, "9,999")]
    [InlineData(10000, "1万")]
    [InlineData(12345, "1.23万")]
    [InlineData(100000000, "1亿")]
    [InlineData(112820485, "1.13亿")]
    public void ModProjectItemFormatsDownloadsWithChineseCompactUnits(long downloads, string expectedValue)
    {
        var item = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.Modrinth,
            Title = "Test Mod",
            Downloads = downloads
        });

        Assert.Equal(string.Format(Strings.Resources_ModDownloadsFormat, expectedValue), item.TrailingText);
    }

    [Fact]
    public async Task BeginEnsureCurrentSectionLoadedStartsDefaultModPageLoadWithoutWaiting()
    {
        var dispatcher = new QueueingUiDispatcher();
        var pendingResult = new TaskCompletionSource<ResourceCatalogSearchResult>();
        var service = new FakeResourceCatalogService(pendingResult.Task);
        var viewModel = new ResourcesPageViewModel(service, uiDispatcher: dispatcher);

        viewModel.BeginEnsureCurrentSectionLoaded();

        Assert.Equal(0, service.CallCount);
        Assert.True(viewModel.ModPage.IsLoadingProjects);
        Assert.True(viewModel.ModPage.CanShowLoadingState);
        Assert.Equal(1, dispatcher.PendingCount);

        dispatcher.RunNext();
        await TestAsync.WaitForAsync(() => service.CallCount == 1);
        Assert.NotNull(service.LastRequest);
        Assert.Equal(0, service.LastRequest.Offset);
        Assert.Equal(20, service.LastRequest.PageSize);

        pendingResult.SetResult(new ResourceCatalogSearchResult
        {
            Projects =
            [
                new ResourceProject
                {
                    Source = ResourceProjectSource.Modrinth,
                    ProjectId = "iris",
                    Slug = "iris",
                    Title = "Iris",
                    Description = "Shader support",
                    Downloads = 1000,
                    ProjectUrl = "https://modrinth.com/mod/iris"
                }
            ]
        });

        await TestAsync.WaitForAsync(() => dispatcher.PendingCount == 1);
        dispatcher.RunNext();
        await TestAsync.WaitForAsync(() =>
            !viewModel.ModPage.IsLoadingProjects
            && viewModel.ModPage.VisibleProjects.Count == 1);
        Assert.Equal("Iris", viewModel.ModPage.VisibleProjects[0].Title);
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
    public async Task ModPageLoadMoreShowsLoadingStateWhilePending()
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

        Assert.True(viewModel.ModPage.IsLoadingMoreProjects);
        Assert.True(viewModel.ModPage.CanShowLoadMoreState);
        Assert.Equal(Strings.Resources_ModProjectsLoadingMore, viewModel.ModPage.LoadMoreMessage);
        Assert.Equal(
            ["P:First 0", $"F:{Strings.Resources_ModProjectsLoadingMore}"],
            FormatProjectListItemsWithFooter(viewModel.ModPage.ProjectListItems));

        dispatcher.RunNext();
        var request = await service.WaitForRequestAsync(1);
        Assert.Equal(20, request.Offset);
        var secondCall = await service.WaitForCallAsync(1);
        secondCall.SetResult(CreateProjectResult(1, "second", hasMore: false));
        await TestAsync.WaitForAsync(() => dispatcher.PendingCount == 1);
        dispatcher.RunNext();
        await loadMore;

        Assert.False(viewModel.ModPage.IsLoadingMoreProjects);
        Assert.Equal(2, viewModel.ModPage.VisibleProjects.Count);
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
    public async Task OnlineProjectPagesUseTheirOwnProjectFooterMessages()
    {
        var cases = new (Func<ResourcesPageViewModel, ResourcesModPageViewModel> Page, string NoMoreText)[]
        {
            (viewModel => viewModel.ModPage, Strings.Resources_ModProjectsNoMore),
            (viewModel => viewModel.ResourcePacksPage, Strings.Resources_ResourcePackProjectsNoMore),
            (viewModel => viewModel.ShaderPacksPage, Strings.Resources_ShaderPackProjectsNoMore),
            (viewModel => viewModel.WorldsPage, Strings.Resources_WorldProjectsNoMore),
            (viewModel => viewModel.ModpacksPage, Strings.Resources_ModpackProjectsNoMore)
        };

        foreach (var (selectPage, noMoreText) in cases)
        {
            var service = new QueueResourceCatalogService(CreateProjectResult(1, "project", hasMore: false));
            var viewModel = new ResourcesPageViewModel(service);
            var page = selectPage(viewModel);

            await page.RefreshProjectsCommand.ExecuteAsync(null);

            Assert.Equal($"F:{noMoreText}", FormatProjectListItemsWithFooter(page.ProjectListItems).Last());
        }
    }

    [Fact]
    public async Task ModPageLoadMoreDoesNotRequestWhenNoMoreProjects()
    {
        var service = new QueueResourceCatalogService(CreateProjectResult(1, "only", hasMore: false));
        var viewModel = new ResourcesPageViewModel(service);

        await viewModel.ModPage.RefreshProjectsCommand.ExecuteAsync(null);
        await viewModel.ModPage.LoadMoreProjectsCommand.ExecuteAsync(null);

        Assert.Single(service.Requests);
        Assert.False(viewModel.ModPage.HasMoreProjects);
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
    public async Task ModPageRefreshProjectsDoesNotShowCurseForgeMissingKeyWarning()
    {
        var service = new FakeResourceCatalogService(new ResourceCatalogSearchResult
        {
            Projects = [],
            IsCurseForgeUnavailable = true,
            IsCurseForgeApiKeyMissing = true
        });
        var viewModel = new ResourcesPageViewModel(service);

        await viewModel.ModPage.RefreshProjectsCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, viewModel.ModPage.PartialWarningMessage);
        Assert.False(viewModel.ModPage.HasPartialWarningMessage);
    }

    [Fact]
    public async Task ModPageLoadRendersProjectsInSmallBatches()
    {
        var dispatcher = new QueueingUiDispatcher();
        var service = new FakeResourceCatalogService(CreateProjectResult(25));
        var viewModel = new ResourcesPageViewModel(service, uiDispatcher: dispatcher);

        var loadTask = viewModel.ModPage.RefreshProjectsAsync();

        Assert.True(viewModel.ModPage.IsLoadingProjects);
        dispatcher.RunNext();
        await TestAsync.WaitForAsync(() => dispatcher.PendingCount == 1);

        dispatcher.RunNext();
        Assert.False(viewModel.ModPage.IsLoadingProjects);
        Assert.Equal(12, viewModel.ModPage.VisibleProjects.Count);
        Assert.Equal(1, viewModel.ModPage.ListEntranceAnimationToken);
        Assert.False(loadTask.IsCompleted);

        dispatcher.RunNext();
        Assert.Equal(20, viewModel.ModPage.VisibleProjects.Count);
        Assert.Equal(1, viewModel.ModPage.ListEntranceAnimationToken);
        Assert.False(loadTask.IsCompleted);

        dispatcher.RunNext();
        await loadTask;
        Assert.Equal(25, viewModel.ModPage.VisibleProjects.Count);
        Assert.Equal(1, viewModel.ModPage.ListEntranceAnimationToken);
    }

    [Fact]
    public async Task ModPageLoadRaisesEntranceAnimationTokenBeforeInitialProjectBatch()
    {
        var dispatcher = new QueueingUiDispatcher();
        var service = new FakeResourceCatalogService(CreateProjectResult(3));
        var viewModel = new ResourcesPageViewModel(service, uiDispatcher: dispatcher);
        var events = new List<string>();
        viewModel.ModPage.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ResourcesModPageViewModel.ListEntranceAnimationToken))
                events.Add("token");
        };
        viewModel.ModPage.VisibleProjects.CollectionChanged += (_, args) =>
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                events.Add("add");
        };

        var loadTask = viewModel.ModPage.RefreshProjectsAsync();
        dispatcher.RunNext();
        await TestAsync.WaitForAsync(() => dispatcher.PendingCount == 1);
        dispatcher.RunNext();
        await loadTask;

        Assert.Equal("token", events.First());
        Assert.Contains("add", events);
        Assert.Contains("token", events);
    }

    [Fact]
    public async Task ModPageCanceledBatchAppendDoesNotContinueAfterNewRefresh()
    {
        var dispatcher = new QueueingUiDispatcher();
        var service = new ControlledResourceCatalogService();
        var viewModel = new ResourcesPageViewModel(service, uiDispatcher: dispatcher);

        var oldLoad = viewModel.ModPage.RefreshProjectsAsync();
        dispatcher.RunNext();
        var oldCall = await service.WaitForCallAsync(0);
        oldCall.SetResult(CreateProjectResult(25, "old"));
        await TestAsync.WaitForAsync(() => dispatcher.PendingCount == 1);
        dispatcher.RunNext();
        Assert.Equal(12, viewModel.ModPage.VisibleProjects.Count);
        Assert.All(viewModel.ModPage.VisibleProjects, item => Assert.StartsWith("Old", item.Title));

        var newLoad = viewModel.ModPage.RefreshProjectsAsync();
        dispatcher.RunNext();
        if (service.CallCount < 2 && dispatcher.PendingCount > 0)
            dispatcher.RunNext();
        var newCall = await service.WaitForCallAsync(1);
        newCall.SetResult(CreateProjectResult(1, "new"));
        await TestAsync.WaitForAsync(() => dispatcher.PendingCount >= 1);

        while (dispatcher.PendingCount > 0)
            dispatcher.RunNext();

        await oldLoad;
        await newLoad;
        Assert.Single(viewModel.ModPage.VisibleProjects);
        Assert.Equal("New 0", viewModel.ModPage.VisibleProjects[0].Title);
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
    public async Task ModProjectDetailsHidesVanillaInstancesAndKeepsLocalDownload()
    {
        var instanceService = new FakeGameInstanceService(
        [
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

        await viewModel.ModPage.LoadInstallTargetsAsync();

        var target = Assert.Single(viewModel.ModPage.InstallTargets);
        Assert.Equal(Strings.Resources_ModInstallTargetLocal, target.Title);
        Assert.True(target.IsLocalDownload);
    }

    [Fact]
    public async Task ModProjectDetailsShowsLocalDownloadWhenThereAreNoInstances()
    {
        var viewModel = new ResourcesPageViewModel(gameInstanceService: new FakeGameInstanceService([]));

        await viewModel.ModPage.LoadInstallTargetsAsync();

        var target = Assert.Single(viewModel.ModPage.InstallTargets);
        Assert.Equal(Strings.Resources_ModInstallTargetLocal, target.Title);
        Assert.True(target.IsLocalDownload);
        Assert.Equal(string.Empty, viewModel.ModPage.InstallTargetsLoadErrorMessage);
    }

    [Fact]
    public async Task ModProjectDetailsKeepsLocalDownloadWhenInstallTargetsFail()
    {
        var viewModel = new ResourcesPageViewModel(gameInstanceService: new ThrowingGameInstanceService());

        await viewModel.ModPage.LoadInstallTargetsAsync();

        var target = Assert.Single(viewModel.ModPage.InstallTargets);
        Assert.Equal(Strings.Resources_ModInstallTargetLocal, target.Title);
        Assert.True(target.IsLocalDownload);
        Assert.Equal(Strings.Resources_ModInstallTargetsLoadError, viewModel.ModPage.InstallTargetsLoadErrorMessage);
        Assert.True(viewModel.ModPage.CanShowInstallTargetsLoadErrorState);
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
    public async Task ModInstallTargetSelectionLoadsAllVersionsForLocalDownload()
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
                        FileName = "fabric-1.18.2.jar",
                        GameVersions = ["1.18.2"],
                        Loaders = ["fabric"]
                    },
                    new ResourceProjectVersion
                    {
                        VersionId = "version-2",
                        Name = "Forge 1.18.2",
                        VersionNumber = "1.0.1",
                        VersionType = "release",
                        FileName = "forge-1.18.2.jar",
                        GameVersions = ["1.18.2"],
                        Loaders = ["Forge"]
                    },
                    new ResourceProjectVersion
                    {
                        VersionId = "version-3",
                        Name = "Fabric 1.18.1",
                        VersionNumber = "1.0.2",
                        VersionType = "release",
                        FileName = "fabric-1.18.1.jar",
                        GameVersions = ["1.18.1"],
                        Loaders = ["fabric"]
                    }
                ]
            }
        };
        var viewModel = new ResourcesPageViewModel(catalogService);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "project",
            Slug = "project",
            Title = "Project"
        });

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectInstallTargetCommand.Execute(ResourcesModInstallTargetItemViewModel.CreateLocalDownload());
        await TestAsync.WaitForAsync(() => viewModel.ModPage.AvailableVersions.Count == 3);

        Assert.Equal(ResourcesModPageStep.ProjectVersions, viewModel.ModPage.CurrentStep);
        Assert.Equal("Project", viewModel.PageTitle);
        Assert.NotNull(catalogService.LastVersionsRequest);
        Assert.True(catalogService.LastVersionsRequest.IncludeAllVersions);
        Assert.Equal(string.Empty, catalogService.LastVersionsRequest.MinecraftVersion);
        Assert.Equal(LoaderKind.Vanilla, catalogService.LastVersionsRequest.Loader);
        Assert.Equal(Strings.Resources_ModVersionsAllTitle, viewModel.ModPage.AvailableVersionsTitle);
        Assert.Equal(
            [
                "H:1.18.2-fabric",
                "V:Fabric 1.18.2",
                "H:1.18.2-forge",
                "V:Forge 1.18.2",
                "H:1.18.1-fabric",
                "V:Fabric 1.18.1"
            ],
            FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems));
        Assert.Equal(Strings.Resources_ModVersionsEmptyLocal, viewModel.ModPage.AvailableVersionsEmptyMessage);

        viewModel.ModPage.SelectedAvailableVersionFilterOption = viewModel.ModPage.AvailableVersionFilterOptions.Single(option => option.Id == "1.18.2");

        Assert.Equal(
            [
                "H:1.18.2-fabric",
                "V:Fabric 1.18.2",
                "H:1.18.2-forge",
                "V:Forge 1.18.2"
            ],
            FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems));
        Assert.Equal(2, viewModel.ModPage.VisibleAvailableVersionCount);

        viewModel.ModPage.SelectedAvailableLoaderFilterOption = viewModel.ModPage.AvailableLoaderFilterOptions.Single(option => option.Id == "forge");

        Assert.Equal(
            [
                "H:1.18.2-forge",
                "V:Forge 1.18.2"
            ],
            FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems));
        Assert.Equal(1, viewModel.ModPage.VisibleAvailableVersionCount);
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
    public async Task ModAvailableVersionsLoadMoreShowsLoadingFooterItem()
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
                        Name = "Forge 1.18.2 One",
                        VersionNumber = "1.0.0",
                        VersionType = "release",
                        FileName = "forge-1.18.2-one.jar",
                        GameVersions = ["1.18.2"],
                        Loaders = ["forge"]
                    }
                ]
            };
        var pendingResult = new TaskCompletionSource<ResourceProjectVersionsResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        catalogService.PendingVersionsResultsByRequestKey[
            FakeResourceCatalogService.CreateVersionsRequestKey(string.Empty, LoaderKind.Vanilla, 10000)] = pendingResult;
        var viewModel = new ResourcesPageViewModel(catalogService);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.CurseForge,
            ProjectId = "1234",
            Slug = "project",
            Title = "Project",
            SupportedMinecraftVersions = ["1.18.2"],
            SupportedLoaders = ["forge"]
        });

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectInstallTargetCommand.Execute(ResourcesModInstallTargetItemViewModel.CreateLocalDownload());
        await TestAsync.WaitForAsync(() => viewModel.ModPage.AvailableVersions.Count == 1);

        var loadMore = viewModel.ModPage.LoadMoreAvailableVersionsAsync();
        await TestAsync.WaitForAsync(() => viewModel.ModPage.IsLoadingMoreAvailableVersions);

        Assert.Equal(
            $"F:{Strings.Resources_ModVersionsLoadingMore}",
            FormatAvailableVersionListItemsWithFooter(viewModel.ModPage.AvailableVersionListItems).Last());

        pendingResult.SetResult(new ResourceProjectVersionsResult());
        await loadMore;
    }

    [Fact]
    public async Task CurseForgeAvailableVersionsRequestsLargeUnfilteredPage()
    {
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        catalogService.VersionsResultsByOffset[0] = new ResourceProjectVersionsResult
        {
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
                },
                new ResourceProjectVersion
                {
                    VersionId = "version-2",
                    Name = "Fabric 1.18.1",
                    VersionNumber = "1.0.1",
                    VersionType = "release",
                    FileName = "fabric-1.18.1.jar",
                    GameVersions = ["1.18.1"],
                    Loaders = ["fabric"]
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
        await TestAsync.WaitForAsync(() => viewModel.ModPage.AvailableVersions.Count == 2);

        var request = Assert.Single(catalogService.VersionRequests);
        Assert.True(request.IncludeAllVersions);
        Assert.Equal(string.Empty, request.MinecraftVersion);
        Assert.Equal(LoaderKind.Vanilla, request.Loader);
        Assert.Equal(0, request.Offset);
        Assert.Equal(10000, request.PageSize);
        Assert.Equal(
            [
                "H:1.18.2-forge",
                "V:Forge 1.18.2",
                "H:1.18.1-fabric",
                "V:Fabric 1.18.1"
            ],
            FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems));
    }

    [Fact]
    public async Task CurseForgeInstanceTargetUsesLargePageAndFiltersLocally()
    {
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        catalogService.VersionsResultsByOffset[0] = new ResourceProjectVersionsResult
        {
            Versions =
            [
                new ResourceProjectVersion
                {
                    VersionId = "forge-version",
                    Name = "Forge 1.18.2",
                    VersionNumber = "1.0.0",
                    VersionType = "release",
                    FileName = "forge.jar",
                    GameVersions = ["1.18.2"],
                    Loaders = ["forge"]
                },
                new ResourceProjectVersion
                {
                    VersionId = "fabric-version",
                    Name = "Fabric 1.18.2",
                    VersionNumber = "1.0.1",
                    VersionType = "release",
                    FileName = "fabric.jar",
                    GameVersions = ["1.18.2"],
                    Loaders = ["fabric"]
                }
            ]
        };
        var instance = new GameInstance
        {
            Id = "fabric-instance",
            Name = "Fabric Instance",
            MinecraftVersion = "1.18.2",
            Loader = LoaderKind.Fabric
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
        viewModel.ModPage.SelectInstallTargetCommand.Execute(ResourcesModInstallTargetItemViewModel.FromInstance(instance));
        await TestAsync.WaitForAsync(() => viewModel.ModPage.AvailableVersions.Count == 2);

        var request = Assert.Single(catalogService.VersionRequests);
        Assert.True(request.IncludeAllVersions);
        Assert.Equal(string.Empty, request.MinecraftVersion);
        Assert.Equal(LoaderKind.Vanilla, request.Loader);
        Assert.Equal(10000, request.PageSize);
        Assert.Equal("1.18.2", viewModel.ModPage.SelectedAvailableVersionFilterOption?.Id);
        Assert.Equal("fabric", viewModel.ModPage.SelectedAvailableLoaderFilterOption?.Id);
        Assert.Equal(["H:1.18.2-fabric", "V:Fabric 1.18.2"], FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems));
    }

    [Fact]
    public async Task CurseForgeLoadMoreRequestsOnlyNextLargePage()
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
            HasMore = true,
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

        await viewModel.ModPage.LoadMoreAvailableVersionsAsync();

        Assert.Equal(2, viewModel.ModPage.AvailableVersions.Count);
        Assert.Equal([0, 10000], catalogService.VersionRequests.Select(request => request.Offset).ToArray());
        Assert.All(catalogService.VersionRequests, request => Assert.True(request.IncludeAllVersions));
        Assert.All(catalogService.VersionRequests, request => Assert.Equal(10000, request.PageSize));
        Assert.True(viewModel.ModPage.HasMoreAvailableVersions);
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
    public async Task ModAvailableVersionsLoadMoreFailureKeepsLoadedVersions()
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
        catalogService.VersionOffsetsToThrow.Add(10000);
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

        Assert.Single(viewModel.ModPage.AvailableVersions);
        Assert.False(viewModel.ModPage.IsLoadingMoreAvailableVersions);
        Assert.Equal(Strings.Resources_ModVersionsLoadMoreError, viewModel.ModPage.AvailableVersionsLoadMoreMessage);
        Assert.True(viewModel.ModPage.CanShowAvailableVersionsLoadMoreState);
        Assert.Equal(
            $"F:{Strings.Resources_ModVersionsLoadMoreError}",
            FormatAvailableVersionListItemsWithFooter(viewModel.ModPage.AvailableVersionListItems).Last());
    }

    [Fact]
    public async Task ModAvailableVersionsLoadMoreRebuildsExistingFiltersFromExpandedSource()
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
                    Name = "Forge Build One",
                    VersionNumber = "1.0.0",
                    VersionType = "release",
                    FileName = "forge-one.jar",
                    GameVersions = ["1.18.2"],
                    Loaders = ["forge"]
                },
                new ResourceProjectVersion
                {
                    VersionId = "version-2",
                    Name = "Fabric Build",
                    VersionNumber = "1.0.0",
                    VersionType = "release",
                    FileName = "fabric.jar",
                    GameVersions = ["1.18.2"],
                    Loaders = ["fabric"]
                }
            ]
        };
        catalogService.VersionsResultsByOffset[10000] = new ResourceProjectVersionsResult
        {
            Versions =
            [
                new ResourceProjectVersion
                {
                    VersionId = "version-3",
                    Name = "Forge Build Two",
                    VersionNumber = "1.0.1",
                    VersionType = "release",
                    FileName = "forge-two.jar",
                    GameVersions = ["1.18.2"],
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
        await TestAsync.WaitForAsync(() => viewModel.ModPage.AvailableVersions.Count == 2);
        viewModel.ModPage.SelectedAvailableLoaderFilterOption = viewModel.ModPage.AvailableLoaderFilterOptions.Single(option => option.Id == "forge");
        viewModel.ModPage.AvailableVersionSearchQuery = "build";

        await viewModel.ModPage.LoadMoreAvailableVersionsAsync();

        Assert.Equal("forge", viewModel.ModPage.SelectedAvailableLoaderFilterOption?.Id);
        Assert.Equal(
            [
                "H:1.18.2-forge",
                "V:Forge Build One",
                "V:Forge Build Two"
            ],
            FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems));
        Assert.Equal(2, viewModel.ModPage.VisibleAvailableVersionCount);
    }

    [Fact]
    public async Task ModAvailableVersionsLoadMoreKeepsMismatchedItemsHiddenUntilFiltersChange()
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
                    Name = "Forge Build",
                    VersionNumber = "1.0.0",
                    VersionType = "release",
                    FileName = "forge.jar",
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
                    Name = "Fabric Build",
                    VersionNumber = "1.0.1",
                    VersionType = "release",
                    FileName = "fabric.jar",
                    GameVersions = ["1.18.2"],
                    Loaders = ["fabric"]
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
        viewModel.ModPage.SelectedAvailableLoaderFilterOption = viewModel.ModPage.AvailableLoaderFilterOptions.Single(option => option.Id == "forge");
        var visibleItemsBeforeLoadMore = FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems);

        await viewModel.ModPage.LoadMoreAvailableVersionsAsync();

        Assert.Equal(2, viewModel.ModPage.AvailableVersions.Count);
        Assert.Equal(visibleItemsBeforeLoadMore, FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems));
        Assert.Equal(1, viewModel.ModPage.VisibleAvailableVersionCount);

        viewModel.ModPage.SelectedAvailableLoaderFilterOption = viewModel.ModPage.AvailableLoaderFilterOptions.Single(option => option.Id == "fabric");

        Assert.Equal(
            [
                "H:1.18.2-fabric",
                "V:Fabric Build"
            ],
            FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems));
    }

    [Fact]
    public async Task ModAvailableVersionsLoadMoreAppendsExistingGroupWithoutDuplicateHeader()
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
                    Name = "Forge 1.18.2 One",
                    VersionNumber = "1.0.0",
                    VersionType = "release",
                    FileName = "forge-1.18.2-one.jar",
                    GameVersions = ["1.18.2"],
                    Loaders = ["forge"]
                },
                new ResourceProjectVersion
                {
                    VersionId = "version-2",
                    Name = "Forge 1.18.1",
                    VersionNumber = "1.0.0",
                    VersionType = "release",
                    FileName = "forge-1.18.1.jar",
                    GameVersions = ["1.18.1"],
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
                    VersionId = "version-3",
                    Name = "Forge 1.18.2 Two",
                    VersionNumber = "1.0.1",
                    VersionType = "release",
                    FileName = "forge-1.18.2-two.jar",
                    GameVersions = ["1.18.2"],
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
        await TestAsync.WaitForAsync(() => viewModel.ModPage.AvailableVersions.Count == 2);
        var entranceAnimationToken = viewModel.ModPage.AvailableVersionListEntranceAnimationToken;

        await viewModel.ModPage.LoadMoreAvailableVersionsAsync();

        Assert.Equal(entranceAnimationToken, viewModel.ModPage.AvailableVersionListEntranceAnimationToken);
        Assert.Equal(
            [
                "H:1.18.2-forge",
                "V:Forge 1.18.2 One",
                "V:Forge 1.18.2 Two",
                "H:1.18.1-forge",
                "V:Forge 1.18.1"
            ],
            FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems));
    }

    [Fact]
    public async Task ModAvailableVersionSearchFiltersLoadedVersionList()
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
                        Name = "Fabric Build",
                        VersionNumber = "1.0.0",
                        VersionType = "release",
                        FileName = "project-1.18.2-fabric.jar",
                        GameVersions = ["1.18.2"],
                        Loaders = ["fabric"]
                    },
                    new ResourceProjectVersion
                    {
                        VersionId = "version-2",
                        Name = "Forge Build",
                        VersionNumber = "1.0.1",
                        VersionType = "release",
                        FileName = "project-1.18.2-forge.jar",
                        GameVersions = ["1.18.2"],
                        Loaders = ["forge"]
                    }
                ]
            }
        };
        var viewModel = new ResourcesPageViewModel(catalogService);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "project",
            Slug = "project",
            Title = "Project"
        });

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectInstallTargetCommand.Execute(ResourcesModInstallTargetItemViewModel.CreateLocalDownload());
        await TestAsync.WaitForAsync(() => viewModel.ModPage.AvailableVersions.Count == 2);

        viewModel.ModPage.AvailableVersionSearchQuery = "forge.jar";

        Assert.Equal(
            [
                "H:1.18.2-forge",
                "V:Forge Build"
            ],
            FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems));
        Assert.Equal(1, viewModel.ModPage.VisibleAvailableVersionCount);

        viewModel.ModPage.AvailableVersionSearchQuery = "missing";

        Assert.Equal(0, viewModel.ModPage.VisibleAvailableVersionCount);
        Assert.Equal(Strings.Resources_ModVersionsFilterEmpty, viewModel.ModPage.AvailableVersionsEmptyMessage);
        Assert.True(viewModel.ModPage.CanShowAvailableVersionsEmptyState);
    }

    [Fact]
    public async Task ModInstallTargetSelectionRepeatsAllVersionItemsForEachCompatibilityGroup()
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
                        Name = "Multi Loader Version",
                        VersionNumber = "1.0.0",
                        VersionType = "release",
                        FileName = "multi-loader.jar",
                        GameVersions = ["1.18.2", "1.18.1"],
                        Loaders = ["fabric", "forge"]
                    }
                ]
            }
        };
        var viewModel = new ResourcesPageViewModel(catalogService);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "project",
            Slug = "project",
            Title = "Project"
        });

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectInstallTargetCommand.Execute(ResourcesModInstallTargetItemViewModel.CreateLocalDownload());
        await TestAsync.WaitForAsync(() => viewModel.ModPage.AvailableVersions.Count == 1);

        Assert.Equal(
            [
                "H:1.18.2-fabric",
                "V:Multi Loader Version",
                "H:1.18.2-forge",
                "V:Multi Loader Version",
                "H:1.18.1-fabric",
                "V:Multi Loader Version",
                "H:1.18.1-forge",
                "V:Multi Loader Version"
            ],
            FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems));

        viewModel.ModPage.SelectedAvailableVersionFilterOption = viewModel.ModPage.AvailableVersionFilterOptions.Single(option => option.Id == "1.18.1");
        viewModel.ModPage.SelectedAvailableLoaderFilterOption = viewModel.ModPage.AvailableLoaderFilterOptions.Single(option => option.Id == "forge");

        Assert.Equal(
            [
                "H:1.18.1-forge",
                "V:Multi Loader Version"
            ],
            FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems));
        Assert.Equal(1, viewModel.ModPage.VisibleAvailableVersionCount);
    }

    [Fact]
    public async Task ModInstallTargetSelectionUsesUnknownCompatibilityTitleWhenAllVersionMetadataIsMissing()
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
                        Name = "Version 1",
                        VersionNumber = "1.0.0",
                        VersionType = "release",
                        FileName = "version-1.jar"
                    }
                ]
            }
        };
        var viewModel = new ResourcesPageViewModel(catalogService);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "project",
            Slug = "project",
            Title = "Project"
        });

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectInstallTargetCommand.Execute(ResourcesModInstallTargetItemViewModel.CreateLocalDownload());
        await TestAsync.WaitForAsync(() => viewModel.ModPage.AvailableVersions.Count == 1);

        Assert.Equal(
            [$"H:{Strings.Resources_ModVersionsUnknown}-{Strings.Resources_ModLoadersUnknown}", "V:Version 1"],
            FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems));
    }

    [Fact]
    public async Task ModInstallTargetSelectionIgnoresEnvironmentTagsAndInfersLoaderFromFileName()
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
                        Name = "30.2.0.15 for NeoForge 26.2",
                        VersionNumber = "30.2.0.15",
                        VersionType = "beta",
                        FileName = "jei-26.2-neoforge-30.2.0.15.jar",
                        GameVersions = ["Client", "Server", "26.2"]
                    },
                    new ResourceProjectVersion
                    {
                        VersionId = "version-2",
                        Name = "30.2.0.15 for Fabric 26.2",
                        VersionNumber = "30.2.0.15",
                        VersionType = "beta",
                        FileName = "jei-26.2-fabric-30.2.0.15.jar",
                        GameVersions = ["client", "server", "26.2"]
                    }
                ]
            }
        };
        var viewModel = new ResourcesPageViewModel(catalogService);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.CurseForge,
            ProjectId = "project",
            Slug = "project",
            Title = "Project"
        });

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectInstallTargetCommand.Execute(ResourcesModInstallTargetItemViewModel.CreateLocalDownload());
        await TestAsync.WaitForAsync(() => viewModel.ModPage.AvailableVersions.Count == 2);

        Assert.Equal(
            [
                "H:26.2-neoforge",
                "V:30.2.0.15 for NeoForge 26.2",
                "H:26.2-fabric",
                "V:30.2.0.15 for Fabric 26.2"
            ],
            FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems));
    }

    [Fact]
    public async Task ModInstallTargetSelectionUsesLoaderTagFromGameVersionsWhenLoaderMetadataIsMissing()
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
                        Name = "Version 1",
                        VersionNumber = "1.0.0",
                        VersionType = "release",
                        FileName = "version-1.jar",
                        GameVersions = ["1.18.2", "Fabric"]
                    }
                ]
            }
        };
        var viewModel = new ResourcesPageViewModel(catalogService);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.CurseForge,
            ProjectId = "project",
            Slug = "project",
            Title = "Project"
        });

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectInstallTargetCommand.Execute(ResourcesModInstallTargetItemViewModel.CreateLocalDownload());
        await TestAsync.WaitForAsync(() => viewModel.ModPage.AvailableVersions.Count == 1);

        Assert.Equal(
            ["H:1.18.2-fabric", "V:Version 1"],
            FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems));
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
    public async Task ModInstallTargetSelectionKeepsInstanceFiltersWhenNoVersionsMatch()
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
                        Name = "Forge 1.18.2",
                        VersionNumber = "1.0.0",
                        VersionType = "release",
                        FileName = "forge-1.18.2.jar",
                        GameVersions = ["1.18.2"],
                        Loaders = ["forge"]
                    }
                ]
            }
        };
        var viewModel = new ResourcesPageViewModel(catalogService);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.CurseForge,
            ProjectId = "1234",
            Title = "Project"
        });
        var target = ResourcesModInstallTargetItemViewModel.FromInstance(new GameInstance
        {
            Id = "fabric-instance",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Fabric
        });

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectInstallTargetCommand.Execute(target);
        await TestAsync.WaitForAsync(() => viewModel.ModPage.AvailableVersions.Count == 1);

        Assert.Equal(ResourcesModPageStep.ProjectVersions, viewModel.ModPage.CurrentStep);
        Assert.Equal("1.20.1", viewModel.ModPage.SelectedAvailableVersionFilterOption?.Id);
        Assert.Equal("fabric", viewModel.ModPage.SelectedAvailableLoaderFilterOption?.Id);
        Assert.Contains(viewModel.ModPage.AvailableVersionFilterOptions, option => option.Id == "1.20.1");
        Assert.Contains(viewModel.ModPage.AvailableLoaderFilterOptions, option => option.Id == "fabric");
        Assert.Equal([$"H:{viewModel.ModPage.AvailableVersionsTitle}"], FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems));
        Assert.Equal(0, viewModel.ModPage.VisibleAvailableVersionCount);
        Assert.Equal(Strings.Resources_ModVersionsFilterEmpty, viewModel.ModPage.AvailableVersionsEmptyMessage);
        Assert.True(viewModel.ModPage.CanShowAvailableVersionsEmptyState);

        viewModel.ModPage.SelectedAvailableVersionFilterOption = viewModel.ModPage.AvailableVersionFilterOptions.Single(option => option.Id == "1.18.2");
        viewModel.ModPage.SelectedAvailableLoaderFilterOption = viewModel.ModPage.AvailableLoaderFilterOptions.Single(option => option.Id == "forge");

        Assert.Equal(["H:1.18.2-forge", "V:Forge 1.18.2"], FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems));
    }

    [Theory]
    [InlineData(LoaderKind.Forge, "forge")]
    [InlineData(LoaderKind.NeoForge, "neoforge")]
    [InlineData(LoaderKind.Quilt, "quilt")]
    public async Task ModInstallTargetSelectionKeepsInstanceLoaderWhenLoaderHasNoMatches(
        LoaderKind instanceLoader,
        string expectedLoaderId)
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
                        FileName = "fabric-1.18.2.jar",
                        GameVersions = ["1.18.2"],
                        Loaders = ["fabric"]
                    }
                ]
            }
        };
        var viewModel = new ResourcesPageViewModel(catalogService);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "project",
            Slug = "project",
            Title = "Project"
        });
        var target = ResourcesModInstallTargetItemViewModel.FromInstance(new GameInstance
        {
            Id = $"{expectedLoaderId}-instance",
            MinecraftVersion = "1.18.2",
            Loader = instanceLoader
        });

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectInstallTargetCommand.Execute(target);
        await TestAsync.WaitForAsync(() => viewModel.ModPage.AvailableVersions.Count == 1);

        Assert.Equal("1.18.2", viewModel.ModPage.SelectedAvailableVersionFilterOption?.Id);
        Assert.Equal(expectedLoaderId, viewModel.ModPage.SelectedAvailableLoaderFilterOption?.Id);
        Assert.Contains(viewModel.ModPage.AvailableLoaderFilterOptions, option => option.Id == expectedLoaderId);
        Assert.Equal([$"H:{viewModel.ModPage.AvailableVersionsTitle}"], FormatAvailableVersionListItems(viewModel.ModPage.AvailableVersionListItems));
        Assert.Equal(0, viewModel.ModPage.VisibleAvailableVersionCount);
        Assert.Equal(Strings.Resources_ModVersionsFilterEmpty, viewModel.ModPage.AvailableVersionsEmptyMessage);
    }

    [Fact]
    public async Task ModVersionsBackReturnsToProjectDetails()
    {
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        var viewModel = new ResourcesPageViewModel(catalogService);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "project",
            Title = "Project"
        });
        var target = ResourcesModInstallTargetItemViewModel.FromInstance(new GameInstance
        {
            Id = "fabric-instance",
            MinecraftVersion = "1.18.2",
            Loader = LoaderKind.Fabric
        });

        viewModel.ModPage.SelectProjectCommand.Execute(project);
        viewModel.ModPage.SelectInstallTargetCommand.Execute(target);
        await TestAsync.WaitForAsync(() => !viewModel.ModPage.IsLoadingAvailableVersions);

        viewModel.ModPage.BackToProjectListCommand.Execute(null);

        Assert.Equal(ResourcesModPageStep.ProjectDetails, viewModel.ModPage.CurrentStep);
        Assert.Same(project, viewModel.ModPage.SelectedProject);
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
    public async Task WorldVersionSelectionInstallsVersionToSelectedInstanceWithWorldMessages()
    {
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        var statusService = new FakeStatusService();
        var downloadTasksPage = new DownloadTasksPageViewModel();
        var instance = new GameInstance
        {
            Id = "world-instance",
            Name = "World Test",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Vanilla,
            InstanceDirectory = "C:\\Instances\\World Test"
        };
        var version = new ResourceProjectVersion
        {
            Kind = ResourceProjectKind.World,
            VersionId = "version-1",
            Name = "Version 1",
            VersionNumber = "1.0.0",
            FileName = "version-1.zip",
            PrimaryDownloadUrl = "https://example.test/version-1.zip"
        };
        var viewModel = new ResourcesPageViewModel(
            catalogService,
            statusService: statusService,
            downloadTasksPage: downloadTasksPage);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Kind = ResourceProjectKind.World,
            Source = ResourceProjectSource.CurseForge,
            ProjectId = "project",
            Title = "World Project"
        });
        viewModel.WorldsPage.SelectProjectCommand.Execute(project);
        viewModel.WorldsPage.SelectInstallTargetCommand.Execute(ResourcesModInstallTargetItemViewModel.FromInstance(instance));

        await viewModel.WorldsPage.InstallAvailableVersionCommand.ExecuteAsync(new ResourcesModVersionItemViewModel(version, project));

        Assert.Same(version, catalogService.LastInstalledVersion);
        Assert.Same(instance, catalogService.LastInstallInstance);
        var task = Assert.Single(downloadTasksPage.Tasks);
        Assert.Equal("Version 1", task.Title);
        Assert.Equal("World Test", task.Subtitle);
        Assert.Equal(DownloadTaskState.Completed, task.State);
        Assert.Contains(string.Format(Strings.Status_WorldDownloadingFormat, "Version 1"), statusService.Messages);
        Assert.Contains(string.Format(Strings.Status_WorldInstalledFormat, "World Project"), statusService.Messages);
    }

    [Fact]
    public async Task WorldVersionSelectionReportsWorldInstallFailure()
    {
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult())
        {
            ThrowOnInstall = true
        };
        var statusService = new FakeStatusService();
        var floatingMessageService = new FakeFloatingMessageService();
        var downloadTasksPage = new DownloadTasksPageViewModel();
        var instance = new GameInstance
        {
            Id = "world-instance",
            Name = "World Test",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Vanilla,
            InstanceDirectory = "C:\\Instances\\World Test"
        };
        var version = new ResourceProjectVersion
        {
            Kind = ResourceProjectKind.World,
            VersionId = "version-1",
            Name = "Version 1",
            VersionNumber = "1.0.0",
            FileName = "version-1.zip",
            PrimaryDownloadUrl = "https://example.test/version-1.zip"
        };
        var viewModel = new ResourcesPageViewModel(
            catalogService,
            statusService: statusService,
            floatingMessageService: floatingMessageService,
            downloadTasksPage: downloadTasksPage);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Kind = ResourceProjectKind.World,
            Source = ResourceProjectSource.CurseForge,
            ProjectId = "project",
            Title = "World Project"
        });
        viewModel.WorldsPage.SelectProjectCommand.Execute(project);
        viewModel.WorldsPage.SelectInstallTargetCommand.Execute(ResourcesModInstallTargetItemViewModel.FromInstance(instance));

        await viewModel.WorldsPage.InstallAvailableVersionCommand.ExecuteAsync(new ResourcesModVersionItemViewModel(version, project));

        Assert.Contains(Strings.Status_WorldInstallFailed, statusService.Messages);
        Assert.Contains(Strings.Status_WorldInstallFailed, floatingMessageService.Messages);
        var task = Assert.Single(downloadTasksPage.Tasks);
        Assert.Equal(DownloadTaskState.Failed, task.State);
        Assert.Equal(Strings.Status_WorldInstallFailed, task.StatusMessage);
    }

    [Fact]
    public async Task ModVersionSelectionDoesNothingWhenLocalFolderPickerIsCanceled()
    {
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        var downloadTasksPage = new DownloadTasksPageViewModel();
        var viewModel = new ResourcesPageViewModel(
            catalogService,
            filePickerService: new FakeFilePickerService(),
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

        Assert.Null(catalogService.LastDownloadedVersion);
        Assert.Null(catalogService.LastDownloadDirectory);
        Assert.Empty(downloadTasksPage.Tasks);
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
    public async Task ModVersionSelectionShowsFloatingMessageWhenInstallingToInstance()
    {
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult());
        var floatingMessageService = new FakeFloatingMessageService();
        var instance = new GameInstance
        {
            Id = "fabric-instance",
            MinecraftVersion = "1.18.2",
            Loader = LoaderKind.Fabric,
            InstanceDirectory = "C:\\Instances\\Fabric Test"
        };
        var viewModel = new ResourcesPageViewModel(
            catalogService,
            floatingMessageService: floatingMessageService);
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

        Assert.Same(version, catalogService.LastInstalledVersion);
        Assert.Contains(Strings.Status_ModDownloading, floatingMessageService.Messages);
    }

    [Fact]
    public async Task ModVersionSelectionReportsFloatingFailureWhenInstanceInstallFails()
    {
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult())
        {
            ThrowOnInstall = true
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

        var task = Assert.Single(downloadTasksPage.Tasks);
        Assert.Equal(DownloadTaskState.Failed, task.State);
        Assert.Equal(Strings.Status_ModInstallFailed, task.StatusMessage);
        Assert.Contains(Strings.Status_ModInstallFailed, floatingMessageService.Messages);
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

    [Fact]
    public async Task ModVersionSelectionCancelsRunningDownloadTask()
    {
        var pendingDownload = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalogService = new FakeResourceCatalogService(new ResourceCatalogSearchResult())
        {
            PendingDownload = pendingDownload
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
        var installTask = viewModel.ModPage.InstallAvailableVersionCommand.ExecuteAsync(new ResourcesModVersionItemViewModel(version, project));
        await TestAsync.WaitForAsync(() => downloadTasksPage.Tasks.Count == 1);

        var task = downloadTasksPage.Tasks.Single();
        downloadTasksPage.CancelTask(task);
        await installTask;

        Assert.Empty(downloadTasksPage.Tasks);
        Assert.True(catalogService.LastDownloadCancellationToken.IsCancellationRequested);
        Assert.Null(catalogService.LastDownloadedVersion);
        Assert.DoesNotContain(statusService.Messages, message => message == string.Format(Strings.Status_ModDownloadedFormat, "version-1.jar"));
        Assert.DoesNotContain(Strings.Status_ModDownloadFailed, floatingMessageService.Messages);
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

        public ResourceProjectVersionsRequest? LastVersionsRequest { get; private set; }

        public List<ResourceProjectVersionsRequest> VersionRequests { get; } = [];

        public ResourceProjectVersionsResult VersionsResult { get; init; } = new();

        public Dictionary<int, ResourceProjectVersionsResult> VersionsResultsByOffset { get; } = [];

        public Dictionary<string, ResourceProjectVersionsResult> VersionsResultsByRequestKey { get; } = [];

        public Dictionary<string, TaskCompletionSource<ResourceProjectVersionsResult>> PendingVersionsResultsByRequestKey { get; } = [];

        public HashSet<int> VersionOffsetsToThrow { get; } = [];

        public ResourceProjectVersion? LastInstalledVersion { get; private set; }

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

            if (VersionsResultsByRequestKey.TryGetValue(CreateVersionsRequestKey(request), out var keyedResult))
                return Task.FromResult(keyedResult);

            if (PendingVersionsResultsByRequestKey.TryGetValue(CreateVersionsRequestKey(request), out var pendingResult))
                return pendingResult.Task;

            if (VersionsResultsByOffset.TryGetValue(request.Offset, out var result))
                return Task.FromResult(result);

            return Task.FromResult(VersionsResult);
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
